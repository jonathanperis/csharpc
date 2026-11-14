// libSystem.Native's C API, the part CoreLib's file-system IL (upstream-types/System.IO.txt) P/Invokes, served by csharpc over its own
// libc layer instead of the pack's shim (the OS boundary stays csharpc's: ADR 0024; linking the shim is an open owner decision,
// docs/21-ownership.md). Each function is exported under libSystem.Native's symbol name ([RuntimeExport]; ModuleCodeGen.PInvoke binds the
// framework's `[DllImport("libSystem.Native", EntryPoint = "SystemNative_X")]` to it), so the framework's marshalling, SafeHandle
// reference counting and error mapping (Interop.GetExceptionForIoErrno) run unchanged; only the native layer is csharpc's.
//
// The semantics are pal_io.c's and pal_errno.c's (dotnet/runtime src/native/libs/System.Native, release/11.0): EINTR is retried where
// they retry it, errno is left as the failing call set it (the framework reads it with Marshal.GetLastSystemError), PAL flags and enums are
// converted to the target's values (never the host's), and struct layouts (FileStatus, DirectoryEntry, TimeSpec, IOVector) are the
// framework's. Security: every descriptor the framework opens carries O_CLOEXEC (it always passes PAL_O_CLOEXEC; mkstemps and pipe add it
// here), paths are passed as the framework's NUL-terminated UTF-8, and nothing here follows a symlink the call does not follow in C.
// Only the entry points the slice's IL references are here; a new one shows up as an unresolved P/Invoke at compile time.
#if !CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class SystemNative
    {
        private const string Trigger = "libSystem.Native!";

        // ---- libc ------------------------------------------------------------------------------------------------
        [DllImport("__Internal", EntryPoint = "csrt_open")] private static extern int open(byte* path, int flags, int mode);
        [DllImport("__Internal", EntryPoint = "csrt_fcntl_int")] private static extern int fcntl_int(int fd, int command, int argument);
        [DllImport("__Internal", EntryPoint = "csrt_fcntl_ptr")] private static extern int fcntl_ptr(int fd, int command, void* argument);
        [DllImport("libc")] private static extern int close(int fd);
        [DllImport("libc")] private static extern nint read(int fd, byte* buffer, nuint count);
        [DllImport("libc")] private static extern nint write(int fd, byte* buffer, nuint count);
        [DllImport("libc")] private static extern nint pread(int fd, byte* buffer, nuint count, long offset);
        [DllImport("libc")] private static extern nint pwrite(int fd, byte* buffer, nuint count, long offset);
        [DllImport("libc")] private static extern nint readv(int fd, void* iov, int count);
        [DllImport("libc")] private static extern nint writev(int fd, void* iov, int count);
        [DllImport("libc")] private static extern long lseek(int fd, long offset, int whence);
        [DllImport("libc")] private static extern int ftruncate(int fd, long length);
        [DllImport("libc")] private static extern int fsync(int fd);
        [DllImport("libc")] private static extern int mkdir(byte* path, int mode);
        [DllImport("libc")] private static extern int rmdir(byte* path);
        [DllImport("libc")] private static extern int unlink(byte* path);
        [DllImport("libc")] private static extern int rename(byte* from, byte* to);
        [DllImport("libc")] private static extern nint readlink(byte* path, byte* buffer, nuint size);
        [DllImport("libc")] private static extern byte* getcwd(byte* buffer, nuint size);
        [DllImport("libc")] private static extern void* opendir(byte* path);
        [DllImport("libc")] private static extern byte* readdir(void* dir);
        [DllImport("libc")] private static extern int closedir(void* dir);
        [DllImport("libc")] private static extern byte* strerror(int error);
        [DllImport("libc")] private static extern nuint strlen(byte* s);
#if !CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern nint preadv(int fd, void* iov, int count, long offset);
        [DllImport("libc")] private static extern nint pwritev(int fd, void* iov, int count, long offset);
        [DllImport("libc")] private static extern int symlink(byte* target, byte* linkPath);
        [DllImport("libc")] private static extern int link(byte* target, byte* linkPath);
        [DllImport("libc")] private static extern int chmod(byte* path, int mode);
        [DllImport("libc")] private static extern int fchmod(int fd, int mode);
        [DllImport("libc")] private static extern int futimens(int fd, long* times);
        [DllImport("libc")] private static extern int utimensat(int dirfd, byte* path, long* times, int flags);
        [DllImport("libc")] private static extern int flock(int fd, int operation);
        [DllImport("libc")] private static extern uint geteuid();
        [DllImport("libc")] private static extern uint getegid();
        [DllImport("libc")] private static extern int getgroups(int size, uint* list);
        [DllImport("libc")] private static extern byte* mkdtemp(byte* template);
        [DllImport("libc")] private static extern int mkostemps(byte* template, int suffixLength, int flags);
#endif

        // ---- target constants ------------------------------------------------------------------------------------
#if CSHARPC_TARGET_WASI
        private const int EINTR = 27, EINVAL = 28, ENOTSUP = 58, EBADF = 8;
        private const int O_RDONLY = 0x4000000, O_WRONLY = 0x10000000, O_RDWR = 0x14000000;
        private const int O_CREAT = 0x1000, O_EXCL = 0x4000, O_TRUNC = 0x8000, O_SYNC = 0x10, O_NOFOLLOW = 0x1000000, O_DIRECTORY = 0x2000, O_CLOEXEC = 0;
        private const int O_NONBLOCK = 0x4;
        private const int F_GETFL = 3;
#elif CSHARPC_TARGET_MACOS
        private const int EINTR = 4, EINVAL = 22, ENOTSUP = 45, EBADF = 9;
        private const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
        private const int O_CREAT = 0x200, O_EXCL = 0x800, O_TRUNC = 0x400, O_SYNC = 0x80, O_NOFOLLOW = 0x100, O_DIRECTORY = 0x100000, O_CLOEXEC = 0x1000000;
        private const int O_NONBLOCK = 0x4;
        private const int F_GETFL = 3, F_SETLK = 8, F_FULLFSYNC = 51;
        private const int AT_FDCWD = -2, AT_SYMLINK_NOFOLLOW = 0x20;
        private const int IOV_MAX = 1024;
        private const uint UF_HIDDEN = 0x8000;
        [DllImport("libc")] private static extern int fchflags(int fd, uint flags);
        [DllImport("libc")] private static extern int lchflags(byte* path, uint flags);
        [DllImport("libc")] private static extern int pipe(int* fds);
        [DllImport("libc")] private static extern int ioctl(int fd, nuint request);
        private const nuint FIOCLEX = 0x20006601;
#else
        private const int EINTR = 4, EINVAL = 22, ENOTSUP = 95, EBADF = 9;
        private const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
        private const int O_CREAT = 0x40, O_EXCL = 0x80, O_TRUNC = 0x200, O_SYNC = 0x101000, O_NOFOLLOW = 0x20000, O_DIRECTORY = 0x10000, O_CLOEXEC = 0x80000;
        private const int O_NONBLOCK = 0x800;
        private const int F_GETFL = 3, F_SETLK = 6;
        private const int AT_FDCWD = -100, AT_SYMLINK_NOFOLLOW = 0x100;
        private const int IOV_MAX = 1024;
        [DllImport("libc")] private static extern int pipe2(int* fds, int flags);
        [DllImport("libc")] private static extern int posix_fadvise(int fd, long offset, long length, int advice);
        [DllImport("libc")] private static extern int fallocate(int fd, int mode, long offset, long length);
#endif

        private static int Errno => Marshal.GetLastSystemError();

        private static int Fail(int errno)
        {
            Marshal.SetLastSystemError(errno);
            return -1;
        }

        private static int Fd(nint fd) => (int)fd;

        // ---- errors (pal_errno.c) --------------------------------------------------------------------------------
        [RuntimeExport("SystemNative_ConvertErrorPlatformToPal", Trigger + "SystemNative_ConvertErrorPlatformToPal")]
        public static int ConvertErrorPlatformToPal(int platformErrno) => ToPal(platformErrno);

        [RuntimeExport("SystemNative_ConvertErrorPalToPlatform", Trigger + "SystemNative_ConvertErrorPalToPlatform")]
        public static int ConvertErrorPalToPlatform(int error) => ToPlatform(error);

        /// <summary>The message of a platform errno copied into the caller's buffer (strerror_r's), NUL-terminated and truncated to it.</summary>
        [RuntimeExport("SystemNative_StrErrorR", Trigger + "SystemNative_StrErrorR")]
        public static byte* StrErrorR(int platformErrno, byte* buffer, int bufferSize)
        {
            if (buffer == null || bufferSize <= 0) return null;
            byte* message = strerror(platformErrno);
            nuint length = message == null ? 0 : strlen(message);
            if (length > (nuint)(bufferSize - 1)) length = (nuint)(bufferSize - 1);
            for (nuint i = 0; i < length; i++) buffer[i] = message[i];
            buffer[length] = 0;
            return buffer;
        }

        // ---- descriptors (pal_io.c) ------------------------------------------------------------------------------
        /// <summary>
        /// SystemNative_Open: the PAL flags converted to the target's (an unknown one is EINVAL, as ConvertOpenFlags), open(2) retried on EINTR.
        /// The framework always passes PAL_O_CLOEXEC, so no descriptor it opens leaks into a child process.
        /// </summary>
        [RuntimeExport("SystemNative_Open", Trigger + "SystemNative_Open")]
        public static nint Open(byte* path, int flags, int mode)
        {
            const int PalAccessMask = 0xF, PalKnown = 0xF | 0x10 | 0x20 | 0x40 | 0x80 | 0x100 | 0x200 | 0x1000;
            if ((flags & ~PalKnown) != 0) return Fail(EINVAL);
            int native;
            switch (flags & PalAccessMask)
            {
                case 0: native = O_RDONLY; break;
                case 1: native = O_WRONLY; break;
                case 2: native = O_RDWR; break;
                default: return Fail(EINVAL);
            }
            if ((flags & 0x10) != 0) native |= O_CLOEXEC;
            if ((flags & 0x20) != 0) native |= O_CREAT;
            if ((flags & 0x40) != 0) native |= O_EXCL;
            if ((flags & 0x80) != 0) native |= O_TRUNC;
            if ((flags & 0x100) != 0) native |= O_SYNC;
            if ((flags & 0x200) != 0) native |= O_NOFOLLOW;
            if ((flags & 0x1000) != 0) native |= O_DIRECTORY;
            int fd;
            while ((fd = open(path, native, mode)) < 0 && Errno == EINTR) { }
            return fd;
        }

        /// <summary>SystemNative_Close: close(2), not retried (a retry after EINTR could close a descriptor another thread just got).</summary>
        [RuntimeExport("SystemNative_Close", Trigger + "SystemNative_Close")]
        public static int Close(nint fd) => close(Fd(fd));

        [RuntimeExport("SystemNative_Read", Trigger + "SystemNative_Read")]
        public static int Read(nint fd, byte* buffer, int count)
        {
            if (count < 0) return Fail(EINVAL);
            nint n;
            while ((n = read(Fd(fd), buffer, (nuint)count)) < 0 && Errno == EINTR) { }
            return (int)n;
        }

        [RuntimeExport("SystemNative_Write", Trigger + "SystemNative_Write")]
        public static int Write(nint fd, byte* buffer, int count)
        {
            if (count < 0) return Fail(EINVAL);
            nint n;
            while ((n = write(Fd(fd), buffer, (nuint)count)) < 0 && Errno == EINTR) { }
            return (int)n;
        }

        // (Non-blocking descriptors: the same calls; EAGAIN is the caller's to handle, as in pal_io.c.)
        [RuntimeExport("SystemNative_ReadFromNonblocking", Trigger + "SystemNative_ReadFromNonblocking")]
        public static int ReadFromNonblocking(nint fd, byte* buffer, int count) => Read(fd, buffer, count);

        [RuntimeExport("SystemNative_WriteToNonblocking", Trigger + "SystemNative_WriteToNonblocking")]
        public static int WriteToNonblocking(nint fd, byte* buffer, int count) => Write(fd, buffer, count);

        [RuntimeExport("SystemNative_PRead", Trigger + "SystemNative_PRead")]
        public static int PRead(nint fd, byte* buffer, int count, long offset)
        {
            if (count < 0) return Fail(EINVAL);
            nint n;
            while ((n = pread(Fd(fd), buffer, (nuint)count, offset)) < 0 && Errno == EINTR) { }
            return (int)n;
        }

        [RuntimeExport("SystemNative_PWrite", Trigger + "SystemNative_PWrite")]
        public static int PWrite(nint fd, byte* buffer, int count, long offset)
        {
            if (count < 0) return Fail(EINVAL);
            nint n;
            while ((n = pwrite(Fd(fd), buffer, (nuint)count, offset)) < 0 && Errno == EINTR) { }
            return (int)n;
        }

        // Vectored I/O: Interop.Sys.IOVector is struct iovec (a pointer and a size_t). More vectors than IOV_MAX are done in batches, as
        // pal_io.c's SystemNative_PReadV/PWriteV do; a short transfer ends the operation, as one call would.
        [RuntimeExport("SystemNative_ReadV", Trigger + "SystemNative_ReadV")]
        public static long ReadV(nint fd, byte* vectors, int count)
        {
            if (count < 0) return Fail(EINVAL);
            nint n;
            while ((n = readv(Fd(fd), vectors, Math.Min(count, IovMax))) < 0 && Errno == EINTR) { }
            return n;
        }

        [RuntimeExport("SystemNative_WriteV", Trigger + "SystemNative_WriteV")]
        public static long WriteV(nint fd, byte* vectors, int count)
        {
            if (count < 0) return Fail(EINVAL);
            nint n;
            while ((n = writev(Fd(fd), vectors, Math.Min(count, IovMax))) < 0 && Errno == EINTR) { }
            return n;
        }

        [RuntimeExport("SystemNative_PReadV", Trigger + "SystemNative_PReadV")]
        public static long PReadV(nint fd, byte* vectors, int count, long offset)
        {
            if (count < 0) return Fail(EINVAL);
            long total = 0;
            int vectorSize = 2 * IntPtr.Size;
            for (int i = 0; i < count; i++)
            {
                byte* b = *(byte**)(vectors + i * vectorSize);
                nuint length = *(nuint*)(vectors + i * vectorSize + IntPtr.Size);
                nint n;
                while ((n = pread(Fd(fd), b, length, offset + total)) < 0 && Errno == EINTR) { }
                if (n < 0) return total > 0 ? total : -1;
                total += n;
                if ((nuint)n != length) break;
            }
            return total;
        }

        [RuntimeExport("SystemNative_PWriteV", Trigger + "SystemNative_PWriteV")]
        public static long PWriteV(nint fd, byte* vectors, int count, long offset)
        {
            if (count < 0) return Fail(EINVAL);
            long total = 0;
            int vectorSize = 2 * IntPtr.Size;
            for (int i = 0; i < count; i++)
            {
                byte* b = *(byte**)(vectors + i * vectorSize);
                nuint length = *(nuint*)(vectors + i * vectorSize + IntPtr.Size);
                nint n;
                while ((n = pwrite(Fd(fd), b, length, offset + total)) < 0 && Errno == EINTR) { }
                if (n < 0) return total > 0 ? total : -1;
                total += n;
                if ((nuint)n != length) break;
            }
            return total;
        }

#if CSHARPC_TARGET_WASI
        private const int IovMax = 1024;
#else
        private const int IovMax = IOV_MAX;
#endif

        [RuntimeExport("SystemNative_LSeek", Trigger + "SystemNative_LSeek")]
        public static long LSeek(nint fd, long offset, int whence)
        {
            if ((uint)whence > 2) return Fail(EINVAL); // SEEK_SET/CUR/END are 0/1/2 on every target
            long r;
            while ((r = lseek(Fd(fd), offset, whence)) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_FTruncate", Trigger + "SystemNative_FTruncate")]
        public static int FTruncate(nint fd, long length)
        {
            int r;
            while ((r = ftruncate(Fd(fd), length)) < 0 && Errno == EINTR) { }
            return r;
        }

        /// <summary>SystemNative_FSync: on macOS F_FULLFSYNC (fsync there leaves data in the drive's cache), falling back to fsync where the file system lacks it.</summary>
        [RuntimeExport("SystemNative_FSync", Trigger + "SystemNative_FSync")]
        public static int FSync(nint fd)
        {
            int r;
#if CSHARPC_TARGET_MACOS
            while ((r = fcntl_int(Fd(fd), F_FULLFSYNC, 0)) < 0 && Errno == EINTR) { }
            if (r == 0 || Errno != ENOTSUP) return r;
#endif
            while ((r = fsync(Fd(fd))) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_FAllocate", Trigger + "SystemNative_FAllocate")]
        public static int FAllocate(nint fd, long offset, long length)
        {
#if CSHARPC_TARGET_LINUX || (!CSHARPC_TARGET_MACOS && !CSHARPC_TARGET_WASI)
            int r;
            while ((r = fallocate(Fd(fd), 0 /* FALLOC_FL_KEEP_SIZE off: the file grows, as .NET's preallocationSize */, offset, length)) < 0 && Errno == EINTR) { }
            return r;
#else
            // pal_io.c on targets without fallocate: no preallocation, and the framework treats ENOTSUP as "not supported here".
            return Fail(ENOTSUP);
#endif
        }

        [RuntimeExport("SystemNative_PosixFAdvise", Trigger + "SystemNative_PosixFAdvise")]
        public static int PosixFAdvise(nint fd, long offset, long length, int advice)
        {
#if CSHARPC_TARGET_MACOS || CSHARPC_TARGET_WASI
            // pal_io.c: no posix_fadvise, nothing to advise (0).
            return 0;
#else
            if ((uint)advice > 5) return EINVAL; // the PAL values are Linux's POSIX_FADV_* (0..5)
            int r;
            while ((r = posix_fadvise(Fd(fd), offset, length, advice)) == EINTR) { }
            return r; // posix_fadvise returns the error number, not -1 + errno
#endif
        }

        [RuntimeExport("SystemNative_FLock", Trigger + "SystemNative_FLock")]
        public static int FLock(nint fd, int operation)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            // LOCK_SH/EX/NB/UN have the same values (1, 2, 4, 8) on Linux and macOS.
            int r;
            while ((r = flock(Fd(fd), operation)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        /// <summary>
        /// SystemNative_LockFileRegion: an advisory POSIX record lock (F_SETLK, never waiting) on [offset, offset + length), FileStream.Lock/Unlock.
        /// LockType: F_RDLCK 0, F_WRLCK 1, F_UNLCK 2 (Linux's values; macOS numbers them 1, 3, 2).
        /// </summary>
        [RuntimeExport("SystemNative_LockFileRegion", Trigger + "SystemNative_LockFileRegion")]
        public static int LockFileRegion(nint fd, long offset, long length, int lockType)
        {
            if (offset < 0 || length < 0) return Fail(EINVAL);
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#elif CSHARPC_TARGET_MACOS
            short type = lockType switch { 0 => 1, 1 => 3, 2 => 2, _ => (short)-1 };
            if (type < 0) return Fail(EINVAL);
            byte* lockData = stackalloc byte[24];   // struct flock: l_start @0, l_len @8, l_pid @16, l_type @20, l_whence @22
            new Span<byte>(lockData, 24).Clear();
            *(long*)lockData = offset;
            *(long*)(lockData + 8) = length;
            *(short*)(lockData + 20) = type;
            int r;
            while ((r = fcntl_ptr(Fd(fd), F_SETLK, lockData)) < 0 && Errno == EINTR) { }
            return r;
#else
            if ((uint)lockType > 2) return Fail(EINVAL);
            byte* lockData = stackalloc byte[32];   // struct flock (LP64): l_type @0, l_whence @2, l_start @8, l_len @16, l_pid @24
            new Span<byte>(lockData, 32).Clear();
            *(short*)lockData = (short)lockType;
            *(long*)(lockData + 8) = offset;
            *(long*)(lockData + 16) = length;
            int r;
            while ((r = fcntl_ptr(Fd(fd), F_SETLK, lockData)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        /// <summary>
        /// SystemNative_FileSystemSupportsLocking: pal_io.c answers whether a failed flock means "locked by someone" or "this file system
        /// can't lock" only on Linux, by the file system's magic; csharpc's answer is that every local file system supports it, which is what
        /// pal_io.c returns for all but NFS and CIFS (where flock is emulated or a no-op).
        /// </summary>
        [RuntimeExport("SystemNative_FileSystemSupportsLocking", Trigger + "SystemNative_FileSystemSupportsLocking")]
        public static int FileSystemSupportsLocking(nint fd, int lockOperation, int accessWrite) => 1;

        [RuntimeExport("SystemNative_FChMod", Trigger + "SystemNative_FChMod")]
        public static int FChMod(nint fd, int mode)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            int r;
            while ((r = fchmod(Fd(fd), mode)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        [RuntimeExport("SystemNative_ChMod", Trigger + "SystemNative_ChMod")]
        public static int ChMod(byte* path, int mode)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            int r;
            while ((r = chmod(path, mode)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        /// <summary>SystemNative_FUTimens: access and modification times of an open file (TimeSpec is struct timespec on LP64).</summary>
        [RuntimeExport("SystemNative_FUTimens", Trigger + "SystemNative_FUTimens")]
        public static int FUTimens(nint fd, long* times)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            int r;
            while ((r = futimens(Fd(fd), times)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        /// <summary>SystemNative_UTimensat: a path's times, the link itself for a symbolic link (AT_SYMLINK_NOFOLLOW, as pal_io.c).</summary>
        [RuntimeExport("SystemNative_UTimensat", Trigger + "SystemNative_UTimensat")]
        public static int UTimensat(byte* path, long* times)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            int r;
            while ((r = utimensat(AT_FDCWD, path, times, AT_SYMLINK_NOFOLLOW)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        [RuntimeExport("SystemNative_Fcntl_GetIsNonBlocking", Trigger + "SystemNative_Fcntl_GetIsNonBlocking")]
        public static int GetIsNonBlocking(nint fd, int* isNonBlocking)
        {
            int flags = fcntl_int(Fd(fd), F_GETFL, 0);
            if (flags < 0) return -1;
            *isNonBlocking = (flags & O_NONBLOCK) != 0 ? 1 : 0;
            return 0;
        }

        /// <summary>SystemNative_Pipe: both ends close-on-exec when PAL_O_CLOEXEC (0x10) is asked, atomically where the target can (pipe2).</summary>
        [RuntimeExport("SystemNative_Pipe", Trigger + "SystemNative_Pipe")]
        public static int Pipe(int* fds, int flags)
        {
            if ((flags & ~0x10) != 0) return Fail(EINVAL);
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#elif CSHARPC_TARGET_MACOS
            int r;
            while ((r = pipe(fds)) < 0 && Errno == EINTR) { }
            if (r == 0 && (flags & 0x10) != 0)
            {
                // FIOCLEX: no variadic argument, unlike fcntl(F_SETFD, FD_CLOEXEC).
                if (ioctl(fds[0], FIOCLEX) < 0 || ioctl(fds[1], FIOCLEX) < 0)
                {
                    int e = Errno;
                    close(fds[0]);
                    close(fds[1]);
                    return Fail(e);
                }
            }
            return r;
#else
            int r;
            while ((r = pipe2(fds, (flags & 0x10) != 0 ? O_CLOEXEC : 0)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        // ---- paths -----------------------------------------------------------------------------------------------
        [RuntimeExport("SystemNative_MkDir", Trigger + "SystemNative_MkDir")]
        public static int MkDir(byte* path, int mode)
        {
            int r;
            while ((r = mkdir(path, mode)) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_RmDir", Trigger + "SystemNative_RmDir")]
        public static int RmDir(byte* path)
        {
            int r;
            while ((r = rmdir(path)) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_Unlink", Trigger + "SystemNative_Unlink")]
        public static int Unlink(byte* path)
        {
            int r;
            while ((r = unlink(path)) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_Rename", Trigger + "SystemNative_Rename")]
        public static int Rename(byte* oldPath, byte* newPath)
        {
            int r;
            while ((r = rename(oldPath, newPath)) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_Link", Trigger + "SystemNative_Link")]
        public static int Link(byte* source, byte* linkPath)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            int r;
            while ((r = link(source, linkPath)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        [RuntimeExport("SystemNative_SymLink", Trigger + "SystemNative_SymLink")]
        public static int SymLink(byte* target, byte* linkPath)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            return symlink(target, linkPath);
#endif
        }

        /// <summary>SystemNative_ReadLink: readlink(2); the framework grows the buffer while the result fills it.</summary>
        [RuntimeExport("SystemNative_ReadLink", Trigger + "SystemNative_ReadLink")]
        public static int ReadLink(byte* path, byte* buffer, int bufferSize)
        {
            if (bufferSize <= 0) return Fail(EINVAL);
            nint n = readlink(path, buffer, (nuint)bufferSize);
            return (int)n;
        }

        [RuntimeExport("SystemNative_GetCwd", Trigger + "SystemNative_GetCwd")]
        public static byte* GetCwd(byte* buffer, int size)
        {
            if (size < 0) { Fail(EINVAL); return null; }
            return getcwd(buffer, (nuint)size);
        }

        /// <summary>SystemNative_MkdTemp: mkdtemp(3) (the directory is created 0700).</summary>
        [RuntimeExport("SystemNative_MkdTemp", Trigger + "SystemNative_MkdTemp")]
        public static byte* MkdTemp(byte* template)
        {
#if CSHARPC_TARGET_WASI
            Fail(ENOTSUP);
            return null;
#else
            byte* r;
            while ((r = mkdtemp(template)) == null && Errno == EINTR) { }
            return r;
#endif
        }

        /// <summary>SystemNative_MksTemps: a new file (0600, O_EXCL by construction) whose descriptor is close-on-exec, as pal_io.c's mkostemps(O_CLOEXEC).</summary>
        [RuntimeExport("SystemNative_MksTemps", Trigger + "SystemNative_MksTemps")]
        public static nint MksTemps(byte* template, int suffixLength)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            int r;
            while ((r = mkostemps(template, suffixLength, O_CLOEXEC)) < 0 && Errno == EINTR) { }
            return r;
#endif
        }

        // ---- directories -----------------------------------------------------------------------------------------
        [RuntimeExport("SystemNative_OpenDir", Trigger + "SystemNative_OpenDir")]
        public static nint OpenDir(byte* path) => (nint)opendir(path);

        [RuntimeExport("SystemNative_CloseDir", Trigger + "SystemNative_CloseDir")]
        public static int CloseDir(nint dir) => closedir((void*)dir);

        /// <summary>
        /// SystemNative_ReadDir: the next entry as Interop.Sys.DirectoryEntry { byte* Name; int NameLength; int InodeType } (Name points into
        /// the DIR's buffer, valid until the next call); -1 at the end, the error number on failure. NameLength is -1 where struct dirent has
        /// no length (the framework then measures the name). InodeType is the DT_* value, which the PAL's NodeType mirrors.
        /// </summary>
        [RuntimeExport("SystemNative_ReadDir", Trigger + "SystemNative_ReadDir")]
        public static int ReadDir(nint dir, byte* entry)
        {
            Marshal.SetLastSystemError(0);
            byte* d = readdir((void*)dir);
            int nameOffset = IntPtr.Size, typeOffset = IntPtr.Size + 4;
            if (d == null)
            {
                int e = Errno;
                new Span<byte>(entry, IntPtr.Size + 8).Clear();
                return e == 0 ? -1 : e;
            }
#if CSHARPC_TARGET_WASI
            // struct dirent (wasi-libc): d_ino @0, d_type @8 (WASI filetype), d_name @9.
            *(byte**)entry = d + 9;
            *(int*)(entry + nameOffset) = -1;
            *(int*)(entry + typeOffset) = d[8] switch { 3 => 4 /* DT_DIR */, 4 => 8 /* DT_REG */, 7 => 10 /* DT_LNK */, 1 => 6 /* DT_BLK */, 2 => 2 /* DT_CHR */, 6 => 12 /* DT_SOCK */, _ => 0 };
#elif CSHARPC_TARGET_MACOS
            // struct dirent (Darwin, 64-bit ino_t): d_namlen @18 (u16), d_type @20, d_name @21.
            *(byte**)entry = d + 21;
            *(int*)(entry + nameOffset) = *(ushort*)(d + 18);
            *(int*)(entry + typeOffset) = d[20];
#else
            // struct dirent (glibc LP64): d_type @18, d_name @19.
            *(byte**)entry = d + 19;
            *(int*)(entry + nameOffset) = -1;
            *(int*)(entry + typeOffset) = d[18];
#endif
            return 0;
        }

        // ---- identity --------------------------------------------------------------------------------------------
#if !CSHARPC_TARGET_WASI
        [RuntimeExport("SystemNative_GetEUid", Trigger + "SystemNative_GetEUid")]
        public static uint GetEUid() => geteuid();

        [RuntimeExport("SystemNative_GetEGid", Trigger + "SystemNative_GetEGid")]
        public static uint GetEGid() => getegid();

        [RuntimeExport("SystemNative_GetGroups", Trigger + "SystemNative_GetGroups")]
        public static int GetGroups(int count, uint* groups) => getgroups(count, groups);
#else
        // WASI has no users: everything is the one user's (pal_io.c's emscripten/WASI build answers 0).
        [RuntimeExport("SystemNative_GetEUid", Trigger + "SystemNative_GetEUid")]
        public static uint GetEUid() => 0;

        [RuntimeExport("SystemNative_GetEGid", Trigger + "SystemNative_GetEGid")]
        public static uint GetEGid() => 0;

        [RuntimeExport("SystemNative_GetGroups", Trigger + "SystemNative_GetGroups")]
        public static int GetGroups(int count, uint* groups) => 0;
#endif

        // ---- hidden flag (macOS UF_HIDDEN) ----------------------------------------------------------------------
        [RuntimeExport("SystemNative_CanGetHiddenFlag", Trigger + "SystemNative_CanGetHiddenFlag")]
#if CSHARPC_TARGET_MACOS
        public static int CanGetHiddenFlag() => 1;
#else
        public static int CanGetHiddenFlag() => 0;
#endif

        [RuntimeExport("SystemNative_LChflagsCanSetHiddenFlag", Trigger + "SystemNative_LChflagsCanSetHiddenFlag")]
#if CSHARPC_TARGET_MACOS
        public static int LChflagsCanSetHiddenFlag() => 1;
#else
        public static int LChflagsCanSetHiddenFlag() => 0;
#endif

        [RuntimeExport("SystemNative_LChflags", Trigger + "SystemNative_LChflags")]
        public static int LChflags(byte* path, uint flags)
        {
#if CSHARPC_TARGET_MACOS
            int r;
            while ((r = lchflags(path, flags)) < 0 && Errno == EINTR) { }
            return r;
#else
            return Fail(ENOTSUP);
#endif
        }

        [RuntimeExport("SystemNative_FChflags", Trigger + "SystemNative_FChflags")]
        public static int FChflags(nint fd, uint flags)
        {
#if CSHARPC_TARGET_MACOS
            int r;
            while ((r = fchflags(Fd(fd), flags)) < 0 && Errno == EINTR) { }
            return r;
#else
            return Fail(ENOTSUP);
#endif
        }

        // ---- file status (Interop.Sys.FileStatus) ----------------------------------------------------------------
        // FileStatus: int Flags @0, int Mode @4, uint Uid @8, uint Gid @12, long Size @16, ATime @24, ATimeNsec @32, MTime @40, MTimeNsec @48,
        // CTime @56, CTimeNsec @64, BirthTime @72, BirthTimeNsec @80, Dev @88, RDev @96, Ino @104, uint UserFlags @112, int HardLinkCount @116.
        private const int FileStatusSize = 120;

        [RuntimeExport("SystemNative_Stat", Trigger + "SystemNative_Stat")]
        public static int Stat(byte* path, byte* output) => StatPath(path, output, follow: true);

        [RuntimeExport("SystemNative_LStat", Trigger + "SystemNative_LStat")]
        public static int LStat(byte* path, byte* output) => StatPath(path, output, follow: false);

#if CSHARPC_TARGET_WASI || CSHARPC_TARGET_MACOS
        [DllImport("libc")] private static extern int stat(byte* path, byte* buffer);
        [DllImport("libc")] private static extern int lstat(byte* path, byte* buffer);
        [DllImport("libc")] private static extern int fstat(int fd, byte* buffer);
        private const int NativeStatSize = 144;

        private static int StatPath(byte* path, byte* output, bool follow)
        {
            byte* st = stackalloc byte[NativeStatSize];
            int r;
            while ((r = follow ? stat(path, st) : lstat(path, st)) < 0 && Errno == EINTR) { }
            if (r == 0) Convert(st, output);
            return r;
        }

        [RuntimeExport("SystemNative_FStat", Trigger + "SystemNative_FStat")]
        public static int FStat(nint fd, byte* output)
        {
            byte* st = stackalloc byte[NativeStatSize];
            int r;
            while ((r = fstat(Fd(fd), st)) < 0 && Errno == EINTR) { }
            if (r == 0) Convert(st, output);
            return r;
        }
#endif

#if CSHARPC_TARGET_WASI
        // struct stat (wasi-libc, wasm32): st_dev @0, st_ino @8, st_nlink @16 (u64), st_mode @24, st_uid @28, st_gid @32, st_rdev @40,
        // st_size @48, st_atim @72, st_mtim @88, st_ctim @104 (timespec: i64 seconds, 32-bit long nanoseconds).
        private static void Convert(byte* st, byte* fs)
        {
            new Span<byte>(fs, FileStatusSize).Clear();
            *(int*)(fs + 4) = *(int*)(st + 24);
            *(uint*)(fs + 8) = *(uint*)(st + 28);
            *(uint*)(fs + 12) = *(uint*)(st + 32);
            *(long*)(fs + 16) = *(long*)(st + 48);
            *(long*)(fs + 24) = *(long*)(st + 72); *(long*)(fs + 32) = *(int*)(st + 80);
            *(long*)(fs + 40) = *(long*)(st + 88); *(long*)(fs + 48) = *(int*)(st + 96);
            *(long*)(fs + 56) = *(long*)(st + 104); *(long*)(fs + 64) = *(int*)(st + 112);
            *(long*)(fs + 88) = *(long*)st;
            *(long*)(fs + 96) = *(long*)(st + 40);
            *(long*)(fs + 104) = *(long*)(st + 8);
            *(int*)(fs + 116) = (int)*(ulong*)(st + 16);
        }
#elif CSHARPC_TARGET_MACOS
        // struct stat (Darwin, 64-bit ino_t): st_dev @0 (i32), st_mode @4 (u16), st_nlink @6 (u16), st_ino @8, st_uid @16, st_gid @20,
        // st_rdev @24 (i32), st_atimespec @32, st_mtimespec @48, st_ctimespec @64, st_birthtimespec @80, st_size @96, st_flags @116.
        private static void Convert(byte* st, byte* fs)
        {
            new Span<byte>(fs, FileStatusSize).Clear();
            *(int*)fs = 1; // FileStatusFlags.HasBirthTime
            *(int*)(fs + 4) = *(ushort*)(st + 4);
            *(uint*)(fs + 8) = *(uint*)(st + 16);
            *(uint*)(fs + 12) = *(uint*)(st + 20);
            *(long*)(fs + 16) = *(long*)(st + 96);
            *(long*)(fs + 24) = *(long*)(st + 32); *(long*)(fs + 32) = *(long*)(st + 40);
            *(long*)(fs + 40) = *(long*)(st + 48); *(long*)(fs + 48) = *(long*)(st + 56);
            *(long*)(fs + 56) = *(long*)(st + 64); *(long*)(fs + 64) = *(long*)(st + 72);
            *(long*)(fs + 72) = *(long*)(st + 80); *(long*)(fs + 80) = *(long*)(st + 88);
            *(long*)(fs + 88) = *(int*)st;
            *(long*)(fs + 96) = *(int*)(st + 24);
            *(long*)(fs + 104) = *(long*)(st + 8);
            *(uint*)(fs + 112) = *(uint*)(st + 116);
            *(int*)(fs + 116) = *(ushort*)(st + 6);
        }
#else
        // Linux: statx, whose layout is the same on every architecture (struct stat is not: st_mode is at 24 on x86-64, 16 on AArch64).
        // pal_io.c reads struct stat, which has no birth time: no HasBirthTime, as there.
        [DllImport("libc")] private static extern int statx(int dirfd, byte* path, int flags, uint mask, byte* buffer);
        private const int AT_EMPTY_PATH = 0x1000;
        private const uint STATX_BASIC_STATS = 0x7FF;

        private static int StatPath(byte* path, byte* output, bool follow)
        {
            byte* st = stackalloc byte[256];
            int r;
            while ((r = statx(AT_FDCWD, path, follow ? 0 : AT_SYMLINK_NOFOLLOW, STATX_BASIC_STATS, st)) < 0 && Errno == EINTR) { }
            if (r == 0) Convert(st, output);
            return r;
        }

        [RuntimeExport("SystemNative_FStat", Trigger + "SystemNative_FStat")]
        public static int FStat(nint fd, byte* output)
        {
            byte* st = stackalloc byte[256];
            byte empty = 0;
            int r;
            while ((r = statx(Fd(fd), &empty, AT_EMPTY_PATH, STATX_BASIC_STATS, st)) < 0 && Errno == EINTR) { }
            if (r == 0) Convert(st, output);
            return r;
        }

        // struct statx: stx_nlink @16, stx_uid @20, stx_gid @24, stx_mode @28 (u16), stx_ino @32, stx_size @40, stx_atime @64, stx_ctime @96,
        // stx_mtime @112 (statx_timestamp: i64 seconds, u32 nanoseconds), stx_rdev_major/minor @128/@132, stx_dev_major/minor @136/@140.
        private static void Convert(byte* st, byte* fs)
        {
            new Span<byte>(fs, FileStatusSize).Clear();
            *(int*)(fs + 4) = *(ushort*)(st + 28);
            *(uint*)(fs + 8) = *(uint*)(st + 20);
            *(uint*)(fs + 12) = *(uint*)(st + 24);
            *(long*)(fs + 16) = *(long*)(st + 40);
            *(long*)(fs + 24) = *(long*)(st + 64); *(long*)(fs + 32) = *(uint*)(st + 72);
            *(long*)(fs + 40) = *(long*)(st + 112); *(long*)(fs + 48) = *(uint*)(st + 120);
            *(long*)(fs + 56) = *(long*)(st + 96); *(long*)(fs + 64) = *(uint*)(st + 104);
            *(long*)(fs + 88) = (long)MakeDev(*(uint*)(st + 136), *(uint*)(st + 140));
            *(long*)(fs + 96) = (long)MakeDev(*(uint*)(st + 128), *(uint*)(st + 132));
            *(long*)(fs + 104) = *(long*)(st + 32);
            *(int*)(fs + 116) = *(int*)(st + 16);
        }

        // glibc's makedev, the dev_t struct stat would hold.
        private static ulong MakeDev(uint major, uint minor) =>
            ((ulong)(major & 0xFFFFF000) << 32) | ((ulong)(major & 0xFFF) << 8) | ((ulong)(minor & 0xFFFFFF00) << 12) | (minor & 0xFF);
#endif

        // ---- copy ------------------------------------------------------------------------------------------------
        /// <summary>
        /// SystemNative_CopyFile: the source's bytes into the destination (both open; the destination new or truncated by the framework), then
        /// the source's permission bits (pal_io.c: fchmod with st_mode's 07777 bits) and its access and modification times, as File.Copy keeps them.
        /// </summary>
        [RuntimeExport("SystemNative_CopyFile", Trigger + "SystemNative_CopyFile")]
        public static int CopyFile(nint source, nint destination, long sourceLength)
        {
            int inFd = Fd(source), outFd = Fd(destination);
            byte* buffer = stackalloc byte[64 * 1024];
            while (true)
            {
                nint n;
                while ((n = read(inFd, buffer, 64 * 1024)) < 0 && Errno == EINTR) { }
                if (n < 0) return -1;
                if (n == 0) break;
                for (nint written = 0; written < n;)
                {
                    nint w;
                    while ((w = write(outFd, buffer + written, (nuint)(n - written))) < 0 && Errno == EINTR) { }
                    if (w < 0) return -1;
                    written += w;
                }
            }
#if !CSHARPC_TARGET_WASI
            byte* status = stackalloc byte[FileStatusSize];
            if (FStat(source, status) != 0) return -1;
            int r;
            while ((r = fchmod(outFd, *(int*)(status + 4) & 0xFFF)) < 0 && Errno == EINTR) { }
            if (r != 0) return -1;
            long* times = stackalloc long[4];
            times[0] = *(long*)(status + 24); times[1] = *(long*)(status + 32);
            times[2] = *(long*)(status + 40); times[3] = *(long*)(status + 48);
            while ((r = futimens(outFd, times)) < 0 && Errno == EINTR) { }
            // (pal_io.c ignores a failure to copy the times: the data and permissions are what matter)
#endif
            return 0;
        }

        // ---- mount points ----------------------------------------------------------------------------------------
        /// <summary>
        /// SystemNative_GetAllMountPoints: calls onFound(context, mountPoint) for each mounted file system (Directory.GetLogicalDrives). Linux
        /// reads /proc/self/mounts as pal_mount.c does (setmntent/getmntent); macOS and WASI have only "/".
        /// </summary>
        [RuntimeExport("SystemNative_GetAllMountPoints", Trigger + "SystemNative_GetAllMountPoints")]
        public static int GetAllMountPoints(delegate* unmanaged<void*, byte*, void> onFound, void* context)
        {
#if CSHARPC_TARGET_MACOS || CSHARPC_TARGET_WASI
            byte* root = stackalloc byte[2];
            root[0] = (byte)'/'; root[1] = 0;
            onFound(context, root);
            return 0;
#else
            byte* path = stackalloc byte[] { (byte)'/', (byte)'p', (byte)'r', (byte)'o', (byte)'c', (byte)'/', (byte)'s', (byte)'e', (byte)'l', (byte)'f',
                (byte)'/', (byte)'m', (byte)'o', (byte)'u', (byte)'n', (byte)'t', (byte)'s', 0 };
            nint fd = Open(path, 0x10 /* PAL_O_CLOEXEC, O_RDONLY */, 0);
            if (fd < 0) return -1;
            var text = new System.Collections.Generic.List<byte>();
            byte* chunk = stackalloc byte[4096];
            int n;
            while ((n = Read(fd, chunk, 4096)) > 0) for (int i = 0; i < n; i++) text.Add(chunk[i]);
            close((int)fd);
            if (n < 0) return -1;
            // Each line: device, mount point (with \040-style octal escapes), type, options, two numbers.
            var line = text.ToArray();
            byte* point = stackalloc byte[4096];
            for (int start = 0; start < line.Length;)
            {
                int end = start;
                while (end < line.Length && line[end] != (byte)'\n') end++;
                int field = start;
                while (field < end && line[field] != (byte)' ') field++;
                field++;
                int length = 0;
                for (int i = field; i < end && line[i] != (byte)' ' && length < 4095; i++)
                {
                    if (line[i] == (byte)'\\' && i + 3 < end && IsOctal(line[i + 1]) && IsOctal(line[i + 2]) && IsOctal(line[i + 3]))
                    {
                        point[length++] = (byte)(((line[i + 1] - '0') << 6) | ((line[i + 2] - '0') << 3) | (line[i + 3] - '0'));
                        i += 3;
                    }
                    else point[length++] = line[i];
                }
                if (length > 0)
                {
                    point[length] = 0;
                    onFound(context, point);
                }
                start = end + 1;
            }
            return 0;
#endif
        }

        private static bool IsOctal(byte b) => b >= (byte)'0' && b <= (byte)'7';

        // ---- errno <-> Interop.Error (pal_errno.c; PAL values are the framework's Interop.Error) --------------------
        private static int ToPal(int platform) => PalTables.ToPal(platform);
        private static int ToPlatform(int pal) => PalTables.FromPal(pal);
    }
}
#endif
