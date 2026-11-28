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
        public const int ENOENT = 44, EPERM = 63, EACCES = 2, EEXIST = 20, ENOTDIR = 54, EBADF = 8, ENAMETOOLONG = 37, EAGAIN = 6, ECANCELED = 11, EFBIG = 22, ERANGE = 68;
#elif CSHARPC_TARGET_MACOS
        public const int ENOENT = 2, EPERM = 1, EACCES = 13, EEXIST = 17, ENOTDIR = 20, EBADF = 9, ENAMETOOLONG = 63, EAGAIN = 35, ECANCELED = 89, EFBIG = 27, ERANGE = 34;
#else
        public const int ENOENT = 2, EPERM = 1, EACCES = 13, EEXIST = 17, ENOTDIR = 20, EBADF = 9, ENAMETOOLONG = 36, EAGAIN = 11, ECANCELED = 125, EFBIG = 27, ERANGE = 34;
#endif

        [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, byte* buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, byte* buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] public static extern byte* getcwd(byte* buffer, nuint size);
        [DllImport("libc", SetLastError = true)] public static extern int chdir(byte* path);
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
        public static byte* MapView(nint handle, long offset, long length) => null;
        public static byte* MapView(nint handle, long offset, long length, bool writable) => null;
        public static void UnmapView(byte* view, long length) { }
#else
        public const bool CanMapFiles = true;
        [DllImport("libc", SetLastError = true)] private static extern void* mmap(void* address, nuint length, int prot, int flags, int fd, long offset);
        [DllImport("libc", SetLastError = true)] private static extern int munmap(void* address, nuint length);

        /// <summary>A read-only shared view of [offset, offset + length) of a file (offset aligned to 64 KiB by the caller); null with errno set on failure.</summary>
        /// <summary>A read-only view of [offset, offset + length) of a file; <paramref name="handle"/> is its descriptor (SafeFileHandle's value).</summary>
        public static byte* MapView(nint handle, long offset, long length) => MapView(handle, offset, length, writable: false);

        /// <summary>A shared view, writable (PROT_READ | PROT_WRITE) or read-only; null with errno set on failure.</summary>
        public static byte* MapView(nint handle, long offset, long length, bool writable)
        {
            void* p = mmap(null, (nuint)length, writable ? 3 : 1 /* PROT_READ (| PROT_WRITE) */, 1 /* MAP_SHARED */, (int)handle, offset);
            return p == (void*)-1 ? null : (byte*)p;
        }

        public static void UnmapView(byte* view, long length) => munmap(view, (nuint)length);
#endif

        public static int Errno => Marshal.GetLastPInvokeError();

        public static string ErrorMessage(int errno) => Utf8.FromNative(strerror(errno));

        /// <summary>
        /// The exception .NET throws for an errno on a path: Interop.GetExceptionForIoErrno (Common/src/Interop/Unix/Interop.IOErrors.cs,
        /// release/11.0), with the path as the caller passes it and CoreLib's resource texts.
        /// </summary>
        public static Exception IOError(int errno, string path, bool isDirError)
        {
            bool hasPath = !string.IsNullOrEmpty(path);
            switch (errno)
            {
                case ENOENT:
                    if (!isDirError && (path == null || Directory.Exists(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)))))
                        return hasPath ? new FileNotFoundException("Could not find file '" + path + "'.", path) : new FileNotFoundException("Unable to find the specified file.");
                    goto case ENOTDIR;
                case ENOTDIR:
                    return hasPath ? new DirectoryNotFoundException("Could not find a part of the path '" + path + "'.", path)
                        : new DirectoryNotFoundException("Could not find a part of the path.");
                case EACCES:
                case EBADF:
                case EPERM:
                    return new UnauthorizedAccessException(hasPath ? "Access to the path '" + path + "' is denied." : "Access to the path is denied.",
                        new IOException(ErrorMessage(errno), errno));
                case ENAMETOOLONG:
                    return new PathTooLongException(hasPath ? "The path '" + path + "' is too long, or a component of the specified path is too long."
                        : "The specified file name or path is too long, or a component of the specified path is too long.");
                case EAGAIN:
                    return new IOException(hasPath ? "The process cannot access the file '" + path + "' because it is being used by another process."
                        : "The process cannot access the file because it is being used by another process.", errno);
                case ECANCELED:
                    return new OperationCanceledException();
                case EFBIG:
                    return new ArgumentOutOfRangeException("value", "Specified file length was too large for the file system.");
                case EEXIST:
                    if (hasPath) return new IOException("The file '" + path + "' already exists.", errno);
                    goto default;
                default:
                    return new IOException(hasPath ? ErrorMessage(errno) + " : '" + path + "'" : ErrorMessage(errno), errno);
            }
        }
    }
}
#endif
