// POSIX layer for System.IO: libc calls (P/Invoke, statically linked) + errno -> .NET exceptions.
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
        // Flag values, errno values and struct layouts are per OS. The compiler parses the corelib with
        // CSHARPC_TARGET_LINUX, CSHARPC_TARGET_MACOS or CSHARPC_TARGET_WASI (from the target triple),
        // never the host's.
#if CSHARPC_TARGET_WASI
        // wasi-libc: access modes and open flags map onto WASI rights and oflags; errno is WASI's numbering.
        public const int O_RDONLY = 0x4000000, O_WRONLY = 0x10000000, O_RDWR = 0x14000000;
        public const int O_CREAT = 0x1000, O_EXCL = 0x4000, O_TRUNC = 0x8000, O_APPEND = 0x1, O_CLOEXEC = 0;
        public const int ENOTEMPTY = 55;
        public const int ENOENT = 44, EPERM = 63, EACCES = 2, EEXIST = 20, ENOTDIR = 54, EISDIR = 31, EINTR = 27, ERANGE = 68;
#else
        public const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
#if CSHARPC_TARGET_MACOS
        public const int O_CREAT = 0x200, O_EXCL = 0x800, O_TRUNC = 0x400, O_APPEND = 0x8, O_CLOEXEC = 0x1000000;
        public const int ENOTEMPTY = 66;
#else
        public const int O_CREAT = 0x40, O_EXCL = 0x80, O_TRUNC = 0x200, O_APPEND = 0x400, O_CLOEXEC = 0x80000;
        public const int ENOTEMPTY = 39;
#endif
        public const int ENOENT = 2, EPERM = 1, EACCES = 13, EEXIST = 17, ENOTDIR = 20, EISDIR = 21, EINTR = 4, ERANGE = 34;
#endif
        public const int SEEK_SET = 0, SEEK_CUR = 1, SEEK_END = 2;
        /// <summary>Directory.Delete of a non-empty directory (.NET: strerror + " : 'path'").</summary>
        public static Exception DirectoryNotEmpty(string full) => new IOException("Directory not empty : '" + full + "'");
        public const int S_IFMT = 0xF000, S_IFDIR = 0x4000, S_IFREG = 0x8000, S_IFLNK = 0xA000;

        // open(2) is variadic: Apple's arm64 ABI passes variadic arguments on the stack, so calling it as
        // a fixed three-argument function would lose `mode`. A C shim in the runtime makes the call.
        [DllImport("__Internal", EntryPoint = "csrt_open", SetLastError = true)] public static extern int open(byte* path, int flags, int mode);
        /// <summary>FileShare is advisory on Unix (Windows enforces it; see Win32.cs).</summary>
        public static int open(byte* path, int flags, int mode, int share) => open(path, flags, mode);
        [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, byte* buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, byte* buffer, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] public static extern long lseek(int fd, long offset, int whence);
        [DllImport("libc", SetLastError = true)] public static extern int ftruncate(int fd, long length);
        [DllImport("libc", SetLastError = true)] public static extern int fsync(int fd);
        [DllImport("libc", SetLastError = true)] public static extern int mkdir(byte* path, int mode);
        [DllImport("libc", SetLastError = true)] public static extern int rmdir(byte* path);
        [DllImport("libc", SetLastError = true)] public static extern int unlink(byte* path);
        [DllImport("libc", SetLastError = true)] public static extern int rename(byte* from, byte* to);
#if !CSHARPC_TARGET_WASI
        [DllImport("libc", SetLastError = true)] public static extern int symlink(byte* target, byte* linkPath);
        [DllImport("libc", SetLastError = true)] public static extern int link(byte* target, byte* linkPath);
        [DllImport("libc", SetLastError = true)] public static extern int chmod(byte* path, int mode);
        [DllImport("libc", SetLastError = true)] public static extern int fchmod(int fd, int mode);
        /// <summary>times: two struct timespec { long tv_sec; long tv_nsec; } (access, modification).</summary>
        [DllImport("libc", SetLastError = true)] public static extern int utimensat(int dirfd, byte* path, long* times, int flags);
