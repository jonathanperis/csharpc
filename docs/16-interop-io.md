# 16 — Native interop (P/Invoke) and System.IO

csharpc programs can call native code the same way .NET programs do, and `System.IO` is
implemented in C# on top of that: the corelib shadows in `src/Csharpc/corelib/IO` and
`src/Csharpc/corelib/Text` call libc through `[DllImport]`, exactly like user code would.

## P/Invoke

### `[DllImport]`

`static extern` methods with `[DllImport]` (and local `static extern` functions, which the
LibraryImport generator emits) are lowered by `ModuleCodeGen.PInvoke.cs` into a stub with the
managed signature that marshals arguments, calls the native function and converts the result.

| Managed parameter | Native |
|---|---|
| primitives, pointers, `nint`/`nuint`, enums, function pointers | passed as-is |
| `bool` | 4-byte Win32 `BOOL` by default; `[MarshalAs(I1/U1)]` → 1 byte, `VariantBool` → 2 bytes (0 / -1) |
| `char` | 1 byte (ANSI) by default, 2 bytes with `CharSet.Unicode` or `[MarshalAs(U2)]` |
| `string` | NUL-terminated UTF-8 copy (`LPStr`/`LPUTF8Str`, default on Unix) or UTF-16 (`LPWStr`, `CharSet.Unicode`); freed after the call |
| blittable arrays (`int[]`, `byte[]`...) | pointer to the first element (the GC never moves objects) |
| `ref`/`out`/`in` | pointer to the variable |
| blittable structs | SysV x86-64 classification: ≤16 bytes split into INTEGER/SSE eightbytes, larger structs `byval` / `sret` |

Return values: primitives, `bool` (non-zero), structs (register pairs or `sret`), and `string`
(converted from UTF-8/UTF-16 and freed with `free`, as .NET does).

`SetLastError = true` clears `errno` before the call and saves it afterwards
(`Marshal.GetLastPInvokeError`, `Marshal.GetLastSystemError`, `Marshal.GetLastWin32Error`).

Delegate parameters are not marshalled; pass callbacks as `delegate* unmanaged<...>` pointing to
an `[UnmanagedCallersOnly]` method (`&Compare`) — that is the supported, allocation-free path in
.NET too.

### Binding

* **Direct link** — `libc`, `c`, `libm`, `m`, `libdl`, `libpthread` (with or without `.so`/version
  suffix), `__Internal` and `csrt` are resolved by the linker. The symbol is declared weak, so a
  missing entry point compiles and throws `EntryPointNotFoundException` at the call, as in .NET.
* **Everything else** is loaded lazily with `dlopen` on first call and cached per call site.
  Probing follows .NET: the name as given, `name.so`, `libname.so`, `name`, `libname`. Failures
  throw `DllNotFoundException` / `EntryPointNotFoundException` with .NET's messages.

`NativeLibrary.Load/TryLoad/GetExport/TryGetExport/Free`, `Marshal.AllocHGlobal/FreeHGlobal`,
`Marshal.StringToHGlobalAnsi/Uni`, `PtrToStringUTF8/Ansi/Uni`, `Read*/Write*`, `Copy`,
`SizeOf`, `PtrToStructure`, `StructureToPtr` and `NativeMemory.*` are C# in
`corelib/Interop.cs`.

### `[LibraryImport]` and other source generators

csharpc runs the SDK's Roslyn source generators (`Frontend/SourceGenerators.cs`) when their
trigger attribute appears in user code: `LibraryImportGenerator` (`[LibraryImport]`),
the Regex generator (`[GeneratedRegex]`) and the System.Text.Json generator
(`[JsonSerializable]`). The generated marshalling code is ordinary C#; the marshallers it uses
(`Utf8StringMarshaller`, `Utf16StringMarshaller`, `AnsiStringMarshaller`, `SpanMarshaller`,
`ReadOnlySpanMarshaller`, `ArrayMarshaller`) are corelib shadows.

* `CSHARPC_DUMP_GENERATED=dir` writes the generated files to `dir`.
* `CSHARPC_NO_GENERATORS=1` disables generators.
* `CSHARPC_ANALYZERS=dir` overrides the analyzer directory
  (default: `packs/Microsoft.NETCore.App.Ref/<version>/analyzers/dotnet/cs`).

