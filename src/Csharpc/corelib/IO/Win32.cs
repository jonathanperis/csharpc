// Windows: the POSIX layer System.IO is written against (see Posix.cs), on top of kernel32 and the
// UCRT. Files are opened with CreateFileW (exact Win32 errors, FileShare sharing modes) and then read
// and written through UCRT file descriptors; everything else is kernel32. Win32 errors are mapped to
// errno values for the shared code paths in File.cs / Streams.cs, and the exceptions and messages
// are .NET's Windows ones (Win32Marshal.GetExceptionForWin32Error).
#if CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class Posix
    {
        // UCRT flag and errno values.
        public const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
        public const int O_APPEND = 0x8, O_CREAT = 0x100, O_TRUNC = 0x200, O_EXCL = 0x400, O_CLOEXEC = 0x80 /* _O_NOINHERIT */;
        private const int O_BINARY = 0x8000;
        public const int ENOTEMPTY = 41;
        public const int SEEK_SET = 0, SEEK_CUR = 1, SEEK_END = 2;
        public const int S_IFMT = 0xF000, S_IFDIR = 0x4000, S_IFREG = 0x8000, S_IFLNK = 0xA000;
        public const int ENOENT = 2, EPERM = 1, EACCES = 13, EEXIST = 17, ENOTDIR = 20, EISDIR = 21, EINTR = 4, ERANGE = 34;
        /// <summary>Win32 errors without an errno counterpart travel as Win32Base + the error code.</summary>
        private const int Win32Base = 0x10000;
        private const int ERROR_FILE_NOT_FOUND = 2, ERROR_PATH_NOT_FOUND = 3, ERROR_ACCESS_DENIED = 5, ERROR_INVALID_DRIVE = 15,
            ERROR_SHARING_VIOLATION = 32, ERROR_LOCK_VIOLATION = 33, ERROR_FILE_EXISTS = 80, ERROR_INVALID_PARAMETER = 87,
            ERROR_INSUFFICIENT_BUFFER = 122, ERROR_INVALID_NAME = 123, ERROR_DIR_NOT_EMPTY = 145, ERROR_ALREADY_EXISTS = 183,
            ERROR_FILENAME_EXCED_RANGE = 206, ERROR_DIRECTORY = 267, ERROR_NO_MORE_FILES = 18;

        /// <summary>Directory.Delete of a non-empty directory (.NET: the system message + " : 'path'.").</summary>
        public static Exception DirectoryNotEmpty(string full) => new IOException("The directory is not empty. : '" + full + "'.");

        [DllImport("libc")] private static extern int _read(int fd, byte* buffer, uint count);
        [DllImport("libc")] private static extern int _write(int fd, byte* buffer, uint count);
        [DllImport("libc")] private static extern int _close(int fd);
        [DllImport("libc")] private static extern long _lseeki64(int fd, long offset, int whence);
        [DllImport("libc")] private static extern int _chsize_s(int fd, long size);
        [DllImport("libc")] private static extern int _commit(int fd);
        [DllImport("libc")] private static extern int _open_osfhandle(nint handle, int flags);
        [DllImport("libc")] private static extern nint _get_osfhandle(int fd);
        [DllImport("libc")] private static extern int _get_errno(int* value);
        [DllImport("libc")] private static extern int _set_errno(int value);
        [DllImport("libc")] private static extern int rand_s(uint* value);
        [DllImport("libc")] private static extern byte* strerror(int error);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateFileW(string name, uint access, uint share, void* security, uint disposition, uint flags, nint template);
        [DllImport("kernel32", SetLastError = true)] private static extern int CloseHandle(nint handle);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int DeleteFileW(string name);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int CreateDirectoryW(string name, void* security);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int RemoveDirectoryW(string name);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int MoveFileExW(string from, string to, uint flags);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int SetCurrentDirectoryW(string name);
        [DllImport("kernel32", SetLastError = true)] private static extern uint GetCurrentDirectoryW(uint length, char* buffer);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetFileAttributesExW(string name, int level, byte* data);
        [DllImport("kernel32", SetLastError = true)] private static extern int GetFileInformationByHandle(nint handle, byte* info);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint FindFirstFileW(string pattern, byte* data);
        [DllImport("kernel32", SetLastError = true)] private static extern int FindNextFileW(nint handle, byte* data);
        [DllImport("kernel32")] private static extern int FindClose(nint handle);
        [DllImport("kernel32")] private static extern uint FormatMessageW(uint flags, void* source, uint id, uint language, char* buffer, uint size, void* args);
        [DllImport("kernel32")] private static extern void GetSystemTimeAsFileTime(long* time);
        [DllImport("kernel32")] private static extern int FileTimeToSystemTime(long* time, ushort* system);
        [DllImport("kernel32")] private static extern int SystemTimeToFileTime(ushort* system, long* time);
        [DllImport("kernel32")] private static extern uint GetDynamicTimeZoneInformation(byte* info);
        [DllImport("kernel32")] private static extern int SystemTimeToTzSpecificLocalTimeEx(byte* info, ushort* utc, ushort* local);

        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const uint CREATE_NEW = 1, CREATE_ALWAYS = 2, OPEN_EXISTING = 3, OPEN_ALWAYS = 4, TRUNCATE_EXISTING = 5;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_REPARSE_POINT = 0x400, FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint MOVEFILE_REPLACE_EXISTING = 1, MOVEFILE_COPY_ALLOWED = 2;
        private static readonly nint InvalidHandle = -1;

        private static string Str(byte* utf8) => Utf8.FromNative(utf8);

        /// <summary>Records a failed Win32 call's error as errno; returns -1.</summary>
        private static int Fail()
        {
            _set_errno(FromWin32(Marshal.GetLastPInvokeError()));
            return -1;
        }

        private static int FromWin32(int error) => error switch
        {
            ERROR_FILE_NOT_FOUND => ENOENT,
            // .NET: ERROR_PATH_NOT_FOUND is a DirectoryNotFoundException (IOError maps ENOTDIR to it).
            ERROR_PATH_NOT_FOUND or ERROR_INVALID_DRIVE or ERROR_DIRECTORY => ENOTDIR,
            ERROR_ACCESS_DENIED => EACCES,
            ERROR_FILE_EXISTS or ERROR_ALREADY_EXISTS => EEXIST,
            ERROR_DIR_NOT_EMPTY => ENOTEMPTY,
            ERROR_INSUFFICIENT_BUFFER => ERANGE,
            _ => Win32Base + error,
        };

        // ---- file descriptors ------------------------------------------------------------------

        public static int open(byte* path, int flags, int mode) => open(path, flags, mode, (int)FileShare.Read);

        /// <summary>CreateFileW with .NET's mapping of FileMode/FileAccess/FileShare, as a UCRT descriptor.</summary>
        public static int open(byte* path, int flags, int mode, int share)
        {
            int accessMode = flags & 3;
            uint access = accessMode == O_RDONLY ? GENERIC_READ : accessMode == O_WRONLY ? GENERIC_WRITE : GENERIC_READ | GENERIC_WRITE;
            uint disposition = (flags & (O_CREAT | O_EXCL | O_TRUNC)) switch
            {
                O_CREAT | O_EXCL or O_CREAT | O_EXCL | O_TRUNC => CREATE_NEW,
                O_CREAT | O_TRUNC => CREATE_ALWAYS,
                O_CREAT => OPEN_ALWAYS,
                O_TRUNC => TRUNCATE_EXISTING,
                _ => OPEN_EXISTING,
            };
            // FileShare's Read / Write / Delete bits are FILE_SHARE_READ / _WRITE / _DELETE.
            nint handle = CreateFileW(Str(path), access, (uint)share & 7, null, disposition, FILE_ATTRIBUTE_NORMAL, 0);
            if (handle == InvalidHandle) return Fail();
            int fd = _open_osfhandle(handle, accessMode | O_BINARY | O_CLOEXEC);
            if (fd < 0) { CloseHandle(handle); _set_errno(Win32Base + 4 /* ERROR_TOO_MANY_OPEN_FILES */); }
            return fd;
        }

        public static nint read(int fd, byte* buffer, nuint count) => _read(fd, buffer, count > int.MaxValue ? int.MaxValue : (uint)count);
        public static nint write(int fd, byte* buffer, nuint count) => _write(fd, buffer, count > int.MaxValue ? int.MaxValue : (uint)count);
        public static int close(int fd) => _close(fd);
        public static long lseek(int fd, long offset, int whence) => _lseeki64(fd, offset, whence);
        public static int ftruncate(int fd, long length)
        {
            int error = _chsize_s(fd, length);
            if (error == 0) return 0;
            _set_errno(error);
            return -1;
        }
        public static int fsync(int fd) => _commit(fd);

        // ---- paths -----------------------------------------------------------------------------

        public static int mkdir(byte* path, int mode) => CreateDirectoryW(Str(path), null) != 0 ? 0 : Fail();
        public static int rmdir(byte* path) => RemoveDirectoryW(Str(path)) != 0 ? 0 : Fail();
        public static int unlink(byte* path) => DeleteFileW(Str(path)) != 0 ? 0 : Fail();
        public static int rename(byte* from, byte* to) =>
            MoveFileExW(Str(from), Str(to), MOVEFILE_REPLACE_EXISTING | MOVEFILE_COPY_ALLOWED) != 0 ? 0 : Fail();
        public static int chdir(byte* path) => SetCurrentDirectoryW(Str(path)) != 0 ? 0 : Fail();

        public static byte* getcwd(byte* buffer, nuint size)
        {
            uint needed = GetCurrentDirectoryW(0, null);
            if (needed == 0) { Fail(); return null; }
            char* chars = stackalloc char[(int)needed];
            uint length = GetCurrentDirectoryW(needed, chars);
            if (length == 0 || length >= needed) { Fail(); return null; }
            string cwd = new string(chars, 0, (int)length);
            if ((nuint)Utf8.GetByteCount(cwd) + 1 > size) { _set_errno(ERANGE); return null; }
            buffer[Utf8.Encode(cwd, buffer)] = 0;
            return buffer;
        }

        // ---- stat ------------------------------------------------------------------------------
        // csharpc's own layout: mode @0 (int), size @8, mtime seconds @16, nanoseconds @24.
        public const int StatSize = 32;
        public static int Mode(byte* st) => *(int*)st;
        public static long Size(byte* st) => *(long*)(st + 8);
        public static long MTimeSeconds(byte* st) => *(long*)(st + 16);
        public static long MTimeNanoseconds(byte* st) => *(long*)(st + 24);

        private const long FileTimeUnixEpoch = 116444736000000000L;

        private static void Fill(byte* st, uint attributes, long size, long writeTime, bool followLinks)
        {
            *(int*)st = (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 && !followLinks ? S_IFLNK
                : (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ? S_IFDIR : S_IFREG;
            *(long*)(st + 8) = size;
            long unix = writeTime - FileTimeUnixEpoch;
            long seconds = unix / 10000000, rest = unix % 10000000;
            if (rest < 0) { seconds--; rest += 10000000; }
            *(long*)(st + 16) = seconds;
            *(long*)(st + 24) = rest * 100;
        }

        private static int StatPath(byte* path, byte* st, bool followLinks)
        {
            // WIN32_FILE_ATTRIBUTE_DATA: attributes @0, last write time @20, size high @28 / low @32.
            byte* data = stackalloc byte[40];
            if (GetFileAttributesExW(Str(path), 0 /* GetFileExInfoStandard */, data) == 0) return Fail();
            Fill(st, *(uint*)data, ((long)*(uint*)(data + 28) << 32) | *(uint*)(data + 32), *(long*)(data + 20), followLinks);
            return 0;
        }

        public static int stat(byte* path, byte* buffer) => StatPath(path, buffer, true);
        public static int lstat(byte* path, byte* buffer) => StatPath(path, buffer, false);

        public static int fstat(int fd, byte* buffer)
        {
            // BY_HANDLE_FILE_INFORMATION: attributes @0, last write time @20, size high @32 / low @36.
            byte* info = stackalloc byte[56];
            if (GetFileInformationByHandle(_get_osfhandle(fd), info) == 0) return Fail();
            Fill(buffer, *(uint*)info, ((long)*(uint*)(info + 32) << 32) | *(uint*)(info + 36), *(long*)(info + 20), true);
            return 0;
        }

        // ---- directories -----------------------------------------------------------------------
        // opendir returns a block: HANDLE @0, "first entry pending" @8, WIN32_FIND_DATAW @16 (592 bytes),
        // then the dirent readdir returns @608: type byte, NUL-terminated UTF-8 name.
        public const int DirentTypeOffset = 0, DirentNameOffset = 1;
        public const byte DT_UNKNOWN = 0, DT_DIR = 4, DT_REG = 8, DT_LNK = 10;
        private const int FindDataOffset = 16, DirentOffset = 608, DirentSize = 1 + 260 * 3 + 1;

        public static void* opendir(byte* path)
        {
            string dir = Str(path);
            string pattern = dir.Length > 0 && (dir[dir.Length - 1] == '\\' || dir[dir.Length - 1] == '/') ? dir + "*" : dir + "\\*";
            byte* block = (byte*)Libc.Alloc((nuint)(DirentOffset + DirentSize));
            nint handle = FindFirstFileW(pattern, block + FindDataOffset);
            if (handle == InvalidHandle)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error != ERROR_FILE_NOT_FOUND) { Libc.free(block); _set_errno(FromWin32(error)); return null; }
            }
            *(nint*)block = handle;
            *(int*)(block + 8) = handle == InvalidHandle ? 0 : 1;
            return block;
        }

        public static byte* readdir(void* dir)
        {
            byte* block = (byte*)dir;
            nint handle = *(nint*)block;
            byte* data = block + FindDataOffset;
            if (handle == InvalidHandle) return null;
            if (*(int*)(block + 8) != 0) *(int*)(block + 8) = 0;
            else if (FindNextFileW(handle, data) == 0) return null;
            uint attributes = *(uint*)data;
            byte* entry = block + DirentOffset;
            entry[DirentTypeOffset] = (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ? DT_LNK
                : (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ? DT_DIR : DT_REG;
            string name = Utf16.FromNative((char*)(data + 44));  // cFileName
            byte* nameBytes = entry + DirentNameOffset;
            nameBytes[Utf8.Encode(name, nameBytes)] = 0;
            return entry;
        }

        public static int closedir(void* dir)
        {
            nint handle = *(nint*)dir;
            if (handle != InvalidHandle) FindClose(handle);
            Libc.free(dir);
            return 0;
        }

        // ---- misc ------------------------------------------------------------------------------

        public static int getentropy(byte* buffer, nuint length)
        {
            for (nuint i = 0; i < length; i++)
            {
                uint value;
                rand_s(&value);
                buffer[i] = (byte)value;
            }
            return 0;
        }

        public static long time(long* result)
        {
            long now;
            GetSystemTimeAsFileTime(&now);
            long seconds = (now - FileTimeUnixEpoch) / 10000000;
            if (result != null) *result = seconds;
            return seconds;
        }

        /// <summary>
        /// The local zone's offset at a UTC instant, written like glibc's struct tm (tm_isdst @32,
        /// tm_gmtoff @40). Windows' zone (with its yearly rules), not TZ, which .NET ignores there too.
        /// </summary>
        public static byte* localtime_r(long* time, byte* tm)
        {
            long fileTime = *time * 10000000 + FileTimeUnixEpoch;
            ushort* utc = stackalloc ushort[8];
            ushort* local = stackalloc ushort[8];
            byte* zone = stackalloc byte[432];  // DYNAMIC_TIME_ZONE_INFORMATION
            if (fileTime < 0 || FileTimeToSystemTime(&fileTime, utc) == 0) return null;
            if (GetDynamicTimeZoneInformation(zone) == 0xFFFFFFFF) return null;
            if (SystemTimeToTzSpecificLocalTimeEx(zone, utc, local) == 0) return null;
            long localTime;
            if (SystemTimeToFileTime(local, &localTime) == 0) return null;
            long offset = (localTime - fileTime) / 10000000;
            int bias = *(int*)zone, standardBias = *(int*)(zone + 84);  // Bias, StandardBias (minutes)
            *(int*)(tm + 32) = offset != -(long)(bias + standardBias) * 60 ? 1 : 0;
            *(long*)(tm + 40) = offset;
            return tm;
        }

        // ---- errors ----------------------------------------------------------------------------

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

        // ---- read-only file views (System.IO.MemoryMappedFiles) --------------------------------
        public const bool CanMapFiles = true;
        [DllImport("kernel32", SetLastError = true)] private static extern nint CreateFileMappingW(nint file, void* security, uint protect, uint sizeHigh, uint sizeLow, char* name);
        [DllImport("kernel32", SetLastError = true)] private static extern void* MapViewOfFile(nint mapping, uint access, uint offsetHigh, uint offsetLow, nuint length);
        [DllImport("kernel32", SetLastError = true)] private static extern int UnmapViewOfFile(void* address);

        /// <summary>A read-only view of [offset, offset + length) of a file (offset aligned to 64 KiB by the caller); null with errno set on failure.</summary>
        public static byte* MapView(int fd, long offset, long length)
        {
            nint mapping = CreateFileMappingW(_get_osfhandle(fd), null, 0x02 /* PAGE_READONLY */, 0, 0, null);
            if (mapping == 0) { _set_errno(Win32Base + Marshal.GetLastPInvokeError()); return null; }
            void* view = MapViewOfFile(mapping, 0x0004 /* FILE_MAP_READ */, (uint)(offset >> 32), (uint)offset, (nuint)length);
            int error = Marshal.GetLastPInvokeError();
            CloseHandle(mapping); // the view keeps the mapping alive
            if (view == null) { _set_errno(Win32Base + error); return null; }
            return (byte*)view;
        }

        public static void UnmapView(byte* view, long length) => UnmapViewOfFile(view);

        public static int Errno { get { int e; _get_errno(&e); return e; } }

        public static string ErrorMessage(int errno)
        {
            if (errno < Win32Base) return Utf8.FromNative(strerror(errno));
            // FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS; .NET trims the trailing newline.
            char* buffer = stackalloc char[512];
            uint n = FormatMessageW(0x1000 | 0x200, null, (uint)(errno - Win32Base), 0, buffer, 512, null);
            while (n > 0 && (buffer[n - 1] == '\n' || buffer[n - 1] == '\r' || buffer[n - 1] == ' ')) n--;
            return n == 0 ? "Unknown error (0x" + (errno - Win32Base).ToString("x") + ")" : new string(buffer, 0, (int)n);
        }

        /// <summary>The exception .NET throws for a Win32 error on a path (Win32Marshal).</summary>
        public static Exception IOError(int errno, string path, bool isDirectory)
        {
            string full = path == null ? null : PathImpl.GetFullPath(path);
            switch (errno)
            {
                case ENOENT:
                    if (!isDirectory) return new FileNotFoundException("Could not find file '" + full + "'.", full);
                    return new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.");
                case ENOTDIR:
                    return new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.");
                case EACCES:
                case EPERM:
                case EISDIR:
                    return new UnauthorizedAccessException("Access to the path '" + full + "' is denied.");
                case EEXIST:
                    return new IOException("The file '" + full + "' already exists.");
                case Win32Base + ERROR_SHARING_VIOLATION:
                case Win32Base + ERROR_LOCK_VIOLATION:
                    return new IOException("The process cannot access the file '" + full + "' because it is being used by another process.");
                case Win32Base + ERROR_FILENAME_EXCED_RANGE:
                    return new PathTooLongException("The path '" + full + "' is too long, or a component of the specified path is too long.");
                default:
                    return new IOException(full == null ? ErrorMessage(errno) : ErrorMessage(errno) + " : '" + full + "'.");
            }
        }

        public static void ThrowIfError(int result, string path, bool isDirectory)
        {
            if (result < 0) throw IOError(Errno, path, isDirectory);
        }
    }
}
#endif
