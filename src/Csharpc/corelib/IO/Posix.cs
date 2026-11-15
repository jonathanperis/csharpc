// POSIX layer for the corelib code that still talks to libc directly: Process (pipes), Console (raw reads), Environment, DateTime's
// local-time conversion, entropy, socket error texts, FileSystemWatcher's errors and MemoryMappedFiles' views. The file system itself
// (File, Directory, FileStream, Path, ...) is the framework's IL over the libSystem.Native contract (SystemNative.cs), not this class.
// Windows has its own implementation of this class (Win32.cs).
#if !CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class Posix
    {
        // errno values are per OS. The compiler parses the corelib with CSHARPC_TARGET_LINUX, CSHARPC_TARGET_MACOS or
        // CSHARPC_TARGET_WASI (from the target triple), never the host's.
#if CSHARPC_TARGET_WASI
        public const int ENOENT = 44, EPERM = 63, EACCES = 2, EEXIST = 20, ENOTDIR = 54, EISDIR = 31;
#else
        public const int ENOENT = 2, EPERM = 1, EACCES = 13, EEXIST = 17, ENOTDIR = 20, EISDIR = 21;
#endif

        [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, byte* buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, byte* buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
        [DllImport("libc")] public static extern byte* getenv(byte* name);
        [DllImport("libc")] public static extern int setenv(byte* name, byte* value, int overwrite);
        [DllImport("libc")] public static extern int unsetenv(byte* name);
#if !CSHARPC_TARGET_WASI
        [DllImport("libc")] public static extern int getpid();
        [DllImport("libc")] public static extern int gethostname(byte* name, nuint length);
#endif
        [DllImport("libc")] public static extern byte* strerror(int error);
        [DllImport("libc")] public static extern int getentropy(byte* buffer, nuint length);
        [DllImport("libc")] public static extern long time(long* result);
        [DllImport("libc")] public static extern byte* localtime_r(long* time, byte* result);

        // ---- read-only file views (System.IO.MemoryMappedFiles) --------------------------------
#if CSHARPC_TARGET_WASI
        public const bool CanMapFiles = false;
        public static byte* MapView(int fd, long offset, long length) => null;
        public static void UnmapView(byte* view, long length) { }
#else
        public const bool CanMapFiles = true;
        [DllImport("libc", SetLastError = true)] private static extern void* mmap(void* address, nuint length, int prot, int flags, int fd, long offset);
        [DllImport("libc", SetLastError = true)] private static extern int munmap(void* address, nuint length);

        /// <summary>A read-only shared view of [offset, offset + length) of a file (offset aligned to 64 KiB by the caller); null with errno set on failure.</summary>
        public static byte* MapView(int fd, long offset, long length)
        {
            void* p = mmap(null, (nuint)length, 1 /* PROT_READ */, 1 /* MAP_SHARED */, fd, offset);
            return p == (void*)-1 ? null : (byte*)p;
        }

        public static void UnmapView(byte* view, long length) => munmap(view, (nuint)length);
#endif

        public static int Errno => Marshal.GetLastPInvokeError();

        public static string ErrorMessage(int errno) => Utf8.FromNative(strerror(errno));

        /// <summary>The exception .NET throws for an errno on a path (System.IO's Unix error mapping, Interop.GetExceptionForIoErrno).</summary>
        public static Exception IOError(int errno, string path, bool isDirectory)
        {
            string full = path == null ? null : System.IO.Path.GetFullPath(path);
            switch (errno)
            {
                case ENOENT:
                    if (!isDirectory)
                    {
                        string parent = System.IO.Path.GetDirectoryName(full);
                        if (parent == null || Directory.Exists(parent))
                            return new FileNotFoundException("Could not find file '" + full + "'.", full);
                    }
                    return new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.", full);
                case ENOTDIR:
                    return new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.", full);
                case EACCES:
                case EPERM:
                case EISDIR:
                    return new UnauthorizedAccessException("Access to the path '" + full + "' is denied.");
                // .NET's Interop.GetExceptionForIoErrno: an IOException's HResult is the raw errno.
                case EEXIST:
                    return new IOException("The file '" + full + "' already exists.", errno);
                default:
                    return new IOException(full == null ? ErrorMessage(errno) : ErrorMessage(errno) + " : '" + full + "'", errno);
            }
        }
    }
}
#endif
