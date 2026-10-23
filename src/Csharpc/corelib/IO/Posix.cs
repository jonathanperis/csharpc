// POSIX layer for System.IO: libc calls (P/Invoke, statically linked) + errno -> .NET exceptions.
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class Posix
    {
        // Flag values, errno values and struct layouts are per OS. The compiler parses the corelib with
        // CSHARPC_TARGET_LINUX or CSHARPC_TARGET_MACOS (from the target triple), never the host's.
        public const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
#if CSHARPC_TARGET_MACOS
        public const int O_CREAT = 0x200, O_EXCL = 0x800, O_TRUNC = 0x400, O_APPEND = 0x8, O_CLOEXEC = 0x1000000;
        public const int ENOTEMPTY = 66;
#else
        public const int O_CREAT = 0x40, O_EXCL = 0x80, O_TRUNC = 0x200, O_APPEND = 0x400, O_CLOEXEC = 0x80000;
        public const int ENOTEMPTY = 39;
#endif
        public const int SEEK_SET = 0, SEEK_CUR = 1, SEEK_END = 2;
        public const int S_IFMT = 0xF000, S_IFDIR = 0x4000, S_IFREG = 0x8000, S_IFLNK = 0xA000;
        public const int ENOENT = 2, EPERM = 1, EACCES = 13, EEXIST = 17, ENOTDIR = 20, EISDIR = 21, EINTR = 4, ERANGE = 34;

        // open(2) is variadic: Apple's arm64 ABI passes variadic arguments on the stack, so calling it as
        // a fixed three-argument function would lose `mode`. A C shim in the runtime makes the call.
        [DllImport("__Internal", EntryPoint = "csrt_open", SetLastError = true)] public static extern int open(byte* path, int flags, int mode);
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
        [DllImport("libc", SetLastError = true)] public static extern byte* getcwd(byte* buffer, nuint size);
        [DllImport("libc", SetLastError = true)] public static extern int chdir(byte* path);
        [DllImport("libc", SetLastError = true)] public static extern void* opendir(byte* path);
        [DllImport("libc", SetLastError = true)] public static extern byte* readdir(void* dir);
        [DllImport("libc")] public static extern int closedir(void* dir);
        [DllImport("libc")] public static extern byte* getenv(byte* name);
        [DllImport("libc")] public static extern int setenv(byte* name, byte* value, int overwrite);
        [DllImport("libc")] public static extern int unsetenv(byte* name);
        [DllImport("libc")] public static extern int getpid();
        [DllImport("libc")] public static extern int gethostname(byte* name, nuint length);
        [DllImport("libc")] public static extern byte* strerror(int error);
        [DllImport("libc")] public static extern int getentropy(byte* buffer, nuint length);
        [DllImport("libc")] public static extern long time(long* result);
        [DllImport("libc")] public static extern byte* localtime_r(long* time, byte* result);

#if CSHARPC_TARGET_MACOS
        // struct stat (Darwin arm64, 64-bit ino_t): st_mode @4 (u16), st_mtimespec @48, st_size @96; 144 bytes.
        [DllImport("libc", SetLastError = true)] public static extern int stat(byte* path, byte* buffer);
        [DllImport("libc", SetLastError = true)] public static extern int lstat(byte* path, byte* buffer);
        [DllImport("libc", SetLastError = true)] public static extern int fstat(int fd, byte* buffer);
        public const int StatSize = 144;
        public static int Mode(byte* st) => *(ushort*)(st + 4);
        public static long Size(byte* st) => *(long*)(st + 96);
        public static long MTimeSeconds(byte* st) => *(long*)(st + 48);
        public static long MTimeNanoseconds(byte* st) => *(long*)(st + 56);

        // struct dirent (Darwin, 64-bit ino_t): d_namlen @18 (u16), d_type @20, d_name @21.
        public const int DirentTypeOffset = 20, DirentNameOffset = 21;
#else
        // stat/lstat/fstat go through statx (glibc 2.28+): struct statx has one layout on every Linux
        // architecture, while struct stat does not (st_mode is at offset 24 on x86-64 but 16 on AArch64).
        // struct statx: stx_mode @28 (u16), stx_size @40 (u64), stx_mtime.tv_sec @112 (i64), tv_nsec @120 (u32); 256 bytes.
        [DllImport("libc", SetLastError = true)] private static extern int statx(int dirfd, byte* path, int flags, uint mask, byte* buffer);
        private const int AT_FDCWD = -100, AT_SYMLINK_NOFOLLOW = 0x100, AT_EMPTY_PATH = 0x1000;
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

        // struct dirent (glibc, LP64 Linux: x86-64 and AArch64 alike): d_type @18, d_name @19.
        public const int DirentTypeOffset = 18, DirentNameOffset = 19;
#endif
        public const byte DT_UNKNOWN = 0, DT_DIR = 4, DT_REG = 8, DT_LNK = 10;

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