## System.IO

All of it is C# over libc (`corelib/IO/Posix.cs` holds the imports, errno constants and the
x86-64 glibc `struct stat` / `struct dirent` offsets):

| Area | Types |
|---|---|
| Files | `File` (read/write all text/bytes/lines, `ReadLines`, append, copy, move, delete, `*Async`), `FileInfo`, `FileSystemInfo` |
| Directories | `Directory` (create recursive, delete recursive, move, `Get*/Enumerate*` with `*`/`?` patterns and `SearchOption`), `DirectoryInfo` |
| Paths | `Path` with .NET's Unix semantics (`Combine`, `Join`, `GetFullPath`, `GetRelativePath`, extensions, temp files) |
| Streams | `Stream`, `FileStream` (4 KiB buffer, all `FileMode`s), `MemoryStream`, `BinaryReader`, `BinaryWriter` |
| Text | `TextReader`, `TextWriter`, `StreamReader` (BOM detection, incremental decoding across buffer boundaries), `StreamWriter` (UTF-8 without BOM by default, preamble when the encoding has one), `StringReader`, `StringWriter` |
| Encodings | `Encoding.UTF8/Default/Unicode/BigEndianUnicode/ASCII/Latin1`, `UTF8Encoding`, `UnicodeEncoding`, `ASCIIEncoding`, `GetEncoding(name/codepage)` |
| Environment | `Get/SetEnvironmentVariable`, `CurrentDirectory`, `MachineName`, `UserName`, `ProcessId`, `ExitCode` |
| Console | `Console.Out`, `Console.Error`, `Console.In` are real `TextWriter`/`TextReader` objects |

Errors map errno to the exceptions and messages .NET uses on Unix:
`ENOENT` → `FileNotFoundException` ("Could not find file '…'.") or `DirectoryNotFoundException`
("Could not find a part of the path '…'.") depending on whether the parent exists;
`EACCES`/`EPERM`/`EISDIR` → `UnauthorizedAccessException`; `EEXIST` → `IOException`
("The file '…' already exists."); anything else → `IOException` with `strerror` text.

### Console fast paths

`Console.Out.WriteLine(x)` / `Console.Error.Write(x)` / `Console.In.ReadLine()` written
literally keep compiling straight to the console runtime (`Intrinsics.TryGetConsoleFastPath`),
so they cost the same as `Console.WriteLine(x)`. A `TextWriter` stored in a variable (or passed
to a method expecting one) is the corelib object and dispatches virtually.

## Tests and benchmark

* `tests/samples/160_pinvoke_dllimport.cs` — libc/libm, struct return, `qsort` with an
  `[UnmanagedCallersOnly]` callback, errno, `Marshal`, `NativeLibrary`, missing library/entry.
* `tests/samples/162_pinvoke_libraryimport.cs` — generated stubs: UTF-8 strings, `bool`
  returns, `SetLastError`, struct returns, `Span<byte>` buffers.
* `tests/samples/161_file_io.cs`, `163_path_semantics.cs`, `164_encoding_streams.cs`,
  `165_environment.cs` — System.IO surface, recorded from real .NET.
* `tests/programs/csvreport` — writes a 5 000-row CSV, parses it back (quoted fields, CRLF/LF),
  aggregates with LINQ, writes a report and greps a directory tree.
* `benchmarks/programs/fileio.cs` — 66 MB CSV written with `StreamWriter`, read back with
  `StreamReader.ReadLine` and parsed, plus a 24 MB `BinaryWriter`/`BinaryReader` pass.

## Limits

* x86-64 Linux (glibc) only: struct layouts and the calling convention are SysV x86-64.
* No delegate marshalling, `SafeHandle` parameters, `[In, Out]` array copy-back of non-blittable
  elements, `StringBuilder` parameters, or variadic functions (same as .NET for the last one).
* `FileStream` is synchronous; the `*Async` file APIs complete synchronously.
* File times other than last-write time, permissions/attributes, and file locking (`FileShare`)
  are not implemented.
