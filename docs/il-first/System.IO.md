# System.IO (CoreLib part)

A library or CoreLib type served from the framework's IL: its row of the per-library table of
[ADR 0023](../adr/0023-framework-il-first.md) (columns explained there, under "Measurements").

| Library (switch) | Correctness (samples from IL) | Binary size, IL / shadow | Compile cpu, IL / shadow | Speed, IL / shadow (time) | Lines removed on a flip | Decision | NativeAOT on the same host, csharpc / NativeAOT (`tools/il-first/measure-aot.sh`: stripped size; compile cpu; run, median of 7; instructions) |
|---|---|---|---|---|---|---|---|
| Streams and text readers/writers: `Stream`, `MemoryStream`, `UnmanagedMemoryStream`, `BufferedStream`, `TextReader`, `TextWriter`, `StreamReader`, `StreamWriter`, `StringReader`, `StringWriter`, `BinaryReader`, `BinaryWriter`, `IndentedTextWriter` (`src/Csharpc/upstream-types/System.IO.txt`, CoreLib types: the slicer route; #315, part 1) | the 74 samples that use streams, readers, writers, the console's writers, files, HttpClient, SslStream, compression and crypto streams pass at -O0 and -O2 (148 runs) and via IL (74); on wasm32 the 35 that run there (1251's wasm compile is killed by the host's memory limit, see below) | `tests/size`: unchanged except `jsonwriter` 818 280 to 820 248 (+0.2 %); the streams program 271 432 to 310 792 (1.15x: the framework's async plumbing and its `StreamReader`/`StreamWriter` encoders) | noise-level on the size programs (`json` 32.6 to 35.8 s, `regex` 27.8 to 16.7 s, shared host); the streams program 8.7 to 8.9 s | `tools/il-first/bench/io_streams_throughput.cs` (UTF-8 lines through `StreamWriter`/`StreamReader`, `BinaryWriter`/`BinaryReader`, `BufferedStream`, `StringWriter`/`StringReader`), median of 7: shadow 852 ms, IL **771 ms (0.90x)**; the checksum equals real .NET | 3 644 (`IO/Streams.cs`, `IO/Text.cs`, `IO/BufferedStream.cs`, `IO/IndentedTextWriter.cs`); 513 of them stay as `IO/FileStream.cs` (until part 2) and `IO/ConsoleText.cs`; 228 added for the contract (`IO/StreamContract.cs`, `IO/UpstreamIoMembers.cs`) | **Flipped (part 1; part 2, the file system over a libSystem.Native contract, follows).** Contract: Stream's asynchronous defaults (upstream runs them as `Stream.ReadWriteTask` over Task internals and reads BeginRead/BeginWrite overrides from the method table) are csharpc's, through the slicer's replacements, with .NET's argument checks and messages; `Task.FromCanceled(OperationCanceledException)`, `ValueTask.DangerousCreateFromTypedValueTask`, `Decimal.GetBytes`/`ToDecimal`, `Encoding.IsUTF8CodePage` and `RuntimeHelpers.IsRuntimeAsync` (its IL body's `false`) are corelib members. General fixes on the way: the slicer forwards `this` to an instance member's replacement; `[AsyncMethodBuilder]` on an already compiled async method is not a dropped semantic attribute; `CoreLib.MapForReceiver` no longer binds `base(...)` or `base.M()` of a corelib class deriving from a framework-served class to the class's own member (it recursed: 199, 548, 538, 1101 to 1103). Security: the hand-written readers and writers of untrusted bytes and text (UTF-8 decoding across buffer boundaries, `BinaryReader.ReadString`'s 7-bit length prefix, `StreamReader`'s BOM detection) are replaced by the framework's own, which bound every length; no native code is added. | `io_streams_throughput`: 310 552 / 1 427 608 B; compile cpu 28.8 / 12.8 s; run 829 / 811 ms (1.02x) |

1251's wasm32 compile is killed by this host's memory limit with the shadow build too (peak 7.2 GB before the kill, exit 137): a
limit of the shared build host, not of the flip; CI's wasm32 runners compile it.

## Part 2: the file system over a libSystem.Native contract (security review)

`File`, `Directory`, `FileInfo`, `DirectoryInfo`, `FileSystemInfo`, `FileStream`, `FileStreamOptions`, `Path`, `RandomAccess`,
`SafeFileHandle` and the `System.IO.Enumeration` types are the framework's IL. Their 57 `Interop.Sys` P/Invokes into
`libSystem.Native` bind to `corelib/IO/SystemNative.cs` (option (b) of [21-ownership.md](../21-ownership.md): a C# contract over
csharpc's own libc layer, not the pack's shim), written function by function after `pal_io.c`, `pal_mount.c`, `pal_time.c`,
`pal_errno.c` and `pal_error_common.h` (dotnet/runtime release/11.0). The framework's marshalling, `SafeHandle` reference counting,
argument checks and errno-to-exception mapping (`Interop.GetExceptionForIoErrno`) run unchanged; the errno tables between the platform
and the PAL numbering are `SystemNativeErrors.cs`, generated from the three targets' errno lists. What was checked:

- **Descriptors are close-on-exec.** Every `open` the framework issues carries `PAL_O_CLOEXEC`, mapped to the target's `O_CLOEXEC`
  (WASI has no `exec`); `mkstemps` (`Path.GetTempFileName`) uses `mkostemps(O_CLOEXEC)` where `pal_io.c` uses plain `mkstemps`
  (stronger, and not observable: the framework closes that descriptor at once); `pipe` uses `pipe2(O_CLOEXEC)` on Linux and
  `FIOCLEX` on both ends on macOS, closing both if that fails; `/proc/mounts` is read through glibc's `setmntent`, which opens it
  close-on-exec. Every `open` also carries `O_NOCTTY` (as `pal_io.c`), so opening a terminal device by path never makes it the
  process's controlling terminal.
- **Permissions on create.** `open(O_CREAT)` and `mkdir` get the framework's mode (0666 for files, 0777 for directories, or
  `UnixCreateMode`), filtered by the process umask in the kernel as with .NET; `mkdtemp` creates 0700 and `mkstemps` 0600.
  `File.Copy` copies the source's `rwx` bits only (`st_mode & 0777`, as `pal_io.c`): setuid, setgid and sticky are never copied
  (1391 checks a setuid source); a failure to copy times or bits is ignored only for `EPERM` (file systems without POSIX ownership).
  macOS copies with `fcopyfile(COPYFILE_ALL)`, as there.