#if CSHARPC_TARGET_MACOS
        public const int AT_FDCWD = -2;
        public const long UTIME_OMIT = -2;
#else
        public const int AT_FDCWD = -100;
        public const long UTIME_OMIT = (1L << 30) - 2;
#endif
#endif
        [DllImport("libc", SetLastError = true)] public static extern byte* getcwd(byte* buffer, nuint size);
        [DllImport("libc", SetLastError = true)] public static extern int chdir(byte* path);
        [DllImport("libc", SetLastError = true)] public static extern void* opendir(byte* path);
        [DllImport("libc", SetLastError = true)] public static extern byte* readdir(void* dir);
        [DllImport("libc")] public static extern int closedir(void* dir);
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

#if CSHARPC_TARGET_WASI
        // struct stat (wasi-libc, wasm32): st_mode @24 (u32), st_size @48, st_mtim @88; 144 bytes.
        [DllImport("libc", SetLastError = true)] public static extern int stat(byte* path, byte* buffer);
        [DllImport("libc", SetLastError = true)] public static extern int lstat(byte* path, byte* buffer);
        [DllImport("libc", SetLastError = true)] public static extern int fstat(int fd, byte* buffer);
        public const int StatSize = 144;
        public static int Mode(byte* st) => *(int*)(st + 24);
        public static long Size(byte* st) => *(long*)(st + 48);
        public static long MTimeSeconds(byte* st) => *(long*)(st + 88);
        public static long MTimeNanoseconds(byte* st) => *(int*)(st + 96);
        // No birth time: the older of the modification and status-change times (st_ctim @104), as .NET on Linux.
        public static void CreationTime(byte* st, out long seconds, out long nanoseconds) =>
            OlderOf(MTimeSeconds(st), MTimeNanoseconds(st), *(long*)(st + 104), *(int*)(st + 112), out seconds, out nanoseconds);

        // struct dirent (wasi-libc): d_ino @0 (u64), d_type @8, d_name @9. File types are WASI's.
        public const int DirentTypeOffset = 8, DirentNameOffset = 9;
        public const byte DT_UNKNOWN = 0, DT_DIR = 3, DT_REG = 4, DT_LNK = 7;
#elif CSHARPC_TARGET_MACOS
        // struct stat (Darwin arm64, 64-bit ino_t): st_mode @4 (u16), st_mtimespec @48, st_size @96; 144 bytes.
        [DllImport("libc", SetLastError = true)] public static extern int stat(byte* path, byte* buffer);
        [DllImport("libc", SetLastError = true)] public static extern int lstat(byte* path, byte* buffer);
        [DllImport("libc", SetLastError = true)] public static extern int fstat(int fd, byte* buffer);
        public const int StatSize = 144;
        public static int Mode(byte* st) => *(ushort*)(st + 4);
        public static long Size(byte* st) => *(long*)(st + 96);
        public static long MTimeSeconds(byte* st) => *(long*)(st + 48);
        public static long MTimeNanoseconds(byte* st) => *(long*)(st + 56);
        // st_birthtimespec @80: .NET reports the birth time where the OS keeps one.
        public static void CreationTime(byte* st, out long seconds, out long nanoseconds)
        {
            seconds = *(long*)(st + 80);
            nanoseconds = *(long*)(st + 88);
        }

        // struct dirent (Darwin, 64-bit ino_t): d_namlen @18 (u16), d_type @20, d_name @21.
        public const int DirentTypeOffset = 20, DirentNameOffset = 21;
