// FileSystemInfo.Attributes and LinkTarget/ResolveLinkTarget as .NET computes them
// (dotnet/runtime release/11.0: FileStatus.Unix.cs, FileSystem.Unix.cs; on Windows the attributes are
// GetFileAttributesW's, as FileSystemInfo.Windows.cs reports them).
//
// Unix attributes: ReadOnly when the permission bits deny the caller write access the way .NET decides
// it (owner/group/other bits, the effective uid, group membership), Hidden for names starting with '.'
// (and UF_HIDDEN on macOS), ReparsePoint for symbolic links, Directory when the entry, or a link's
// target, is a directory; Normal when none applies, -1 for an entry that doesn't exist.
#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class FileStatusImpl
    {
#if CSHARPC_TARGET_WINDOWS
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFileAttributesW(string name);

        public static FileAttributes GetAttributes(string fullPath, string name)
        {
            uint attributes = GetFileAttributesW(fullPath);
            return attributes == 0xFFFFFFFF ? (FileAttributes)(-1) : (FileAttributes)attributes;
        }

        public static string ReadLink(string path) =>
            throw new NotSupportedException("Symbolic link targets on Windows are not supported by csharpc yet.");

        public static void SetUnixFileMode(string path, UnixFileMode mode) => throw new PlatformNotSupportedException("Unix file modes are not supported on this platform.");
        public static UnixFileMode GetUnixFileMode(string path) => throw new PlatformNotSupportedException("Unix file modes are not supported on this platform.");
        public static void CreateSymbolicLink(string path, string target) =>
            throw new NotSupportedException("Symbolic links on Windows are not supported by csharpc yet.");
#else
        private const int UserRead = 0x100, UserWrite = 0x80, GroupRead = 0x20, GroupWrite = 0x10, OtherRead = 0x4, OtherWrite = 0x2;
#if CSHARPC_TARGET_MACOS
        // struct stat (Darwin): st_uid @16, st_gid @20, st_flags @116.
        private static uint Uid(byte* st) => *(uint*)(st + 16);
        private static uint Gid(byte* st) => *(uint*)(st + 20);
        private static bool HasHiddenFlag(byte* st) => (*(uint*)(st + 116) & 0x8000 /* UF_HIDDEN */) != 0;
#elif !CSHARPC_TARGET_WASI
        // struct statx: stx_uid @20, stx_gid @24.
        private static uint Uid(byte* st) => *(uint*)(st + 20);
        private static uint Gid(byte* st) => *(uint*)(st + 24);
        private static bool HasHiddenFlag(byte* st) => false;
#endif
#if !CSHARPC_TARGET_WASI
        [DllImport("libc")] private static extern uint geteuid();
        [DllImport("libc")] private static extern uint getegid();
        [DllImport("libc")] private static extern int getgroups(int size, uint* list);
#endif
        [DllImport("libc", SetLastError = true)] private static extern nint readlink(byte* path, byte* buffer, nuint size);

        public static FileAttributes GetAttributes(string fullPath, string name)
        {
            string path = PathImpl.TrimEndingDirectorySeparator(fullPath);
            byte* st = stackalloc byte[Posix.StatSize];
            byte* p = Utf8.ToNative(path);
            int rv = Posix.lstat(p, st);
            int errno = Posix.Errno;
            try
            {
                if (rv < 0)
                {
                    if (errno == Posix.ENOENT || errno == Posix.ENOTDIR) return (FileAttributes)(-1);
                    throw Posix.IOError(errno, fullPath, false);
                }
                int mode = Posix.Mode(st);
                bool isLink = (mode & Posix.S_IFMT) == Posix.S_IFLNK;
                bool isDirectory = (mode & Posix.S_IFMT) == Posix.S_IFDIR;
                bool brokenLink = false;
                if (isLink)
                {
                    // A link reports its target's kind and permissions (FileStatus.RefreshCaches).
                    byte* target = stackalloc byte[Posix.StatSize];
                    if (Posix.stat(p, target) == 0)
                    {
                        int targetMode = Posix.Mode(target);
                        isDirectory = (targetMode & Posix.S_IFMT) == Posix.S_IFDIR;
                        mode = Posix.S_IFLNK | (targetMode & 0xFFF);
                    }
                    else brokenLink = true;
                }
                FileAttributes attributes = 0;
                if (!brokenLink && IsReadOnly(mode, st)) attributes |= FileAttributes.ReadOnly;
                if (isLink) attributes |= FileAttributes.ReparsePoint;
                if (isDirectory) attributes |= FileAttributes.Directory;
                if ((name != null && name.StartsWith('.')) || HasHidden(st)) attributes |= FileAttributes.Hidden;
                return attributes != 0 ? attributes : FileAttributes.Normal;
            }
            finally { Libc.free(p); }
        }

#if CSHARPC_TARGET_WASI
        private static bool HasHidden(byte* st) => false;

        private static bool IsReadOnly(int mode, byte* st) => (mode & UserRead) != 0 && (mode & UserWrite) == 0;
#else
        private static bool HasHidden(byte* st) => HasHiddenFlag(st);

        private static bool IsReadOnly(int mode, byte* st)
        {
            bool user = (mode & UserRead) != 0 && (mode & UserWrite) == 0;
            bool group = (mode & GroupRead) != 0 && (mode & GroupWrite) == 0;
            bool other = (mode & OtherRead) != 0 && (mode & OtherWrite) == 0;
            if (user == group && group == other) return user;
            if (Uid(st) == geteuid()) return user;
            if (group == user) return group;
            return IsMemberOfGroup(Gid(st)) ? group : other;
        }

        /// <summary>System.Native's SystemNative_IsMemberOfGroup: the effective gid or a supplementary group.</summary>
        private static bool IsMemberOfGroup(uint gid)
        {
            if (getegid() == gid) return true;
            int count = getgroups(0, null);
            if (count <= 0) return false;
            uint* groups = stackalloc uint[count];
            count = getgroups(count, groups);
            for (int i = 0; i < count; i++) if (groups[i] == gid) return true;
            return false;
        }
#endif

        [DllImport("libc", SetLastError = true)] private static extern int symlink(byte* target, byte* path);
#if CSHARPC_TARGET_WASI
        // WASI has no permission bits to change.
        public static void SetUnixFileMode(string path, UnixFileMode mode) => throw new PlatformNotSupportedException("Unix file modes are not supported on this platform.");
#else
        [DllImport("libc", SetLastError = true)] private static extern int chmod(byte* path, int mode);

        public static void SetUnixFileMode(string path, UnixFileMode mode)
        {
            if (((int)mode & ~0xFFF) != 0) throw new ArgumentException("Invalid UnixFileMode value. (Parameter 'mode')");
            byte* p = Utf8.ToNative(PathImpl.GetFullPath(path));
            int rv = chmod(p, (int)mode);
            int errno = Posix.Errno;
            Libc.free(p);
            if (rv < 0) throw Posix.IOError(errno, path, false);
        }
#endif

        public static UnixFileMode GetUnixFileMode(string path)
        {
            byte* st = stackalloc byte[Posix.StatSize];
            byte* p = Utf8.ToNative(PathImpl.GetFullPath(path));
            int rv = Posix.stat(p, st);
            int errno = Posix.Errno;
            Libc.free(p);
            if (rv < 0) throw Posix.IOError(errno, path, false);
            return (UnixFileMode)(Posix.Mode(st) & 0xFFF);
        }

        public static void CreateSymbolicLink(string path, string target)
        {
            byte* t = Utf8.ToNative(target);
            byte* p = Utf8.ToNative(PathImpl.GetFullPath(path));
            int rv = symlink(t, p);
            int errno = Posix.Errno;
            Libc.free(t);
            Libc.free(p);
            if (rv < 0) throw Posix.IOError(errno, path, false);
        }

        /// <summary>readlink(2): the link's target, or null when it is not a link or can't be read (Interop.Sys.ReadLink).</summary>
        public static string ReadLink(string path)
        {
            byte* p = Utf8.ToNative(path);
            try
            {
                for (int size = 256; ; size *= 2)
                {
                    byte* buffer = (byte*)Libc.malloc((nuint)size);
                    nint n = readlink(p, buffer, (nuint)size);
                    if (n < 0) { Libc.free(buffer); return null; }
                    if (n < size)
                    {
                        string target = Utf8.Decode(buffer, (int)n);
                        Libc.free(buffer);
                        return target;
                    }
                    Libc.free(buffer);
                }
            }
            finally { Libc.free(p); }
        }
#endif

        /// <summary>FileSystem.ResolveLinkTarget: null when <paramref name="linkPath"/> is not a link.</summary>
        public static FileSystemInfo ResolveLinkTarget(string linkPath, bool returnFinalTarget, bool isDirectory)
        {
            const int MaxFollowedLinks = 40;
            string linkTarget = ReadLink(linkPath);
            if (linkTarget == null)
            {
                int errno = Posix.Errno;
                if (errno == EINVAL) return null;  // not a link
                throw Posix.IOError(errno, linkPath, isDirectory);
            }
            string current = linkPath;
            if (!returnFinalTarget) current = TargetFullPath(current, linkTarget);
            else
            {
                string next = linkTarget;
                int visitCount = 1;
                while (next != null)
                {
                    if (visitCount > MaxFollowedLinks) throw new IOException("Too many levels of symbolic links in '" + linkPath + "'.");
                    current = TargetFullPath(current, next);
                    next = ReadLink(current);
                    visitCount++;
                }
            }
            return isDirectory ? (FileSystemInfo)new DirectoryInfo(current) : new FileInfo(current);
        }

        // A relative target is relative to the link's directory.
        private static string TargetFullPath(string link, string target)
        {
            if (PathImpl.IsPathRooted(target)) return target;
            string directory = PathImpl.GetDirectoryName(link) ?? "";
            return directory.EndsWith('/') ? directory + target : directory + "/" + target;
        }

#if CSHARPC_TARGET_WASI
        private const int EINVAL = 28;
#elif CSHARPC_TARGET_WINDOWS
        private const int EINVAL = 22;
#else
        private const int EINVAL = 22;
#endif
    }
}
