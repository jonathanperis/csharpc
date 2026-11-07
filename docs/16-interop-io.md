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
| blittable structs | per target, as clang does: SysV x86-64 (≤16 bytes split into INTEGER/SSE eightbytes, larger structs `byval` / `sret`); AAPCS64 on Linux and macOS (homogeneous float aggregates in SIMD registers, other composites ≤16 bytes in GPRs, larger ones by reference, `sret` in x8). Apple's arm64 variant packs stack arguments at natural alignment and has the caller extend `sbyte`/`short`/`bool` arguments. Microsoft x64 (Windows): structs of exactly 1, 2, 4 or 8 bytes travel as one integer, any other size by reference to a caller-made copy (no `byval`), returned through a hidden pointer |

Return values: primitives, `bool` (non-zero), structs (register pairs or `sret`), and `string`
(converted from UTF-8/UTF-16 and freed with `free`, as .NET does).

`SetLastError = true` clears `errno` before the call and saves it afterwards
(`Marshal.GetLastPInvokeError`, `Marshal.GetLastSystemError`, `Marshal.GetLastWin32Error`); on
Windows it is the Win32 last error (`SetLastError(0)` / `GetLastError()`), as in .NET.

Delegate parameters of a `[DllImport]` are not marshalled; pass callbacks as `delegate* unmanaged<...>` pointing to
an `[UnmanagedCallersOnly]` method (`&Compare`) — that is the supported, allocation-free path in
.NET too.

`Marshal.GetFunctionPointerForDelegate<TDelegate>` (what `[LibraryImport]` stubs call for a delegate parameter) and
`Marshal.GetDelegateForFunctionPointer<TDelegate>` work for delegate types known at compile time whose signature is
blittable (integers, floats, pointers, enums, by-ref blittables; `bool` as a 4-byte BOOL). csharpc generates no code at run
time, so each such delegate type gets a pool of 32 reverse P/Invoke thunks (`ModuleCodeGen.ReverseThunks.cs`,
`corelib/DelegateThunks.cs`): thunk *i* calls the delegate in slot *i*, which holds it weakly, as .NET's thunks do (keep
the delegate reachable while native code can call it; a collected delegate frees its slot, and a full pool is collected
once before it gives up with an `InvalidOperationException`). Other signatures (strings, structs by value, `char`) are a
compile-time diagnostic; the non-generic overloads (a delegate known only as `Delegate`, a `Type` argument) and
generic delegate types are a `NotSupportedException` / `ArgumentException`. A pointer from
`GetFunctionPointerForDelegate` wrapped again with `GetDelegateForFunctionPointer` is a new delegate that calls it
(.NET returns the original delegate).

### Binding

* **Direct link** — `libc`, `c`, `libm`, `m`, `libdl`, `libpthread` (with or without `.so`/version
  suffix), `__Internal` and `csrt` are resolved by the linker. The symbol is declared weak, so a
  missing entry point compiles and throws `EntryPointNotFoundException` at the call, as in .NET.
* **Everything else** is loaded lazily with `dlopen` on first call and cached per call site.
  Probing follows .NET: the executable's directory first (where csharpc copies the native libraries of
  the program's NuGet packages, `runtimes/<rid>/native`), then the system's search, each with the name as
  given, `name.so`, `libname.so`, `name`, `libname`. Failures throw `DllNotFoundException` / `EntryPointNotFoundException` with .NET's messages.