#else
        // stat/lstat/fstat go through statx (glibc 2.28+): struct statx has one layout on every Linux
        // architecture, while struct stat does not (st_mode is at offset 24 on x86-64 but 16 on AArch64).
        // struct statx: stx_mode @28 (u16), stx_size @40 (u64), stx_mtime.tv_sec @112 (i64), tv_nsec @120 (u32); 256 bytes.
        [DllImport("libc", SetLastError = true)] private static extern int statx(int dirfd, byte* path, int flags, uint mask, byte* buffer);
        private const int AT_SYMLINK_NOFOLLOW = 0x100, AT_EMPTY_PATH = 0x1000; // AT_FDCWD: above
        private const uint STATX_BASIC_STATS = 0x7FF;
        public const int StatSize = 256;
        public static int stat(byte* path, byte* buffer) => statx(AT_FDCWD, path, 0, STATX_BASIC_STATS, buffer);
        public static int lstat(byte* path, byte* buffer) => statx(AT_FDCWD, path, AT_SYMLINK_NOFOLLOW, STATX_BASIC_STATS, buffer);
        public static int fstat(int fd, byte* buffer)
        {
            byte empty = 0;
            return statx(fd, &empty, AT_EMPTY_PATH, STATX_BASIC_STATS, buffer);
        }
        public static int Mode(byte* st) => *(ushort*)(st + 28);
        public static long Size(byte* st) => *(long*)(st + 40);
        public static long MTimeSeconds(byte* st) => *(long*)(st + 112);
        public static long MTimeNanoseconds(byte* st) => *(uint*)(st + 120);
        // .NET's System.Native reads struct stat on Linux, which has no birth time, so CreationTime is the
        // older of the modification and status-change times (stx_ctime @96), not statx's stx_btime.
        public static void CreationTime(byte* st, out long seconds, out long nanoseconds) =>
            OlderOf(MTimeSeconds(st), MTimeNanoseconds(st), *(long*)(st + 96), *(uint*)(st + 104), out seconds, out nanoseconds);

        // struct dirent (glibc, LP64 Linux: x86-64 and AArch64 alike): d_type @18, d_name @19.
        public const int DirentTypeOffset = 18, DirentNameOffset = 19;
#endif
#if !CSHARPC_TARGET_WASI
        public const byte DT_UNKNOWN = 0, DT_DIR = 4, DT_REG = 8, DT_LNK = 10;
#endif

        // FileStatus.GetCreationTime without a birth time: the modification time if it is older, else the
        // status-change time.
        private static void OlderOf(long mSeconds, long mNanoseconds, long cSeconds, long cNanoseconds, out long seconds, out long nanoseconds)
        {
            bool modified = mSeconds < cSeconds || (mSeconds == cSeconds && mNanoseconds < cNanoseconds);
            seconds = modified ? mSeconds : cSeconds;
            nanoseconds = modified ? mNanoseconds : cNanoseconds;
        }

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

        /// <summary>stat() a path: 0 = missing, S_IFDIR / S_IFREG / other mode bits otherwise.</summary>
        public static int FileType(string path)
        {
            byte* st = stackalloc byte[StatSize];
            byte* p = Utf8.ToNative(path);
            int r = stat(p, st);
            Libc.free(p);
            return r != 0 ? 0 : Mode(st) & S_IFMT;
        }

        public static bool IsDirectory(string path) => FileType(path) == S_IFDIR;
        public static bool IsFile(string path) { int t = FileType(path); return t != 0 && t != S_IFDIR; }

        public static int Errno => Marshal.GetLastPInvokeError();

        public static string ErrorMessage(int errno) => Utf8.FromNative(strerror(errno));

        /// <summary>The exception .NET throws for an errno on a path (System.IO's Unix error mapping).</summary>
        public static Exception IOError(int errno, string path, bool isDirectory)
        {
            string full = path == null ? null : PathImpl.GetFullPath(path);
            switch (errno)
            {
                case ENOENT:
                    if (!isDirectory)
                    {
                        string parent = PathImpl.GetDirectoryName(full);
                        if (parent == null || IsDirectory(parent))
                            return new FileNotFoundException("Could not find file '" + full + "'.", full);
                    }
                    return new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.");
                case ENOTDIR:
                    return new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.");
                case EACCES:
                case EPERM:
                case EISDIR:
                    return new UnauthorizedAccessException("Access to the path '" + full + "' is denied.");
                case EEXIST:
                    return new IOException("The file '" + full + "' already exists.");
                default:
                    return new IOException(full == null ? ErrorMessage(errno) : ErrorMessage(errno) + " : '" + full + "'");
            }
        }

        public static void ThrowIfError(int result, string path, bool isDirectory)
        {
            if (result < 0) throw IOError(Errno, path, isDirectory);
        }
    }
}
#endif
