// libSystem.Native's C API, the part CoreLib's file-system IL (upstream-types/System.IO.txt) P/Invokes, served by csharpc over its own
// libc layer instead of the pack's shim (the OS boundary stays csharpc's: ADR 0024; linking the shim is an open owner decision,
// docs/21-ownership.md). Each function is exported under libSystem.Native's symbol name ([RuntimeExport]; ModuleCodeGen.PInvoke binds the
// framework's `[DllImport("libSystem.Native", EntryPoint = "SystemNative_X")]` to it), so the framework's marshalling, SafeHandle
// reference counting and error mapping (Interop.GetExceptionForIoErrno) run unchanged; only the native layer is csharpc's.
//
// The semantics are pal_io.c's, pal_mount.c's, pal_time.c's and pal_errno.c's (dotnet/runtime src/native/libs/System.Native, release/11.0),
// function by function: EINTR is retried where they retry it (and a close/closedir interrupted by a signal is success, as there), errno is
// left as the failing call set it (the framework reads it with Marshal.GetLastSystemError), PAL flags and enums are converted to the
// target's values (never the host's), and struct layouts (FileStatus, DirectoryEntry, TimeSpec, IOVector) are the framework's.
// Security (docs/il-first/System.IO.md, "Security review"): every descriptor the framework opens carries O_CLOEXEC (it always passes
// PAL_O_CLOEXEC; mkstemps and pipe add it here) and O_NOCTTY; new files and directories get the framework's mode through the umask;
// symbolic links are followed exactly where the C calls follow them (stat/open/chmod do, lstat/utimensat/readlink/unlink/rename do not);
// paths are the framework's NUL-terminated UTF-8 (it rejects embedded NULs first) and are never copied into fixed buffers here; every
// buffer written is bounded by the size the caller passed.
// Only the entry points the slice's IL references are here; a new one shows up as an unresolved P/Invoke at compile time.
#if !CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class SystemNative
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
#if CSHARPC_TARGET_MACOS || CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern int strerror_r(int error, byte* buffer, nuint size);
#else
        [DllImport("libc")] private static extern byte* strerror_r(int error, byte* buffer, nuint size);   // glibc's GNU strerror_r
#endif
        [DllImport("libc")] private static extern int poll(void* fds, nuint count, int timeout);
        [DllImport("libc")] private static extern int symlink(byte* target, byte* linkPath);
        [DllImport("libc")] private static extern int link(byte* target, byte* linkPath);
        [DllImport("libc")] private static extern int futimens(int fd, long* times);
        [DllImport("libc")] private static extern int utimensat(int dirfd, byte* path, long* times, int flags);
#if !CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern nint preadv(int fd, void* iov, int count, long offset);
        [DllImport("libc")] private static extern nint pwritev(int fd, void* iov, int count, long offset);
        [DllImport("libc")] private static extern int chmod(byte* path, int mode);
        [DllImport("libc")] private static extern int fchmod(int fd, int mode);
        [DllImport("libc")] private static extern int flock(int fd, int operation);
        [DllImport("libc")] private static extern int fstatfs(int fd, byte* buffer);
        [DllImport("libc")] private static extern int statfs(byte* path, byte* buffer);
        [DllImport("libc")] private static extern int statvfs(byte* path, byte* buffer);
        [DllImport("libc")] private static extern byte* realpath(byte* path, byte* resolved);
        [DllImport("libc")] private static extern uint geteuid();
        [DllImport("libc")] private static extern uint getegid();
        [DllImport("libc")] private static extern int getgroups(int size, uint* list);
        [DllImport("libc")] private static extern byte* mkdtemp(byte* template);
        [DllImport("libc")] private static extern int mkostemps(byte* template, int suffixLength, int flags);
#endif

        // ---- target constants ------------------------------------------------------------------------------------
#if CSHARPC_TARGET_WASI
        private const int EINTR = 27, EINVAL = 28, ENOTSUP = 58, EBADF = 8, EAGAIN = 6, EPIPE = 64, EPERM = 63, ERANGE = 68;
        private const int O_RDONLY = 0x4000000, O_WRONLY = 0x10000000, O_RDWR = 0x14000000;
        // (no O_NOCTTY: WASI has no controlling terminal, and pal_io.c leaves it out on WASM)
        private const int O_CREAT = 0x1000, O_EXCL = 0x4000, O_TRUNC = 0x8000, O_SYNC = 0x10, O_NOFOLLOW = 0x1000000, O_DIRECTORY = 0x2000, O_CLOEXEC = 0, O_NOCTTY = 0;
        private const int O_NONBLOCK = 0x4;
        private const int F_GETFL = 3, F_SETFL = 4;
        private const int AT_FDCWD = -2, AT_SYMLINK_NOFOLLOW = 0x1;
        private const short POLLIN = 0x1, POLLOUT = 0x2, POLLERR = 0x1000, POLLHUP = 0x2000;