* **Windows**: there is no `libc` to load (`DllImport("libc")` throws `DllNotFoundException` under
  .NET on Windows too), so user code binds everything except `__Internal`/`csrt` through
  `LoadLibraryExW`/`GetProcAddress`, probing `name.dll` then `name`, with .NET's Windows messages
  ("Unable to load DLL 'x' or one of its dependencies: The specified module could not be found.
  (0x8007007E)", "... entry point named 'f' in DLL 'x'."). The corelib's own `libc`/`kernel32`
  imports link directly against the UCRT and kernel32 (as strong symbols: a COFF weak external of,
  say, `malloc` would also capture the C runtime's own references to it).

* **.NET's own shims** (`libSystem.IO.Compression.Native`, and `libSystem.Native` for tar and zip permissions)
  are linked statically from the NativeAOT runtime pack, as NativeAOT does, so framework assemblies compiled from
  IL (`System.IO.Compression`, `System.Net.Http`'s decompression) call the same zlib-ng, brotli and zstd. On
  **Windows** the framework's IL names the shim `System.IO.Compression.Native`, and the pack's archives are
  MSVC libraries (`System.IO.Compression.Native.Aot.lib`, `zlibstatic.lib`, `brotli*.lib`, `zstd_static.lib`)
  that lld links into the MinGW executable. Three things make that work: `src/runtime/windows-shims/msvc_compat.c`
  supplies the few vcruntime symbols they name (`__security_cookie` and its check, `__GSHandlerCheck`,
  `__guard_dispatch_icall_fptr`, `__chkstk`, `__isa_available`, `_fltused`; weak, with the MSVC fail-fast `int 0x29`
  on a smashed stack cookie), `-nodefaultlib` drops their `LIBCMT`/`OLDNAMES`/`uuid` `/DEFAULTLIB` directives, and
  the object is compiled and linked only into programs that link a shim, so others are unchanged. Gzip, deflate,
  zlib and Brotli streams and `HttpClient`'s `AutomaticDecompression` therefore work on every platform
  (samples 1005, 1211). `ZipArchive` and `ZipFile` do not on Windows yet: they reach `Aes.Create()` (CNG) and
  `File.SetLastWriteTimeUtc`, both still missing there (samples 537, 540).

`NativeLibrary.Load/TryLoad/GetExport/TryGetExport/Free`, `Marshal.AllocHGlobal/FreeHGlobal`,
`Marshal.StringToHGlobalAnsi/Uni`, `PtrToStringUTF8/Ansi/Uni`, `Read*/Write*`, `Copy` (every primitive array type, range-checked
like the span constructors) and `NativeMemory.*` (including the aligned allocators, over a header kept below the aligned
address) are C# in `corelib/Interop.cs`.

### Marshal.SizeOf / OffsetOf / PtrToStructure / StructureToPtr / DestroyStructure

These are resolved at the call site (`FunctionCodeGen.MarshalLayout.cs`), where the type is a type argument, `typeof(X)` or
the static type of the structure: the unmanaged layout is computed at compile time as the CLR's marshaller lays it out
(sequential or explicit, `Pack`, `bool` as a 4-byte `BOOL` or one byte with `[MarshalAs(U1/I1)]`, a char as one ANSI
byte unless the layout says `CharSet.Unicode`, nested structs, enums) and the copies are generated field by field, or as
one block copy when the unmanaged bytes are the managed ones. A layout with a converted field (`bool`) is written into
zeroed bytes and cleared by `DestroyStructure`, as the CLR does. The marshaller's own refusals (an enum, string, array
or auto-layout class given to `SizeOf`, generic types, null arguments) are the same `ArgumentException`s at run time.
Explicit diagnostics: a type known only at run time (`Type` variables, a structure typed `object` or an unsealed class), fields
that need real marshalling (strings, arrays, delegates, object references, fixed buffers) and ANSI `char` fields when
reading or writing memory.

### `[LibraryImport]` and other source generators

csharpc runs Roslyn source generators (`Frontend/SourceGenerators.cs`) and compiles their output
with the program, as csc does. `[GeneratedRegex]` is handled by csharpc's own generator
(`Frontend/GeneratedRegex.cs`, [12-corelib.md](12-corelib.md#regular-expressions)) in place of the
SDK's, which emits code built on `RegexRunner` internals. The generated marshalling code is ordinary C#; the marshallers it uses
(`Utf8StringMarshaller`, `Utf16StringMarshaller`, `AnsiStringMarshaller`, `SpanMarshaller`,
`ReadOnlySpanMarshaller`, `ArrayMarshaller`) are corelib shadows.

Which generators, with which inputs:

* **Projects** (`csharpc App.csproj`): what csc gets in `dotnet build`. The project evaluation
  (`Driver/ProjectLoader.cs`) restores and runs the SDK's `Compile` target with
  `SkipCompilerExecution=true ProvideCommandLineArgs=true` (what design-time builds use), and reads the
  csc command line: every `/analyzer` (the targeting packs', the Web/Razor SDKs', NuGet packages'
  `analyzers` folders), the `/additionalfile`s (`.razor`/`.cshtml` for Razor), the `/analyzerconfig`
  files (the `GeneratedMSBuildEditorConfig` with `build_property.*` from `CompilerVisibleProperty` and
  `build_metadata.*` from `CompilerVisibleItemMetadata`, `.editorconfig`, `.globalconfig`) and the
  `/features` (`InterceptorsNamespaces` for the request-delegate and configuration-binder interceptors).
  Generated files get csc's paths (under the `/out` directory). Analyzers that aren't generators are not
  run: their findings never change the program. With ProjectReferences, each project's generators run on
  that project's own compilation (`RoslynFrontend.BuildProjects`, the compilations `dotnet build` would make:
  its references, parse options, analyzers, `RootNamespace` and other global options, additional files), and the
  generated trees are folded into the merged program, attributed to the project for `Type.Assembly`.
  Post-initialization sources every project's generator emits (identical text) are kept once. The one run
  over the merged program left is the JSON generator for the contexts csharpc synthesizes for reflection-style
  `System.Text.Json` calls.
* **Single files**: the targeting pack's LibraryImport and System.Text.Json generators, when their
  attribute appears in the code, and csharpc's `[GeneratedRegex]`.

A generator that throws (csc's warnings CS8784/CS8785) or can't be loaded is an error under csharpc:
csc would compile without its output, so the program would silently differ.

* `CSHARPC_DUMP_GENERATED=dir` writes the generated files to `dir`.
* `CSHARPC_NO_GENERATORS=1` disables generators.

The SDK's generators for `[JsonSerializable]` and `[LibraryImport]` are linked into csharpc, taken from the
`Microsoft.NETCore.App.Ref` pack of the SDK that builds it (`analyzers/dotnet/cs`), so a csharpc compiled by csharpc
runs them too; a project's `/analyzer` file of the same assembly version uses the linked copy, any other
generator assembly is loaded from its file (the native compiler cannot load assemblies: an explicit diagnostic).

## System.IO

All of it is C# over libc (`corelib/IO/Posix.cs` holds the imports, errno constants and the
`struct stat` / `struct dirent` offsets); on Windows over kernel32 and the UCRT (below). Those differ per OS (`O_CREAT` is 0x40 on Linux and 0x200 on
macOS, `ENOTEMPTY` 39 and 66, `d_name` sits at offset 19 and 21), so the compiler parses the corelib
with `CSHARPC_TARGET_LINUX` or `CSHARPC_TARGET_MACOS`, chosen from the **target** triple (never the
host's), and `Posix.cs` / `ConsoleKeys.cs` (termios) select the values with `#if`. Linux stats through
`statx`, whose layout is the same on every architecture; macOS arm64 calls `stat`/`lstat`/`fstat`
directly (64-bit `ino_t` layout, no `$INODE64` symbol variants on arm64).

On Windows (`CSHARPC_TARGET_WINDOWS`) `corelib/IO/Win32.cs` implements the same `Posix` surface over
kernel32 and the UCRT, so `File`/`Directory`/`FileStream` share their logic across OSes: files are
opened with `CreateFileW` (.NET's `FileMode`/`FileAccess` mapping, `FileShare` as the share mode, so
sharing violations happen as under .NET) and read and written through UCRT descriptors
(`_open_osfhandle`); directories use `CreateDirectoryW`/`RemoveDirectoryW`/`FindFirstFileW`, stat uses
`GetFileAttributesExW` (100 ns write times), `MoveFileExW` renames. Win32 errors map to .NET's Windows
exceptions and messages (`ERROR_PATH_NOT_FOUND` → `DirectoryNotFoundException`, sharing violations,
"The directory is not empty."). `PathWindows.cs` gives `Path` its Windows rules (`\` and `/`
separators, drive, root-relative and UNC roots, case-insensitive `GetRelativePath`, `GetFullPathNameW`,
`GetTempPathW`), `Environment` uses `GetEnvironmentVariableW`, `GetComputerNameW`, `GetUserNameW` and
`GetCommandLineW`, and `ConsoleKeysWindows.cs` reads keys with `ReadConsoleInputW` (a standard handle
counts as redirected unless it is a console, as in .NET; `Console.Clear` on redirected output throws
`IOException` like .NET). The local time zone is the Windows one (`GetDynamicTimeZoneInformation` +
`SystemTimeToTzSpecificLocalTimeEx`); `TZ` is ignored, as .NET does on Windows.

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

## System.Net (sockets)

`src/Csharpc/corelib/Net/` implements the basic networking types in C# over the OS's BSD socket API
(libc on Linux and macOS, Winsock/`ws2_32` on Windows): `IPAddress`/`IPEndPoint` (parsing and
formatting follow .NET: inet_aton-style IPv4 input such as `127.1` or `0x7f.1`, RFC 5952 IPv6
output with embedded IPv4 for mapped addresses), `Socket` (TCP and UDP, blocking; `new
Socket(SocketType, ProtocolType)` and `new TcpClient()` are dual-mode IPv6 sockets, so IPv4 peers
appear as `::ffff:a.b.c.d`, as in .NET), `NetworkStream`, `TcpListener`, `TcpClient`, `UdpClient`
and `Dns.GetHostAddresses`/`GetHostName` (getaddrinfo). Constants, `sockaddr` layouts (macOS has a
length byte first) and errno values are per target OS. Errors are `SocketException`s with .NET's
`SocketErrorCode` (Winsock numbering, mapped from errno), the native error as `ErrorCode`/
`NativeErrorCode`, the OS's message text, and — for failed connects — the endpoint appended, as .NET
does; `SocketException` derives from `Win32Exception` and `ExternalException` like in .NET. The
`*Async` methods run the blocking call on the thread pool. TLS (`SslStream`, X.509) works on Linux and macOS (over OpenSSL 3), [ADR 0022](adr/0022-tls.md). Not yet: `HttpClient`
on macOS and Windows,
`Socket.Select`/`Poll`, socket flags, most socket options (other members are compile-time
"not supported" diagnostics). Samples `195_ip_addresses` and `196_sockets_loopback`.
Disposing a `NetworkStream` that owns its socket, or a `TcpClient`, shuts the connection down (both directions, errors ignored)
before closing the descriptor, as .NET does: a bare `close()` with unread received data sends an RST, and the peer's kernel
then drops what it had received but not read, such as a TLS `close_notify` (a client that never read the server's TLS 1.3
session tickets; sample `1207_socket_close_unread_data`). `Socket.Dispose` and `Close(0)` stay bare closes.

Connecting by name follows .NET's rules (samples `1180`-`1182`): `Connect`/`ConnectAsync(host, port)`,
`(DnsEndPoint)`, `(IPAddress[], port)`, `SocketAsyncEventArgs` with a `DnsEndPoint` (instance and static
`Socket.ConnectAsync`, `ConnectSocket`, `ConnectByNameError`) and `TcpClient`'s overloads resolve the name and
try its addresses in turn, `::1` before `127.0.0.1` for `localhost`: a socket of one address family only tries
that family (a dual-mode one reaches IPv4 too), the failure of the last attempt is the exception, a canceled
token ends the connect, and no usable address is `ArgumentException`/`NoData` as in .NET. On Unix a socket
whose connect failed is replaced by a new descriptor with the same options before the next attempt
(`ReplaceHandleIfNecessaryAfterFailedConnect`, .NET's `SafeSocketHandle.LastConnectFailed`): Linux leaves an
IPv4-mapped connect's protocol operations on the socket after the peer refused it, so an attempt with a real
IPv6 address would never complete. `TcpListener.AcceptTcpClientAsync`/`AcceptSocketAsync` are the socket
engine's asynchronous accepts, so `Stop` ends them and closes the listening socket.

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

* Linux (glibc; x86-64 and AArch64), macOS on Apple silicon and Windows x64. Library probing follows
  .NET's rules per OS (`.so` / `.dylib` / `.dll` suffixes, `lib` prefix), and `DllNotFoundException`
  uses .NET's per-OS message.
* Windows: `CharSet.Ansi` strings are marshalled as UTF-8 (.NET uses the ANSI code page; the same for
  ASCII text), and returned native strings are released with `free` (.NET: `CoTaskMemFree`).
* No delegate parameters on `[DllImport]` (see above for the `[LibraryImport]` path), `SafeHandle` parameters, `[In, Out]` array copy-back of non-blittable
  elements, `StringBuilder` parameters, or variadic functions (same as .NET for the last one).
* Windows `System.Uri`: a rooted path without a drive (`"/absolute/path"`) is no implicit file path there, as in
  .NET (only `C:\dir` and UNC paths are): `UriKind.Absolute` throws `UriFormatException` ("The format of the URI could
  not be determined."), the relative kinds give a relative `Uri`, which `HttpClient` resolves against `BaseAddress`
  (samples 972, 1010, 1210; the last one is recorded live from .NET on Windows). UNC paths and `file:` URIs without a
  drive letter (their `LocalPath` and `IsUnc` rules) are an explicit `NotSupportedException` on Windows.
* `FileStream` is synchronous; the `*Async` file APIs complete synchronously.
* File times other than last-write time, permissions/attributes, and file locking (`FileShare`)
  are not implemented.
