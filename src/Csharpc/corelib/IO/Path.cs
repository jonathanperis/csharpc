// System.IO.Path with .NET's Unix semantics ('/' separators, rooted == starts with '/'); the Windows
// members (drive letters, '\\' and '/' separators, UNC roots) are in PathWindows.cs.
#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.Path")]
    internal static unsafe partial class PathImpl
    {
#if !CSHARPC_TARGET_WINDOWS
        public static readonly char DirectorySeparatorChar = '/';
        public static readonly char AltDirectorySeparatorChar = '/';
        public static readonly char VolumeSeparatorChar = '/';
        public static readonly char PathSeparator = ':';
        private const char Sep = '/';

        private static bool IsSep(char c) => c == '/';

        public static bool IsPathRooted(string path) => path != null && path.Length > 0 && path[0] == '/';

        public static bool IsPathFullyQualified(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            return IsPathRooted(path);
        }

        public static string GetPathRoot(string path) => path == null ? null : IsPathRooted(path) ? "/" : "";

        public static string TrimEndingDirectorySeparator(string path) =>
            EndsInDirectorySeparator(path) && path.Length > 1 ? path.Substring(0, path.Length - 1) : path;

        public static char[] GetInvalidPathChars() { char[] c = new char[1]; c[0] = '\0'; return c; }
        public static char[] GetInvalidFileNameChars() { char[] c = new char[2]; c[0] = '\0'; c[1] = '/'; return c; }
#endif

        public static bool EndsInDirectorySeparator(string path) => path != null && path.Length > 0 && IsSep(path[path.Length - 1]);

        // ---- Combine / Join ------------------------------------------------------------------

        public static string Combine(string path1, string path2)
        {
            if (path1 == null) throw new ArgumentNullException("path1");
            if (path2 == null) throw new ArgumentNullException("path2");
            return CombineTwo(path1, path2);
        }

        public static string Combine(string path1, string path2, string path3)
        {
            if (path1 == null) throw new ArgumentNullException("path1");
            if (path2 == null) throw new ArgumentNullException("path2");
            if (path3 == null) throw new ArgumentNullException("path3");
            return CombineTwo(CombineTwo(path1, path2), path3);
        }

        public static string Combine(string path1, string path2, string path3, string path4)
        {
            if (path1 == null) throw new ArgumentNullException("path1");
            if (path2 == null) throw new ArgumentNullException("path2");
            if (path3 == null) throw new ArgumentNullException("path3");
            if (path4 == null) throw new ArgumentNullException("path4");
            return CombineTwo(CombineTwo(CombineTwo(path1, path2), path3), path4);
        }

        public static string Combine(params string[] paths)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            string result = "";
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] == null) throw new ArgumentNullException("paths");
                result = CombineTwo(result, paths[i]);
            }
            return result;
        }

        private static string CombineTwo(string first, string second)
        {
            if (second.Length == 0) return first;
            if (first.Length == 0 || IsPathRooted(second)) return second;
            return IsSep(first[first.Length - 1]) ? first + second : first + Sep + second;
        }

        public static string Join(string path1, string path2) => JoinTwo(path1 ?? "", path2 ?? "");
        public static string Join(string path1, string path2, string path3) => JoinTwo(JoinTwo(path1 ?? "", path2 ?? ""), path3 ?? "");
        public static string Join(string path1, string path2, string path3, string path4) =>
            JoinTwo(JoinTwo(JoinTwo(path1 ?? "", path2 ?? ""), path3 ?? ""), path4 ?? "");

        public static string Join(params string[] paths)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            string result = "";
            for (int i = 0; i < paths.Length; i++) result = JoinTwo(result, paths[i] ?? "");
            return result;
        }

        private static string JoinTwo(string first, string second)
        {
            if (first.Length == 0) return second;
            if (second.Length == 0) return first;
            return IsSep(first[first.Length - 1]) || IsSep(second[0]) ? first + second : first + Sep + second;
        }

        // ---- Components -------------------------------------------------------------------------

#if !CSHARPC_TARGET_WINDOWS
        public static string GetFileName(string path)
        {
            if (path == null) return null;
            int i = path.LastIndexOf('/');
            return i < 0 ? path : path.Substring(i + 1);
        }
#endif

        public static string GetFileNameWithoutExtension(string path)
        {
            if (path == null) return null;
            string name = GetFileName(path);
            int dot = name.LastIndexOf('.');
            return dot < 0 ? name : name.Substring(0, dot);
        }

        private static int ExtensionStart(string path)
        {
            for (int i = path.Length - 1; i >= 0; i--)
            {
                char c = path[i];
                if (c == '.') return i;
                if (IsSep(c)) break;
            }
            return -1;
        }

        public static string GetExtension(string path)
        {
            if (path == null) return null;
            int dot = ExtensionStart(path);
            return dot < 0 || dot == path.Length - 1 ? "" : path.Substring(dot);
        }

        public static bool HasExtension(string path)
        {
            if (path == null) return false;
            int dot = ExtensionStart(path);
            return dot >= 0 && dot < path.Length - 1;
        }

        public static string ChangeExtension(string path, string extension)
        {
            if (path == null) return null;
            int dot = ExtensionStart(path);
            string stem = dot < 0 ? path : path.Substring(0, dot);
            if (extension == null) return stem;
            if (extension.Length == 0 || extension[0] != '.') return stem + "." + extension;
            return stem + extension;
        }

