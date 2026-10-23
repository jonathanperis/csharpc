// System.IO.Path members with .NET's Windows semantics: '\' and '/' both separate, roots are "C:\",
// "C:", "\" and "\\server\share", full paths come from GetFullPathNameW, and comparisons ignore case.
#if CSHARPC_TARGET_WINDOWS
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CsharpcCoreLib
{
    internal static unsafe partial class PathImpl
    {
        public static readonly char DirectorySeparatorChar = '\\';
        public static readonly char AltDirectorySeparatorChar = '/';
        public static readonly char VolumeSeparatorChar = ':';
        public static readonly char PathSeparator = ';';
        private const char Sep = '\\';
        private const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFullPathNameW(string path, uint length, char* buffer, char** filePart);
        [DllImport("kernel32")] private static extern uint GetTempPathW(uint length, char* buffer);

        private static bool IsSep(char c) => c == '\\' || c == '/';
        private static bool IsDriveLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        /// <summary>Length of the root: "C:\" 3, "C:" 2, "\" 1, "\\server\share\" / "\\?\..." up to the share.</summary>
        private static int RootLength(string path)
        {
            int n = path.Length;
            if (n >= 2 && IsSep(path[0]) && IsSep(path[1]))
            {
                // UNC (\\server\share) or device (\\?\, \\.\) path: the root runs through the second
                // component after the prefix.
                bool device = n >= 4 && (path[2] == '?' || path[2] == '.') && IsSep(path[3]);
                int i = device ? 4 : 2;
                if (device && n >= 8 && (path[4] | 0x20) == 'u' && (path[5] | 0x20) == 'n' && (path[6] | 0x20) == 'c' && IsSep(path[7])) i = 8;
                else if (device) { while (i < n && !IsSep(path[i])) i++; return i < n ? i + 1 : i; }
                int components = 0;
                while (i < n)
                {
                    if (IsSep(path[i]) && ++components == 2) break;
                    i++;
                }
                return i;
            }
            if (n >= 1 && IsSep(path[0])) return 1;
            if (n >= 2 && path[1] == ':' && IsDriveLetter(path[0])) return n >= 3 && IsSep(path[2]) ? 3 : 2;
            return 0;
        }

        /// <summary>'/' becomes '\' and runs of separators collapse (except a leading "\\").</summary>
        private static string NormalizeSeparators(string path)
        {
            var sb = new StringBuilder(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (IsSep(c))
                {
                    if (i > 1 && IsSep(path[i - 1])) continue;
                    c = '\\';
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool IsEffectivelyEmpty(string path)
        {
            for (int i = 0; i < path.Length; i++) if (path[i] != ' ') return false;
            return true;
        }

        public static bool IsPathRooted(string path) =>
            path != null && (path.Length >= 1 && IsSep(path[0]) || path.Length >= 2 && path[1] == ':' && IsDriveLetter(path[0]));

        public static bool IsPathFullyQualified(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Length < 2) return false;
            if (IsSep(path[0])) return path[1] == '?' || IsSep(path[1]);
            return path.Length >= 3 && path[1] == ':' && IsSep(path[2]) && IsDriveLetter(path[0]);
        }

        public static string GetPathRoot(string path)
        {
            if (path == null || IsEffectivelyEmpty(path)) return null;
            return NormalizeSeparators(path.Substring(0, RootLength(path)));
        }

        public static string TrimEndingDirectorySeparator(string path) =>
            EndsInDirectorySeparator(path) && path.Length > RootLength(path) ? path.Substring(0, path.Length - 1) : path;

        public static char[] GetInvalidPathChars()
        {
            char[] c = new char[33];
            c[0] = '|';
            for (int i = 0; i < 32; i++) c[i + 1] = (char)i;
            return c;
        }

        public static char[] GetInvalidFileNameChars()
        {
            char[] c = new char[41];
            c[0] = '"'; c[1] = '<'; c[2] = '>'; c[3] = '|';
            for (int i = 0; i < 32; i++) c[i + 4] = (char)i;
            c[36] = ':'; c[37] = '*'; c[38] = '?'; c[39] = '\\'; c[40] = '/';
            return c;
        }

        public static string GetFileName(string path)
        {
            if (path == null) return null;
            int root = RootLength(path);
            int i = path.Length - 1;
            while (i >= 0 && !IsSep(path[i])) i--;
            return path.Substring(i < root ? root : i + 1);
        }

        public static string GetDirectoryName(string path)
        {
            if (path == null || IsEffectivelyEmpty(path)) return null;
            int root = RootLength(path);
            int end = path.Length;
            if (end <= root) return null;
            while (end > root && !IsSep(path[--end])) { }
            while (end > root && IsSep(path[end - 1])) end--;
            return NormalizeSeparators(path.Substring(0, end));
        }

        public static string GetFullPath(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Length == 0) throw new ArgumentException("The value cannot be an empty string. (Parameter 'path')");
            if (path.IndexOf('\0') >= 0) throw new ArgumentException("Null character in path. (Parameter 'path')");
            uint size = 260;
            while (true)
            {
                char[] buffer = new char[size];
                uint n;
                fixed (char* p = buffer) n = GetFullPathNameW(path, size, p, null);
                if (n == 0) throw Posix.IOError(Marshal.GetLastPInvokeError() + 0x10000, path, false);
                if (n < size) return new string(buffer, 0, (int)n);
                size = n;
            }
        }

        public static string GetFullPath(string path, string basePath)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (basePath == null) throw new ArgumentNullException("basePath");
            if (!IsPathFullyQualified(basePath)) throw new ArgumentException("Basepath argument is not fully qualified. (Parameter 'basePath')");
            if (path.IndexOf('\0') >= 0 || basePath.IndexOf('\0') >= 0) throw new ArgumentException("Null character in path.");
            if (IsPathFullyQualified(path)) return GetFullPath(path);
            if (path.Length == 0) return basePath;
            string combined;
            int length = path.Length;
            if (length >= 1 && IsSep(path[0]))
                combined = GetPathRoot(basePath).TrimEnd('\\') + path;   // "\x": the base's volume
            else if (length >= 2 && path[1] == ':' && IsDriveLetter(path[0]))
                // "C:x": relative to the base only when it is on the same drive.
                combined = string.Equals(GetPathRoot(basePath).Substring(0, 2), path.Substring(0, 2), StringComparison.OrdinalIgnoreCase)
                    ? CombineTwo(basePath, path.Substring(2)) : path.Substring(0, 2) + "\\" + path.Substring(2);
            else
                combined = CombineTwo(basePath, path);
            return GetFullPath(combined);
        }

        public static string GetTempPath()
        {
            char* buffer = stackalloc char[261];
            uint n = GetTempPathW(261, buffer);
            return new string(buffer, 0, (int)n);
        }

        public static string GetTempFileName()
        {
            // GetTempFileNameW's names: "tmp" + up to four hex digits + ".tmp".
            string dir = GetTempPath();
            byte* random = stackalloc byte[2];
            for (int attempt = 0; attempt < 65536; attempt++)
            {
                Posix.getentropy(random, 2);
                string candidate = dir + "tmp" + (random[0] | random[1] << 8).ToString("X") + ".tmp";
                byte* p = Utf8.ToNative(candidate);
                int fd = Posix.open(p, Posix.O_CREAT | Posix.O_EXCL | Posix.O_WRONLY, 0x180, 0);
                int errno = Posix.Errno;
                Libc.free(p);
                if (fd >= 0) { Posix.close(fd); return candidate; }
                if (errno != Posix.EEXIST) throw Posix.IOError(errno, candidate, false);
            }
            throw new System.IO.IOException("The file exists.");
        }
    }
}
#endif