#elif CSHARPC_TARGET_MACOS
        private const int EINTR = 4, EINVAL = 22, ENOTSUP = 45, EBADF = 9, EAGAIN = 35, EPIPE = 32, EPERM = 1, ERANGE = 34;
        private const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
        private const int O_CREAT = 0x200, O_EXCL = 0x800, O_TRUNC = 0x400, O_SYNC = 0x80, O_NOFOLLOW = 0x100, O_DIRECTORY = 0x100000, O_CLOEXEC = 0x1000000, O_NOCTTY = 0x20000;
        private const int O_NONBLOCK = 0x4;
        private const int F_GETFL = 3, F_SETFL = 4, F_SETLK = 8, F_FULLFSYNC = 51, F_PREALLOCATE = 42;
        private const short POLLIN = 0x1, POLLOUT = 0x4, POLLERR = 0x8, POLLHUP = 0x10;
        [DllImport("libc")] private static extern int fcopyfile(int from, int to, void* state, uint flags);
        [DllImport("libc")] private static extern int getfsstat(byte* buffer, int bufferSize, int mode);
        private const int AT_FDCWD = -2, AT_SYMLINK_NOFOLLOW = 0x20;
        private const int IOV_MAX = 1024;
        private const uint UF_HIDDEN = 0x8000;
        [DllImport("libc")] private static extern int fchflags(int fd, uint flags);
        [DllImport("libc")] private static extern int lchflags(byte* path, uint flags);
        [DllImport("libc")] private static extern int pipe(int* fds);
        [DllImport("libc")] private static extern int ioctl(int fd, nuint request);
        private const nuint FIOCLEX = 0x20006601;
#else
        private const int EINTR = 4, EINVAL = 22, ENOTSUP = 95, EBADF = 9, EAGAIN = 11, EPIPE = 32, EPERM = 1, ENOSYS = 38;
        private const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
        private const int O_CREAT = 0x40, O_EXCL = 0x80, O_TRUNC = 0x200, O_SYNC = 0x101000, O_NOFOLLOW = 0x20000, O_DIRECTORY = 0x10000, O_CLOEXEC = 0x80000, O_NOCTTY = 0x100;
        private const int O_NONBLOCK = 0x800;
        private const int F_GETFL = 3, F_SETFL = 4, F_SETLK = 6;
        private const short POLLIN = 0x1, POLLOUT = 0x4, POLLERR = 0x8, POLLHUP = 0x10;
        [DllImport("libc")] private static extern int ioctl(int fd, nuint request, int argument);
        [DllImport("libc")] private static extern nint copy_file_range(int inFd, long* inOffset, int outFd, long* outOffset, nuint length, uint flags);
        [DllImport("libc")] private static extern nint sendfile(int outFd, int inFd, long* offset, nuint count);
        [DllImport("libc")] private static extern int uname(byte* buffer);
        [DllImport("libc")] private static extern void* setmntent(byte* file, byte* mode);
        [DllImport("libc")] private static extern void* getmntent_r(void* stream, byte* entry, byte* buffer, int bufferSize);
        [DllImport("libc")] private static extern int endmntent(void* stream);
        private const nuint FICLONE = 0x40049409;
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
        // Error_EHOSTNOTFOUND and Error_ESOCKETERROR (name lookup, unidentified socket error) have no errno: pal_error_common.h gives them
        // negative platform values, which SocketException reports as its NativeErrorCode and StrErrorR names.
        public static int ConvertErrorPalToPlatform(int error) => error switch { 0x20001 => -0x20001, 0x20002 => -0x20002, _ => ToPlatform(error) };

        /// <summary>
        /// SystemNative_StrErrorR: the message of a platform errno, through strerror_r (strerror is not thread-safe), as pal_error_common.h:
        /// glibc's GNU variant returns the message (in the buffer or a static string); the POSIX variant (macOS, WASI) fills the buffer, and
        /// a message the buffer truncates is null.
        /// </summary>
        [RuntimeExport("SystemNative_StrErrorR", Trigger + "SystemNative_StrErrorR")]
        public static byte* StrErrorR(int platformErrno, byte* buffer, int bufferSize)
        {
            if (buffer == null || bufferSize <= 0) return null;
            if (platformErrno < 0)
            {
                // Not a system error (pal_error_common.h): a failed name lookup is getaddrinfo's EAI_NONAME text, an unidentified socket
                // error a fixed one; both copied truncated to the buffer, as SafeStringCopy.
                byte* text = null;
#if !CSHARPC_TARGET_WASI
                if (platformErrno == -0x20001) text = gai_strerror(EAI_NONAME_PLATFORM);
#endif
                if (text != null) return CopyTruncated(text, buffer, bufferSize);
                if (platformErrno == -0x20002)
                {
                    fixed (byte* unknown = "Unknown socket error\0"u8) return CopyTruncated(unknown, buffer, bufferSize);
                }
            }
#if CSHARPC_TARGET_MACOS || CSHARPC_TARGET_WASI
            int error = strerror_r(platformErrno, buffer, (nuint)bufferSize);
            return error == ERANGE ? null : buffer;
#else
            return strerror_r(platformErrno, buffer, (nuint)bufferSize);
#endif
        }

        private static byte* CopyTruncated(byte* text, byte* buffer, int bufferSize)
        {
            int i = 0;
            for (; i < bufferSize - 1 && text[i] != 0; i++) buffer[i] = text[i];
            buffer[i] = 0;
            return buffer;
        }