#if !CSHARPC_TARGET_WINDOWS
        public static string GetDirectoryName(string path)
        {
            if (path == null || path.Length == 0) return null;
            int root = IsPathRooted(path) ? 1 : 0;
            int end = path.Length;
            while (end > root && !IsSep(path[end - 1])) end--;
            if (end <= root) return root == 1 && path.Length > 1 ? "/" : (root == 1 ? null : "");
            while (end > root && IsSep(path[end - 1])) end--;
            return end == 0 ? "/" : path.Substring(0, end);
        }

        // ---- Full / relative paths --------------------------------------------------------------

        public static string GetFullPath(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Length == 0) throw new ArgumentException("The value cannot be an empty string. (Parameter 'path')");
            if (path.IndexOf('\0') >= 0) throw new ArgumentException("Null character in path. (Parameter 'path')");
            string combined = IsPathRooted(path) ? path : CombineTwo(DirectoryImpl.GetCurrentDirectory(), path);
            return Normalize(combined);
        }

        public static string GetFullPath(string path, string basePath)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (basePath == null) throw new ArgumentNullException("basePath");
            if (!IsPathRooted(basePath)) throw new ArgumentException("Basepath argument is not fully qualified. (Parameter 'basePath')");
            return Normalize(IsPathRooted(path) ? path : CombineTwo(basePath, path));
        }

        /// <summary>Resolves "." / ".." and collapses repeated separators; keeps a trailing separator.</summary>
        private static string Normalize(string path)
        {
            bool trailing = path.Length > 1 && IsSep(path[path.Length - 1]);
            var parts = new List<string>();
            int start = 1;
            for (int i = 1; i <= path.Length; i++)
            {
                if (i == path.Length || IsSep(path[i]))
                {
                    string part = path.Substring(start, i - start);
                    if (part.Length == 0 || part == ".") { }
                    else if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                    else parts.Add(part);
                    start = i + 1;
                }
            }
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++) sb.Append('/').Append(parts[i]);
            if (sb.Length == 0) return "/";
            if (trailing) sb.Append('/');
            return sb.ToString();
        }

        private const StringComparison PathComparison = StringComparison.Ordinal;
#endif

        public static string GetRelativePath(string relativeTo, string path)
        {
            if (relativeTo == null) throw new ArgumentNullException("relativeTo");
            if (path == null) throw new ArgumentNullException("path");
            string from = GetFullPath(relativeTo);
            string to = GetFullPath(path);
#if CSHARPC_TARGET_WINDOWS
            // Different volumes: no relative path exists.
            if (!string.Equals(GetPathRoot(from), GetPathRoot(to), PathComparison)) return path;
#endif
            string[] a = from.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
            string[] b = to.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
            int common = 0;
            while (common < a.Length && common < b.Length && string.Equals(a[common], b[common], PathComparison)) common++;
            if (common == a.Length && common == b.Length) return ".";
            var sb = new StringBuilder();
            for (int i = common; i < a.Length; i++) { if (sb.Length > 0) sb.Append(Sep); sb.Append(".."); }
            for (int i = common; i < b.Length; i++) { if (sb.Length > 0) sb.Append(Sep); sb.Append(b[i]); }
            if (EndsInDirectorySeparator(to) && sb.Length > 0) sb.Append(Sep);
            return sb.ToString();
        }

        // ---- Temp / random ----------------------------------------------------------------------

#if !CSHARPC_TARGET_WINDOWS
        public static string GetTempPath()
        {
            string tmp = EnvironmentExtras.GetEnvironmentVariable("TMPDIR");
            if (string.IsNullOrEmpty(tmp)) tmp = "/tmp/";
            return EndsInDirectorySeparator(tmp) ? tmp : tmp + "/";
        }

#endif

        public static string GetRandomFileName()
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz012345";
            byte* random = stackalloc byte[11];
            Posix.getentropy(random, 11);
            char[] name = new char[12];
            for (int i = 0, j = 0; i < 12; i++)
                name[i] = i == 8 ? '.' : alphabet[random[j++] & 31];
            return new string(name);
        }

#if !CSHARPC_TARGET_WINDOWS
        public static string GetTempFileName()
        {
            string dir = GetTempPath();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string candidate = dir + "tmp" + GetRandomFileName().Substring(0, 6).ToUpperInvariant() + ".tmp";
                byte* p = Utf8.ToNative(candidate);
                int fd = Posix.open(p, Posix.O_CREAT | Posix.O_EXCL | Posix.O_WRONLY | Posix.O_CLOEXEC, 0x180);
                int errno = Posix.Errno;
                Libc.free(p);
                if (fd >= 0) { Posix.close(fd); return candidate; }
                if (errno != Posix.EEXIST) throw Posix.IOError(errno, candidate, false);
            }
            throw new System.IO.IOException("Unable to create a temporary file.");
        }
#endif

        public static bool Exists(string path) => !string.IsNullOrEmpty(path) && Posix.FileType(path) != 0;
    }
}