- **Symbolic links** are followed exactly where `libSystem.Native` follows them: `stat`, `open` (unless the framework passes
  `O_NOFOLLOW`), `chmod`, `opendir` follow; `lstat`, `utimensat` (`AT_SYMLINK_NOFOLLOW`), `readlink`, `unlink`, `rename`, `rmdir` and
  `lchflags` do not. No call resolves a path itself.
- **Paths and NULs.** Paths arrive as the framework's NUL-terminated UTF-8 (`Path.GetFullPath` rejects an embedded NUL first:
  "Null character in path.", in 1391) and are passed to libc as they are; nothing copies a path into a fixed buffer, so the length
  limit is the kernel's (`ENAMETOOLONG` becomes `PathTooLongException`, in 1391).
- **EINTR** is retried around every call `pal_io.c` retries (open, read, write, pread/pwrite, readv/writev, preadv/pwritev, lseek,
  ftruncate, fsync, fallocate, flock, fcntl locks, chmod, futimens/utimensat, mkdir, rmdir, unlink, rename, link, symlink, opendir,
  stat/fstat, mkdtemp/mkstemps, copy loops, poll); `close` and `closedir` are never retried (the descriptor is gone even when they
  report EINTR, and a retry could close one another thread just received), and their EINTR is success, as there.
- **Buffer bounds in the 57 entry points.** Every write goes to a buffer whose size the caller passed or to a stack buffer of the
  native structure's size: `Read`/`Write`/`PRead`/`PWrite` reject a negative count; `ReadLink` passes the caller's size to `readlink`
  (which does not terminate) and returns the count; `GetCwd` and `StrErrorR` (`strerror_r`, not the thread-unsafe `strerror`) pass the
  caller's size; `ReadDir` fills exactly `DirectoryEntry` (a pointer and two ints) and points `Name` into the `DIR`'s own buffer, as
  there; `Stat`/`LStat`/`FStat` fill exactly the 120-byte `FileStatus` from a full-size native `stat`/`statx`; vectored I/O passes at
  most `IOV_MAX` vectors (and on macOS no more than `INT_MAX` bytes, as `GetAllowedVectorCount`); mount points are read with
  `getmntent_r` into an 8 KiB buffer (`pal_mount.c`'s size) and with `getfsstat` into a buffer sized from its own count.
- **Non-blocking descriptors** (an anonymous pipe's async ends): on `EAGAIN` the read or write waits in `poll` and retries; a hang-up
  is the end of the stream for a reader and `EPIPE` for a writer, as there.
- **The serialization guard** (`SerializationInfo.ThrowIfDeserializationInProgress`) is upstream's: while framework IL that deserializes
  has called `SerializationInfo.StartDeserialization` (System.Data's `DataTable.ReadXml`/`DataSet.ReadXml` do, through `TypeLimiter`),
  opening a file for writing throws `SerializationException` unless `Switch.System.Runtime.Serialization.SerializationGuard.AllowFileWrites`
  is set, and so does `Process.Start` (`AllowProcessCreation`; the corelib's `Process` now asks the same guard). The previous no-op
  was not reachable yet (System.Data's IL does not compile with csharpc: explicit diagnostics for `Array.Copy` on `Array` and
  XmlSerializer delegates), but it would have become a silent difference the day it does; real .NET throws both exceptions from a
  `DataTable.RowChanged` handler during `ReadXml` (checked by hand; no sample, since csharpc rejects System.Data).

What `pal_io.c` does that the contract does differently, by design and not observable: `mkostemps(O_CLOEXEC)` above; Linux `stat`
goes through `statx` (one layout on every architecture), with no birth time reported, as `pal_io.c`'s `struct stat` path.