#if !CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern byte* gai_strerror(int error);
#if CSHARPC_TARGET_MACOS
        private const int EAI_NONAME_PLATFORM = 8;
#else
        private const int EAI_NONAME_PLATFORM = -2;
#endif
#endif

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
            native |= O_NOCTTY; // a terminal device opened by path never becomes the controlling terminal (pal_io.c's default)
            int fd;
            while ((fd = open(path, native, mode)) < 0 && Errno == EINTR) { }
            return fd;
        }

        /// <summary>
        /// SystemNative_Close: close(2), not retried (a retry after EINTR could close a descriptor another thread just got); EINTR is
        /// success, as pal_io.c: on every supported kernel the descriptor is released by then.
        /// </summary>
        [RuntimeExport("SystemNative_Close", Trigger + "SystemNative_Close")]
        public static int Close(nint fd)
        {
            int r = close(Fd(fd));
            return r < 0 && Errno == EINTR ? 0 : r;
        }

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

        // Non-blocking descriptors (an anonymous pipe's async end): on EAGAIN wait in poll(2) until the descriptor is ready, then retry; a
        // hang-up with nothing to read is the end of the stream (0), one with no room to write is EPIPE, as pal_io.c.
        [RuntimeExport("SystemNative_ReadFromNonblocking", Trigger + "SystemNative_ReadFromNonblocking")]
        public static int ReadFromNonblocking(nint fd, byte* buffer, int count)
        {
            while (true)
            {
                int r = Read(fd, buffer, count);
                if (r != -1 || Errno != EAGAIN) return r;
                int ready = WaitReady(fd, POLLIN);
                if (ready <= 0) return ready;
            }
        }

        [RuntimeExport("SystemNative_WriteToNonblocking", Trigger + "SystemNative_WriteToNonblocking")]
        public static int WriteToNonblocking(nint fd, byte* buffer, int count)
        {
            while (true)
            {
                int r = Write(fd, buffer, count);
                if (r != -1 || Errno != EAGAIN) return r;
                int ready = WaitReady(fd, POLLOUT);
                if (ready <= 0) return ready;
            }
        }

        /// <summary>
        /// Blocks until fd is ready for <paramref name="events"/>: 1 to retry, -1 with errno set (poll failed, or EPIPE for a hung-up write
        /// end), 0 for a hung-up read end with no data (end of stream).
        /// </summary>
        private static int WaitReady(nint fd, short events)
        {
            // struct pollfd { int fd; short events; short revents; }
            byte* pfd = stackalloc byte[8];
            *(int*)pfd = Fd(fd);
            *(short*)(pfd + 4) = events;
            *(short*)(pfd + 6) = 0;
            int r;
            while ((r = poll(pfd, 1, -1)) < 0 && Errno == EINTR) { }
            if (r < 0) return -1;
            short revents = *(short*)(pfd + 6);
            if ((revents & (POLLHUP | POLLERR)) != 0 && (revents & events) == 0)
                return events == POLLIN ? 0 : Fail(EPIPE);
            return 1;
        }

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

        // Vectored I/O: Interop.Sys.IOVector is struct iovec (a pointer and a size_t). At most IOV_MAX vectors per call, and on macOS no more
        // than INT_MAX bytes in all (preadv/pwritev fail with EINVAL past it there): pal_io.c's GetAllowedVectorCount. The framework handles
        // the short transfer. WASI's preadv/pwritev are done one vector at a time, as pal_io.c does on WASM.
        private static int AllowedVectorCount(byte* vectors, int count)
        {
            int allowed = Math.Min(count, IovMax);
#if CSHARPC_TARGET_MACOS
            ulong total = 0;
            for (int i = 0; i < allowed; i++)
            {
                total += *(ulong*)(vectors + i * 16 + 8);
                if (total > int.MaxValue) return i;
            }
#endif
            return allowed;
        }

        [RuntimeExport("SystemNative_ReadV", Trigger + "SystemNative_ReadV")]
        public static long ReadV(nint fd, byte* vectors, int count)
        {
            if (count < 0) return Fail(EINVAL);
            int allowed = AllowedVectorCount(vectors, count);
            while (true)
            {
                nint n;
                while ((n = readv(Fd(fd), vectors, allowed)) < 0 && Errno == EINTR) { }
                if (n != -1 || Errno != EAGAIN) return n;
                int ready = WaitReady(fd, POLLIN);
                if (ready <= 0) return ready;
            }
        }

        [RuntimeExport("SystemNative_WriteV", Trigger + "SystemNative_WriteV")]
        public static long WriteV(nint fd, byte* vectors, int count)
        {
            if (count < 0) return Fail(EINVAL);
            int allowed = AllowedVectorCount(vectors, count);
            while (true)
            {
                nint n;
                while ((n = writev(Fd(fd), vectors, allowed)) < 0 && Errno == EINTR) { }
                if (n != -1 || Errno != EAGAIN) return n;
                int ready = WaitReady(fd, POLLOUT);
                if (ready <= 0) return ready;
            }
        }

        [RuntimeExport("SystemNative_PReadV", Trigger + "SystemNative_PReadV")]
        public static long PReadV(nint fd, byte* vectors, int count, long offset)
        {
            if (count < 0) return Fail(EINVAL);
#if !CSHARPC_TARGET_WASI
            nint r;
            while ((r = preadv(Fd(fd), vectors, AllowedVectorCount(vectors, count), offset)) < 0 && Errno == EINTR) { }
            return r;
#else
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
#endif
        }

        [RuntimeExport("SystemNative_PWriteV", Trigger + "SystemNative_PWriteV")]
        public static long PWriteV(nint fd, byte* vectors, int count, long offset)
        {
            if (count < 0) return Fail(EINVAL);
#if !CSHARPC_TARGET_WASI
            nint r;
            while ((r = pwritev(Fd(fd), vectors, AllowedVectorCount(vectors, count), offset)) < 0 && Errno == EINTR) { }
            return r;
#else
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
#endif
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

        /// <summary>
        /// SystemNative_FAllocate (FileStreamOptions.PreallocationSize): reserves the blocks without changing the file's length — Linux's
        /// fallocate with FALLOC_FL_KEEP_SIZE, macOS's F_PREALLOCATE (all or nothing, from the physical end of the file) — as pal_io.c.
        /// </summary>
        [RuntimeExport("SystemNative_FAllocate", Trigger + "SystemNative_FAllocate")]
        public static int FAllocate(nint fd, long offset, long length)
        {
#if CSHARPC_TARGET_MACOS
            int r;
            byte* store = stackalloc byte[32];  // fstore_t: fst_flags @0, fst_posmode @4, fst_offset @8, fst_length @16, fst_bytesalloc @24
            *(uint*)store = 0x4;                // F_ALLOCATEALL
            *(int*)(store + 4) = 3;             // F_PEOFPOSMODE
            *(long*)(store + 8) = offset;
            *(long*)(store + 16) = length;
            *(long*)(store + 24) = 0;
            while ((r = fcntl_ptr(Fd(fd), F_PREALLOCATE, store)) == -1 && Errno == EINTR) { }
            return r;
#elif CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);   // EOPNOTSUPP, the same number on WASI
#else
            int r;
            while ((r = fallocate(Fd(fd), 1 /* FALLOC_FL_KEEP_SIZE */, offset, length)) == -1 && Errno == EINTR) { }
            return r;
#endif
        }

        /// <summary>SystemNative_PosixFAdvise: returns the error number (0 on success), not -1; ENOTSUP where there is no posix_fadvise (macOS).</summary>
        [RuntimeExport("SystemNative_PosixFAdvise", Trigger + "SystemNative_PosixFAdvise")]
        public static int PosixFAdvise(nint fd, long offset, long length, int advice)
        {
#if CSHARPC_TARGET_MACOS
            return ENOTSUP;
#elif CSHARPC_TARGET_WASI
            return 0;   // a hint; the framework ignores ENOTSUP here as it would ignore success
#else
            if ((uint)advice > 5) return EINVAL; // the PAL values are Linux's POSIX_FADV_* (0..5)
            return posix_fadvise(Fd(fd), offset, length, advice);
#endif
        }

        [RuntimeExport("SystemNative_FLock", Trigger + "SystemNative_FLock")]
        public static int FLock(nint fd, int operation)
        {
#if CSHARPC_TARGET_WASI
            return EINTR;   // pal_io.c on WASI: no locking, a non-negative result (FileSystemSupportsLocking says 0 there first)
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
        public static int LockFileRegion(nint fd, long offset, long length, short lockType)
        {
#if CSHARPC_TARGET_WASI
            return EINTR;   // pal_io.c on WASI: a non-negative result, no lock
#else
            if (offset < 0 || length < 0) return Fail(EINVAL);
#if CSHARPC_TARGET_MACOS
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
#endif
        }

        /// <summary>
        /// SystemNative_FileSystemSupportsLocking: whether FileStream takes the advisory flock for FileShare. A shared lock on a writable
        /// descriptor is skipped on NFS, CIFS and SMB, where LOCK_SH drops writes silently (dotnet/runtime#44546, #53182): Linux tells them by
        /// the file system's magic (fstatfs f_type), macOS by its name (f_fstypename), as pal_io.c. WASI has no locking (0).
        /// </summary>
        [RuntimeExport("SystemNative_FileSystemSupportsLocking", Trigger + "SystemNative_FileSystemSupportsLocking")]
        public static int FileSystemSupportsLocking(nint fd, int lockOperation, int accessWrite)
        {
#if CSHARPC_TARGET_WASI
            return 0;
#else
            if (lockOperation == 2 /* PAL_LOCK_EX */ || accessWrite == 0) return 1;
            byte* st = stackalloc byte[2168];   // struct statfs: 120 bytes on Linux, 2168 on macOS
            int r;
            while ((r = fstatfs(Fd(fd), st)) == -1 && Errno == EINTR) { }
            if (r == -1) return 0;
#if CSHARPC_TARGET_MACOS
            byte* name = st + 72;               // f_fstypename[16]
            return NameIs(name, "nfs") || NameIs(name, "cifs") || NameIs(name, "smb") || NameIs(name, "smb2") ? 0 : 1;
#else
            uint type = (uint)*(long*)st;       // f_type (__fsword_t, the first field)
            return type == 0x6969 || type == 0xFF534D42 || type == 0x517B || type == 0xFE534D42 ? 0 : 1;   // NFS, CIFS, SMB, SMB2
#endif
#endif
        }

#if CSHARPC_TARGET_MACOS
        private static bool NameIs(byte* name, string expected)
        {
            for (int i = 0; i < expected.Length; i++)
                if (name[i] != (byte)expected[i]) return false;
            return name[expected.Length] == 0;
        }
#endif

        [RuntimeExport("SystemNative_FChMod", Trigger + "SystemNative_FChMod")]
        public static int FChMod(nint fd, int mode)
        {
#if CSHARPC_TARGET_WASI
            return EINTR;   // pal_io.c without fchmod (WASI has no permission bits): a non-negative result
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
            return EINTR;   // pal_io.c without chmod (WASI has no permission bits): a non-negative result
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
            // TimeSpec { long sec; long nsec; } is struct timespec on LP64 and, little-endian with nanoseconds below 2^31, on wasm32 too.
            int r;
            while ((r = futimens(Fd(fd), times)) < 0 && Errno == EINTR) { }
            return r;
        }

        /// <summary>SystemNative_UTimensat: a path's times, the link itself for a symbolic link (AT_SYMLINK_NOFOLLOW, as pal_io.c).</summary>
        [RuntimeExport("SystemNative_UTimensat", Trigger + "SystemNative_UTimensat")]
        public static int UTimensat(byte* path, long* times)
        {
            int r;
            while ((r = utimensat(AT_FDCWD, path, times, AT_SYMLINK_NOFOLLOW)) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_FcntlGetIsNonBlocking", Trigger + "SystemNative_FcntlGetIsNonBlocking")]
        public static int GetIsNonBlocking(nint fd, int* isNonBlocking)
        {
            if (isNonBlocking == null) return 0x10015; // Error_EFAULT
            int flags = fcntl_int(Fd(fd), F_GETFL, 0);
            if (flags < 0) { *isNonBlocking = 0; return -1; }
            *isNonBlocking = (flags & O_NONBLOCK) != 0 ? 1 : 0;
            return 0;
        }

        /// <summary>
        /// SystemNative_Pipe: both ends close-on-exec when PAL_O_CLOEXEC (0x10) is asked, atomically where the target can (pipe2); then
        /// O_NONBLOCK on the read end (PAL_O_NONBLOCK_READ 0x400) and the write end (PAL_O_NONBLOCK_WRITE 0x800), as pal_io.c. On failure
        /// after the pipe exists both ends are closed and errno is the failing call's.
        /// </summary>
        [RuntimeExport("SystemNative_Pipe", Trigger + "SystemNative_Pipe")]
        public static int Pipe(int* fds, int flags)
        {
#if CSHARPC_TARGET_WASI
            return Fail(ENOTSUP);
#else
            if ((flags & ~(0x10 | 0x400 | 0x800)) != 0) return Fail(EINVAL);
            int r;
#if CSHARPC_TARGET_MACOS
            while ((r = pipe(fds)) < 0 && Errno == EINTR) { }
            if (r == 0 && (flags & 0x10) != 0)
            {
                // FIOCLEX: no variadic argument, unlike fcntl(F_SETFD, FD_CLOEXEC).
                if (ioctl(fds[0], FIOCLEX) < 0 || ioctl(fds[1], FIOCLEX) < 0)
                    return CloseBoth(fds);
            }
#else
            while ((r = pipe2(fds, (flags & 0x10) != 0 ? O_CLOEXEC : 0)) < 0 && Errno == EINTR) { }
#endif
            if (r == 0 && (flags & (0x400 | 0x800)) != 0)
            {
                if ((flags & 0x400) != 0 && SetNonBlocking(fds[0]) != 0) return CloseBoth(fds);
                if ((flags & 0x800) != 0 && SetNonBlocking(fds[1]) != 0) return CloseBoth(fds);
            }
            return r;
#endif
        }

#if !CSHARPC_TARGET_WASI
        private static int SetNonBlocking(int fd)
        {
            int flags = fcntl_int(fd, F_GETFL, 0);
            return flags == -1 ? -1 : fcntl_int(fd, F_SETFL, flags | O_NONBLOCK);
        }

        private static int CloseBoth(int* fds)
        {
            int e = Errno;
            close(fds[0]);
            close(fds[1]);
            return Fail(e);
        }
#endif

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
            int r;
            while ((r = link(source, linkPath)) < 0 && Errno == EINTR) { }
            return r;
        }

        [RuntimeExport("SystemNative_SymLink", Trigger + "SystemNative_SymLink")]
        public static int SymLink(byte* target, byte* linkPath)
        {
            int r;
            while ((r = symlink(target, linkPath)) < 0 && Errno == EINTR) { }
            return r;
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
        /// <summary>SystemNative_OpenDir: opendir(3), retried on EINTR (undocumented, but macOS returns it), as pal_io.c.</summary>
        [RuntimeExport("SystemNative_OpenDir", Trigger + "SystemNative_OpenDir")]
        public static nint OpenDir(byte* path)
        {
            void* d;
            while ((d = opendir(path)) == null && Errno == EINTR) { }
            return (nint)d;
        }

        /// <summary>SystemNative_CloseDir: closedir(3); EINTR is success (the stream is released), as pal_io.c.</summary>
        [RuntimeExport("SystemNative_CloseDir", Trigger + "SystemNative_CloseDir")]
        public static int CloseDir(nint dir)
        {
            int r = closedir((void*)dir);
            return r < 0 && Errno == EINTR ? 0 : r;
        }

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

#if CSHARPC_TARGET_LINUX
        [DllImport("libc")] private static extern int getpwuid_r(uint uid, void* pwd, byte* buf, nuint buflen, void** result);

        // pal_uid.c: the entry's strings live in the caller's buffer, bounded by its length; 0 on success, -1 when there is no such user,
        // the errno value otherwise (the framework's X509 store reads the home directory of the effective user this way).
        [RuntimeExport("SystemNative_GetPwUidR", Trigger + "SystemNative_GetPwUidR")]
        public static int GetPwUidR(uint uid, void* pwd, byte* buf, int buflen)
        {
            if (buflen < 0) return 22; // EINVAL
            // struct passwd (glibc LP64): pw_name, pw_passwd, pw_uid, pw_gid, pw_gecos, pw_dir, pw_shell.
            byte* native = stackalloc byte[48];
            void* result;
            int error;
            while ((error = getpwuid_r(uid, native, buf, (nuint)buflen, &result)) == 4 /* EINTR */) { }
            // Passwd (pal_uid.h): Name, Password, UserId, GroupId, UserInfo, HomeDirectory, Shell (the same layout).
            if (error != 0 || result == null)
            {
                new Span<byte>(pwd, 48).Clear();
                return error != 0 ? error : -1;
            }
            Buffer.MemoryCopy(native, pwd, 48, 48);
            return 0;
        }
#endif
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
        /// SystemNative_CopyFile (File.Copy; both descriptors open, the destination new or truncated by the framework), as pal_io.c:
        /// macOS: fcopyfile(COPYFILE_ALL), data and metadata in the kernel.
        /// Linux: a copy-on-write clone (FICLONE), else copy_file_range (kernel 5.3+), else sendfile, else read/write; then the source's
        /// access and modification times and its permission bits (rwx for user, group and other only: never setuid, setgid or sticky),
        /// a failure to copy either tolerated only as EPERM (a file system without POSIX ownership).
        /// WASI: read/write and the times (no permission bits there).
        /// </summary>
        [RuntimeExport("SystemNative_CopyFile", Trigger + "SystemNative_CopyFile")]
        public static int CopyFile(nint source, nint destination, long sourceLength)
        {
            int inFd = Fd(source), outFd = Fd(destination);
#if CSHARPC_TARGET_MACOS
            return fcopyfile(inFd, outFd, null, 15 /* COPYFILE_ALL */) == 0 ? 0 : -1;
#else
            int r;
            bool copied = false;
#if !CSHARPC_TARGET_WASI
            bool trySendFile = true;
            if (sourceLength != 0)
            {
                while ((r = ioctl(outFd, FICLONE, inFd)) < 0 && Errno == EINTR) { }
                copied = r == 0;
            }
            if (!copied && sourceLength != 0 && SupportsCopyFileRange())
            {
                do
                {
                    nint sent = copy_file_range(inFd, null, outFd, null, (nuint)sourceLength, 0);
                    if (sent <= 0) { trySendFile = false; break; }
                    sourceLength -= sent;
                } while (sourceLength > 0);
                copied = sourceLength == 0;
            }
            if (trySendFile && !copied && sourceLength != 0)
            {
                do
                {
                    nint sent = sendfile(outFd, inFd, null, (nuint)sourceLength);
                    if (sent < 0)
                    {
                        int e = Errno;
                        if (e != EINVAL && e != ENOSYS) return -1;
                        break;
                    }
                    if (sent == 0) break;   // truncated meanwhile: the rest by read/write
                    sourceLength -= sent;
                } while (sourceLength > 0);
                copied = sourceLength == 0;
            }
#endif
            if (!copied && CopyReadWrite(inFd, outFd) != 0) return -1;

            byte* status = stackalloc byte[FileStatusSize];
            r = FStat(source, status);
            if (r == 0)
            {
                long* times = stackalloc long[4];
                times[0] = *(long*)(status + 24); times[1] = *(long*)(status + 32);
                times[2] = *(long*)(status + 40); times[3] = *(long*)(status + 48);
                while ((r = futimens(outFd, times)) < 0 && Errno == EINTR) { }
            }
            if (r != 0 && Errno != EPERM) return -1;
#if !CSHARPC_TARGET_WASI
            // The mode the framework created the file with went through the umask; copy the source's rwx bits explicitly.
            while ((r = fchmod(outFd, *(int*)(status + 4) & 0x1FF)) < 0 && Errno == EINTR) { }
            if (r != 0 && Errno != EPERM) return -1;
#endif
            return 0;
#endif
        }

#if !CSHARPC_TARGET_MACOS
        private static int CopyReadWrite(int inFd, int outFd)
        {
            const int BufferLength = 80 * 1024;
            byte* buffer = (byte*)NativeMemory.Alloc(BufferLength);
            try
            {
                while (true)
                {
                    nint n;
                    while ((n = read(inFd, buffer, BufferLength)) < 0 && Errno == EINTR) { }
                    if (n < 0) return -1;
                    if (n == 0) return 0;
                    for (nint written = 0; written < n;)
                    {
                        nint w;
                        while ((w = write(outFd, buffer + written, (nuint)(n - written))) < 0 && Errno == EINTR) { }
                        if (w < 0) return -1;
                        written += w;
                    }
                }
            }
            finally
            {
                int e = Errno;
                NativeMemory.Free(buffer);
                Marshal.SetLastSystemError(e);
            }
        }
#endif

#if !CSHARPC_TARGET_MACOS && !CSHARPC_TARGET_WASI
        private static int s_copyFileRange; // 0 unknown, 1 supported, -1 not

        /// <summary>copy_file_range only on Linux 5.3+ (earlier kernels have known problems with it, https://lwn.net/Articles/789527/), as pal_io.c.</summary>
        private static bool SupportsCopyFileRange()
        {
            int supported = s_copyFileRange;
            if (supported == 0)
            {
                supported = -1;
                byte* name = stackalloc byte[390];  // struct utsname: six 65-byte fields; release @130
                if (uname(name) == 0)
                {
                    byte* p = name + 130;
                    uint major = 0, minor = 0;
                    while (*p >= (byte)'0' && *p <= (byte)'9') major = major * 10 + (uint)(*p++ - '0');
                    if (*p == (byte)'.')
                    {
                        p++;
                        while (*p >= (byte)'0' && *p <= (byte)'9') minor = minor * 10 + (uint)(*p++ - '0');
                    }
                    if (major > 5 || (major == 5 && minor >= 3))
                        supported = copy_file_range(-1, null, -1, null, 0, 0) == -1 && Errno != ENOSYS ? 1 : -1;
                }
                s_copyFileRange = supported;
            }
            return supported == 1;
        }
#endif

        // ---- mount points ----------------------------------------------------------------------------------------
        /// <summary>
        /// SystemNative_GetAllMountPoints: calls onFound(context, mountPoint) for each mounted file system (Directory.GetLogicalDrives,
        /// DriveInfo.GetDrives), as pal_mount.c: Linux reads /proc/mounts with glibc's setmntent/getmntent_r (its escape handling and
        /// O_CLOEXEC stream), macOS lists them with getfsstat (thread-safe, unlike getmntinfo); WASI has only "/".
        /// </summary>
        [RuntimeExport("SystemNative_GetAllMountPoints", Trigger + "SystemNative_GetAllMountPoints")]
        public static int GetAllMountPoints(delegate* unmanaged<void*, byte*, void> onFound, void* context)
        {
#if CSHARPC_TARGET_WASI
            byte* root = stackalloc byte[2];
            root[0] = (byte)'/'; root[1] = 0;
            onFound(context, root);
            return 0;
#elif CSHARPC_TARGET_MACOS
            const int StatfsSize = 2168, MntOnNameOffset = 88, MNT_NOWAIT = 2;
            byte* mounts = null;
            int count, capacity = 0;
            while (true)
            {
                count = getfsstat(null, 0, MNT_NOWAIT);
                if (count < 0) { NativeMemory.Free(mounts); return -1; }
                if (count >= capacity)
                {
                    NativeMemory.Free(mounts);
                    capacity = count + 1;   // one extra, to see mounts added between the two calls
                    if ((long)capacity * StatfsSize > int.MaxValue) { mounts = null; return Fail(12 /* ENOMEM */); }
                    mounts = (byte*)NativeMemory.Alloc((nuint)(capacity * StatfsSize));
                }
                if (count == 0) break;
                count = getfsstat(mounts, capacity * StatfsSize, MNT_NOWAIT);
                if (count < 0) { NativeMemory.Free(mounts); return -1; }
                if (count < capacity) break;
            }
            for (int i = 0; i < count; i++) onFound(context, mounts + (long)i * StatfsSize + MntOnNameOffset);
            NativeMemory.Free(mounts);
            return 0;
#else
            byte* path = stackalloc byte[] { (byte)'/', (byte)'p', (byte)'r', (byte)'o', (byte)'c', (byte)'/', (byte)'m', (byte)'o', (byte)'u', (byte)'n', (byte)'t', (byte)'s', 0 };
            byte* mode = stackalloc byte[] { (byte)'r', 0 };
            void* stream = setmntent(path, mode);
            if (stream == null) return -1;
            const int BufferSize = 8192;    // pal_mount.c's STRING_BUFFER_SIZE
            byte* entry = stackalloc byte[48];  // struct mntent: mnt_fsname, mnt_dir @8, mnt_type, mnt_opts, mnt_freq, mnt_passno
            byte* buffer = (byte*)NativeMemory.Alloc(BufferSize);
            while (getmntent_r(stream, entry, buffer, BufferSize) != null)
                onFound(context, *(byte**)(entry + 8));
            NativeMemory.Free(buffer);
            endmntent(stream);
            return 0;
#endif
        }

        /// <summary>
        /// SystemNative_GetSpaceInfoForMountPoint (DriveInfo's sizes, #102): pal_mount.c's block size times the free, available and total
        /// block counts, from statvfs on Linux and statfs on macOS; zeros and -1 (errno set) when the call fails. A MountPointInformation
        /// is three uint64s: AvailableFreeSpace, TotalFreeSpace, TotalSize.
        /// </summary>
        [RuntimeExport("SystemNative_GetSpaceInfoForMountPoint", Trigger + "SystemNative_GetSpaceInfoForMountPoint")]
        public static int GetSpaceInfoForMountPoint(byte* name, ulong* info)
        {
            info[0] = info[1] = info[2] = 0;
#if CSHARPC_TARGET_WASI
            return Fail(58 /* ENOTSUP */);
#else
            byte* st = stackalloc byte[2168];   // struct statvfs (Linux, 112 bytes) / struct statfs (macOS, 2168)
#if CSHARPC_TARGET_MACOS
            if (statfs(name, st) != 0) return -1;
            ulong bsize = *(uint*)st, blocks = *(ulong*)(st + 8), free = *(ulong*)(st + 16), available = *(ulong*)(st + 24);
#else
            if (statvfs(name, st) != 0) return -1;
            ulong bsize = *(ulong*)st, blocks = *(ulong*)(st + 16), free = *(ulong*)(st + 24), available = *(ulong*)(st + 32);
#endif
            info[0] = bsize * available;
            info[1] = bsize * free;
            info[2] = bsize * blocks;
            return 0;
#endif
        }

        /// <summary>
        /// SystemNative_GetFileSystemTypeNameForMountPoint (DriveInfo.DriveFormat and DriveType, #102), as pal_mount.c: macOS gives the
        /// file system's name (statfs f_fstypename; ERANGE when the buffer is shorter than MFSTYPENAMELEN) and type -1, Linux its magic
        /// (statfs f_type), which the framework names, and an empty name. On failure the name is empty and the type -1.
        /// </summary>
        [RuntimeExport("SystemNative_GetFileSystemTypeNameForMountPoint", Trigger + "SystemNative_GetFileSystemTypeNameForMountPoint")]
        public static int GetFileSystemTypeNameForMountPoint(byte* name, byte* formatName, int bufferLength, long* formatType)
        {
            int result;
#if CSHARPC_TARGET_WASI
            result = Fail(58 /* ENOTSUP */);
#else
            byte* st = stackalloc byte[2168];   // struct statfs: 120 bytes on Linux, 2168 on macOS
            result = statfs(name, st);
            if (result == 0)
            {
#if CSHARPC_TARGET_MACOS
                if (bufferLength < 16 + 1) result = Fail(34 /* ERANGE */);
                CopyCString(st + 72, formatName, bufferLength);   // f_fstypename[16]
                *formatType = -1;
#else
                *formatType = *(long*)st;       // f_type (__fsword_t, the first field)
                CopyCString(null, formatName, bufferLength);
#endif
            }
#endif
            if (result != 0)
            {
                CopyCString(null, formatName, bufferLength);
                *formatType = -1;
            }
            return result;
        }

        /// <summary>pal_utilities.h's SafeStringCopy: at most size - 1 bytes of source (null: none) and the terminator.</summary>
        private static void CopyCString(byte* source, byte* destination, int size)
        {
            if (size <= 0) return;
            int i = 0;
            if (source != null)
                for (; i < size - 1 && source[i] != 0; i++) destination[i] = source[i];
            destination[i] = 0;
        }

        /// <summary>SystemNative_RealPath (#102): realpath(path, NULL), a malloc'd canonical path the framework frees, or null with errno set.</summary>
        [RuntimeExport("SystemNative_RealPath", Trigger + "SystemNative_RealPath")]
        public static byte* RealPath(byte* path)
        {
#if CSHARPC_TARGET_WASI
            Fail(58 /* ENOTSUP */);
            return null;
#else
            return realpath(path, null);
#endif
        }

        // ---- errno <-> Interop.Error (pal_errno.c; PAL values are the framework's Interop.Error) --------------------
        private static int ToPal(int platform) => PalTables.ToPal(platform);
        private static int ToPlatform(int pal) => PalTables.FromPal(pal);
    }
}
#endif
