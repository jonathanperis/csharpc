# 08 — csharpc as a drop-in toolchain

The goal is for an ordinary .NET project to become a native executable through csharpc
without changing its code or project layout, the way `PublishAot=true` works for NativeAOT. This
page covers what works today, how it's built, and what "drop-in" still lacks.

**Target and end state** ([ADR 0016](adr/0016-nativeaot-parity-and-end-state.md)): drop-in means
**100 % parity with NativeAOT** — everything that works under `PublishAot=true` works with csharpc,
with the same behaviour; what NativeAOT can't do (run-time code generation) stays an explicit
diagnostic. `Csharpc.Sdk` honours NativeAOT's MSBuild properties under their NativeAOT names as it
gains them (a property it doesn't honour yet is a warning, not silently ignored). The end state is a
fork of dotnet in which the `PublishAot` step runs csharpc instead of ILCompiler, with the same inputs,
parameters and outputs (roadmap M8).

## Usage

### 1. CLI on a project

```sh
csharpc path/to/App.csproj            # -> path/to/bin/csharpc/App
csharpc path/to/dir/                  # the single .csproj in that directory
csharpc                               # the single .csproj in the current directory
csharpc App.csproj -o out/            # -> out/App   (trailing '/' or an existing directory)
csharpc App.csproj -o app.native      # explicit file name
csharpc App.csproj -c Release         # Release symbols/properties (Debug is the default, as in dotnet build); -O/-g are separate
csharpc App.csproj -p:TargetFramework=net11.0 -p:Foo=Bar   # MSBuild global properties
csharpc App.csproj --emit llvm-ir     # every --emit kind works with projects too
```

Loose files keep working as before (`csharpc a.cs b.cs -o app`).

### 2. `dotnet build` / `dotnet run` / `dotnet publish`

Add the MSBuild package (`src/Csharpc.Sdk`):

```xml
<PackageReference Include="Csharpc.Sdk" Version="0.1.0-alpha" PrivateAssets="all" />
```

```sh
dotnet build   -p:UseCsharpc=true      # native exe replaces the apphost: bin/<cfg>/<tfm>/App
dotnet run     -p:UseCsharpc=true      # ...so `dotnet run` runs the native image
dotnet publish -p:PublishCsharpc=true  # publish dir holds one native executable (like PublishAot)
```

Both properties can be set in the project file instead (`<UseCsharpc>true</UseCsharpc>`).
Optional knobs:

| Property | Meaning |
|---|---|
| `CsharpcArgs` | extra compiler flags, e.g. `-O3 -v` |
| `CsharpcCompilerPath` | use a locally built `csharpc.dll` instead of the one in the package |
| `CsharpcOutputPath` | where `dotnet build` puts the native binary (default: the apphost path) |
| `CsharpcConfiguration` | override the `-c` passed to csharpc (default `$(Configuration)`) |
| `CsharpcKeepManagedPublishOutput` | `true` keeps the managed `*.dll` files in the publish dir |

You can skip the package by importing the targets directly, which is what the repo's tests do:

```sh
dotnet build -p:UseCsharpc=true \
  -p:CustomAfterMicrosoftCommonTargets=$REPO/src/Csharpc.Sdk/build/Csharpc.Sdk.targets \
  -p:CsharpcCompilerPath=$REPO/src/Csharpc/bin/Release/net11.0/csharpc.dll
```

### 3. Packages

```sh
dotnet pack src/Csharpc.Sdk -o artifacts/packages   # Csharpc.Sdk: build/*.targets + tools/csharpc/**
dotnet pack src/Csharpc     -o artifacts/packages   # csharpc: a .NET tool
dotnet tool install -g csharpc --add-source artifacts/packages --prerelease
```

Both packages carry `src/runtime/*.c`. csrt is compiled on the user's machine on first use and
cached in `~/.cache/csharpc`, so the packages are architecture-neutral. You need `clang`, or
`CSHARPC_CC`, plus libLLVM 18–20.

Published builds (`.github/workflows/release.yml`, after green CI on `main`, and on `v*` tags) push
RID-specific NativeAOT **tool** packages (`csharpc.<rid>` plus a pointer `csharpc` package) to
GitHub Packages, and `v*` tags (only) to nuget.org — the same binaries as the
[GitHub Release](https://github.com/jonathanperis/csharpc/releases) archives (libLLVM under `lib/`).
`dotnet tool install -g csharpc` therefore installs a ready native compiler for the host RID; clang
is still required on the machine to compile csrt and link. nuget.org OIDC lives in `release.yml`
(no GitHub environment; variable `NUGET_USER`). See the README Install section.


## How it works

### Project evaluation (`src/Csharpc/Driver/ProjectLoader.cs`)

csharpc doesn't try to interpret project files itself. It asks MSBuild:

```sh
dotnet msbuild App.csproj -p:Configuration=Release \
  -getProperty:AssemblyName -getProperty:DefineConstants -getProperty:LangVersion ... \
  -getItem:Compile -getItem:Using -getItem:ProjectReference -getItem:PackageReference
```

That's a JSON evaluation dump, available since .NET 8. As a result, globs,
`Directory.Build.props/targets`, conditions, SDK defaults (`TRACE;DEBUG`, implicit usings) and
`<Compile Remove>` are exactly what `dotnet build` sees. From the result:

- `Compile` items become the source files.
- `Using` items (`<Using Include="X" Alias="Y" Static="true"/>`, including the SDK's implicit usings)
  become a synthesized `global using` file. This is what the SDK's `GenerateGlobalUsings` target
  writes to `obj/` during a real build; csharpc derives it from evaluation, so no build is needed.
- `DefineConstants`, `LangVersion`, `Nullable`, `AllowUnsafeBlocks` and `AssemblyName` configure
  Roslyn.
- `OutputType` must be `Exe`/`WinExe` for `--emit exe`.
- `ProjectReference`s are evaluated recursively, and their sources are **folded into the same
  compilation**. csharpc is a closed-world, whole-program compiler (ADR 0006). A library isn't a
  separate binary artifact; it's more code in the one LLVM module, which LLVM can inline across.
  Global usings are unioned across projects, because they're compilation-wide in a single
  compilation. Language settings come from the root project.
- `<Reference>` items (with a `HintPath`, or a `.dll` path) and `PackageReference`s are assemblies
  without sources. For packages csharpc runs `dotnet msbuild -restore -t:ResolvePackageAssets` and
  takes `RuntimeCopyLocalItems`, the implementation DLLs `dotnet build` would copy next to the app.
  All of them are bound by Roslyn and compiled from IL on demand ([ADR 0010](adr/0010-il-frontend.md)).

### MSBuild targets (`src/Csharpc.Sdk/build/Csharpc.Sdk.targets`)

- **`CsharpcCompile`** runs `AfterTargets="Build"`. The normal `CoreCompile` still runs, so
  diagnostics, analyzers, IDE behaviour and the managed `App.dll` are unaffected. The target runs
  csharpc on `$(MSBuildProjectFullPath)` and writes `obj/<cfg>/<tfm>/csharpc/App`. It's
  incremental: its inputs are `@(Compile)`, `@(ReferencePath)` (so changes in referenced projects
  count), the project file and the compiler.
- **`CsharpcCopyToOutput`** copies the native image over the apphost on every build, because the
  regular build may have refreshed it. `RunCommand` already points at the apphost, so
  `dotnet run -p:UseCsharpc=true` runs native code with no further integration.
- Library projects are skipped silently. When a global `UseCsharpc=true` flows into referenced
  projects, they contribute their sources to the executable instead.
- **`CsharpcPublish`** runs `AfterTargets="Publish"`. It writes `$(PublishDir)App` and deletes the
  managed payload (`*.dll`, `*.deps.json`, `*.runtimeconfig.json`, `*.pdb`), the same UX as
  NativeAOT.

### Diagnostics and exit codes

Messages use MSBuild's canonical format (`file(line,col): error ID: message`,
`csharpc: error : message`), so MSBuild, IDEs and CI parse them. They're colored on a TTY
unless `NO_COLOR` is set. Exit codes:

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | compile errors |
| 2 | bad command line |
| 70 | internal compiler error |

## Tests

`tests/projects.sh` builds `tests/projects/HelloProject` four ways and compares each against
`dotnet run`. The sample has multiple files, file-scoped namespaces, a partial class split
across files, `#if` on a `DefineConstants` symbol, `<Using>` items including an alias, and a
`ProjectReference` to `tests/projects/MathLib`. The four ways:

1. `csharpc HelloProject.csproj`
2. `dotnet build -p:UseCsharpc=true`. The test checks that the output really is the native ELF
   and not the apphost.
3. `dotnet run -p:UseCsharpc=true`
4. `dotnet pack` Csharpc.Sdk into a temporary local feed, `dotnet add package`, then
   `dotnet publish -p:PublishCsharpc=true`. The test checks that the publish directory contains
   only the native executable.

## Known limits of the drop-in

The **toolchain** side is drop-in: project files, multi-project solutions, `dotnet build/run/publish`, packaging and
diagnostics work. What differs from `PublishAot` is the language and library side, and the open items are tracked in
[06-roadmap.md](06-roadmap.md). In rough order of impact:

1. **BCL coverage.** User code binds against the real .NET reference assemblies, so every API resolves, but only the
   members csharpc implements compile (`csharpc --intrinsics` lists those mapped to the runtime). The library surface is
   the C# corelib compiled together with the program ([12-corelib.md](12-corelib.md)): calls bound to the
   reference-assembly symbols are redirected to it by matching signatures, and framework assemblies csharpc implements
   none of are compiled from the shared framework's IL ([ADR 0013](adr/0013-framework-il.md)). A missing member is a
   `CSLLVM0002` diagnostic; the ranked gaps are the roadmap's M2 list.
2. **NuGet packages.** Binary packages are IL; the IL front-end compiles the parts a program reaches
   ([ADR 0010](adr/0010-il-frontend.md)), with RID-specific assemblies and native assets selected for the target. What
   still fails is mostly BCL surface the packages call (the same members a source program would need) and the shapes
   listed under M5 in the roadmap.
3. **Runtime services a native closed world can't offer directly:** `Assembly.Load`, `System.Reflection.Emit` and run-time
   code generation. NativeAOT has the same constraints, and the answer is the same: reflection over compile-time metadata
   for what is statically knowable ([ADR 0018](adr/0018-reflection-metadata.md)), `dynamic` bound at compile time
   ([ADR 0019](adr/0019-dynamic.md)), and diagnostics for the rest.
4. **Source generators and analyzers.** Analyzers run in the regular `CoreCompile` and don't affect the native build.
   Source generators run inside csharpc (Roslyn's `GeneratorDriver`) with the inputs csc gets from the project: its
   analyzers, additional files, analyzer config and features, read from the csc command line of the SDK's `Compile`
   target (for each referenced project too, run on that project's own compilation)
   ([16-interop-io.md](16-interop-io.md#libraryimport-and-other-source-generators)). The SDK's JSON and
   `LibraryImport` generators and the regex generator are built into csharpc.
5. **Platforms.** `--target` passes the triple through; Linux x86-64 and ARM64, macOS ARM64, Windows x64 and
   `wasm32-wasip1` are tested in CI ([06-roadmap.md](06-roadmap.md)). A
   native toolchain is required on the host: clang (and lld when available), LLVM-MinGW on Windows, the WASI SDK for
   WebAssembly.
6. **Build performance.** A release build of csharpc is ReadyToRun-compiled and a compilation is multi-threaded; hello
   world compiles in about a second at `-O0`. Project input adds one `dotnet msbuild` evaluation per project, and the
   compile time of large programs is dominated by LLVM ([ADR 0014](adr/0014-compile-time.md)).
