// System.IO.File / Directory / FileInfo / DirectoryInfo over libc (Unix semantics as in .NET).
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.File")]
    internal static unsafe class FileImpl
    {
        public static bool Exists(string path) => !string.IsNullOrEmpty(path) && Posix.IsFile(path);

        public static FileStream Open(string path, FileMode mode) => new FileStream(path, mode);
        public static FileStream Open(string path, FileMode mode, FileAccess access) => new FileStream(path, mode, access);
        public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) => new FileStream(path, mode, access, share);
        public static FileStream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read);
        public static FileStream OpenWrite(string path) => new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
        public static FileStream Create(string path) => new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        public static StreamReader OpenText(string path) => new StreamReader(path);
        public static StreamWriter CreateText(string path) => new StreamWriter(path, false);
        public static StreamWriter AppendText(string path) => new StreamWriter(path, true);

        public static byte[] ReadAllBytes(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1))
            {
                long length = fs.Length;
                if (length > int.MaxValue) throw new IOException("The file is too long. This operation is currently limited to supporting files less than 2 gigabytes in size.");
                byte[] bytes = new byte[length];
                int total = 0;
                while (total < bytes.Length)
                {
                    int n = fs.Read(bytes, total, bytes.Length - total);
                    if (n == 0) break;
                    total += n;
                }
                if (total == bytes.Length && length > 0) return bytes;
                // Size unknown up front (e.g. /proc files): read to EOF.
                var ms = new MemoryStream();
                ms.Write(bytes, 0, total);
                byte[] chunk = new byte[4096];
                int r;
                while ((r = fs.Read(chunk, 0, chunk.Length)) > 0) ms.Write(chunk, 0, r);
                return ms.ToArray();
            }
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1)) fs.Write(bytes, 0, bytes.Length);
        }

        public static void AppendAllBytes(string path, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 1)) fs.Write(bytes, 0, bytes.Length);
        }

        public static string ReadAllText(string path) => ReadAllText(path, new UTF8Encoding(false));

        public static string ReadAllText(string path, Encoding encoding)
        {
            using (var reader = new StreamReader(path, encoding, true)) return reader.ReadToEnd();
        }

        public static string[] ReadAllLines(string path) => ReadAllLines(path, new UTF8Encoding(false));

        public static string[] ReadAllLines(string path, Encoding encoding)
        {
            var lines = new List<string>();
            using (var reader = new StreamReader(path, encoding, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }
            return lines.ToArray();
        }

        public static IEnumerable<string> ReadLines(string path) => ReadLines(path, new UTF8Encoding(false));

        public static IEnumerable<string> ReadLines(string path, Encoding encoding)
        {
            if (path == null) throw new ArgumentNullException("path");
            var reader = new StreamReader(path, encoding, true);   // opened eagerly, like .NET
            return ReadLinesIterator(reader);
        }

        private static IEnumerable<string> ReadLinesIterator(StreamReader reader)
        {
            using (reader)
            {
                string line;
                while ((line = reader.ReadLine()) != null) yield return line;
            }
        }

        public static void WriteAllText(string path, string contents) => WriteAllText(path, contents, new UTF8Encoding(false));

        public static void WriteAllText(string path, string contents, Encoding encoding)
        {
            using (var writer = new StreamWriter(path, false, encoding)) writer.Write(contents);
        }

        public static void AppendAllText(string path, string contents) => AppendAllText(path, contents, new UTF8Encoding(false));

        public static void AppendAllText(string path, string contents, Encoding encoding)
        {
            using (var writer = new StreamWriter(path, true, encoding)) writer.Write(contents);
        }

        public static void WriteAllLines(string path, string[] contents) => WriteAllLines(path, (IEnumerable<string>)contents, new UTF8Encoding(false));
        public static void WriteAllLines(string path, IEnumerable<string> contents) => WriteAllLines(path, contents, new UTF8Encoding(false));
        public static void WriteAllLines(string path, string[] contents, Encoding encoding) => WriteAllLines(path, (IEnumerable<string>)contents, encoding);

        public static void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            if (contents == null) throw new ArgumentNullException("contents");
            using (var writer = new StreamWriter(path, false, encoding))
                foreach (var line in contents) writer.WriteLine(line);
        }

        public static void AppendAllLines(string path, IEnumerable<string> contents) => AppendAllLines(path, contents, new UTF8Encoding(false));

        public static void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            if (contents == null) throw new ArgumentNullException("contents");
            using (var writer = new StreamWriter(path, true, encoding))
                foreach (var line in contents) writer.WriteLine(line);
        }

        public static Task<string> ReadAllTextAsync(string path) => Task.FromResult(ReadAllText(path));
        public static Task<string[]> ReadAllLinesAsync(string path) => Task.FromResult(ReadAllLines(path));
        public static Task<byte[]> ReadAllBytesAsync(string path) => Task.FromResult(ReadAllBytes(path));
        public static Task WriteAllTextAsync(string path, string contents) { WriteAllText(path, contents); return Task.CompletedTask; }
        public static Task WriteAllBytesAsync(string path, byte[] bytes) { WriteAllBytes(path, bytes); return Task.CompletedTask; }
        public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents) { WriteAllLines(path, contents); return Task.CompletedTask; }
        public static Task AppendAllTextAsync(string path, string contents) { AppendAllText(path, contents); return Task.CompletedTask; }

        public static void Delete(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            byte* p = Utf8.ToNative(path);
            int r = Posix.unlink(p);
            int errno = Posix.Errno;
            Libc.free(p);
            if (r == 0 || errno == Posix.ENOENT) return;     // deleting a missing file is not an error
            if (errno == Posix.EISDIR || errno == Posix.EPERM && Posix.IsDirectory(path))
                throw new UnauthorizedAccessException("Access to the path '" + PathImpl.GetFullPath(path) + "' is denied.");
            throw Posix.IOError(errno, path, false);
        }

        public static void Copy(string sourceFileName, string destFileName) => Copy(sourceFileName, destFileName, false);

        public static void Copy(string sourceFileName, string destFileName, bool overwrite)
        {
            if (sourceFileName == null) throw new ArgumentNullException("sourceFileName");
            if (destFileName == null) throw new ArgumentNullException("destFileName");
            if (Posix.IsDirectory(sourceFileName)) throw new UnauthorizedAccessException("Access to the path '" + PathImpl.GetFullPath(sourceFileName) + "' is denied.");
            using (var src = new FileStream(sourceFileName, FileMode.Open, FileAccess.Read))
            {
                if (!overwrite && Posix.FileType(destFileName) != 0)
                    throw new IOException("The file '" + PathImpl.GetFullPath(destFileName) + "' already exists.");
                using (var dst = new FileStream(destFileName, FileMode.Create, FileAccess.Write)) src.CopyTo(dst);
            }
        }

        public static void Move(string sourceFileName, string destFileName) => Move(sourceFileName, destFileName, false);

        public static void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            if (sourceFileName == null) throw new ArgumentNullException("sourceFileName");
            if (destFileName == null) throw new ArgumentNullException("destFileName");
            if (!Posix.IsFile(sourceFileName))
                throw new FileNotFoundException("Could not find file '" + PathImpl.GetFullPath(sourceFileName) + "'.", PathImpl.GetFullPath(sourceFileName));
            if (!overwrite && Posix.FileType(destFileName) != 0)
                throw new IOException("Cannot create a file when that file already exists.");
            byte* a = Utf8.ToNative(sourceFileName), b = Utf8.ToNative(destFileName);
            int r = Posix.rename(a, b);
            int errno = Posix.Errno;
            Libc.free(a); Libc.free(b);
            if (r != 0) throw Posix.IOError(errno, destFileName, false);
        }

        public static DateTime GetLastWriteTimeUtc(string path)
        {
            byte* st = stackalloc byte[Posix.StatSize];
            byte* p = Utf8.ToNative(path);
            int r = Posix.stat(p, st);
            Libc.free(p);
            if (r != 0) return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(Posix.MTimeSeconds(st) * 10000000 + Posix.MTimeNanoseconds(st) / 100);
        }

        public static DateTime GetLastWriteTime(string path) => GetLastWriteTimeUtc(path).ToLocalTime();

        internal static long Length(string path)
        {
            byte* st = stackalloc byte[Posix.StatSize];
            byte* p = Utf8.ToNative(path);
            int r = Posix.stat(p, st);
            int errno = Posix.Errno;
            Libc.free(p);
            if (r != 0 || (Posix.Mode(st) & Posix.S_IFMT) == Posix.S_IFDIR)
                throw new FileNotFoundException("Could not find file '" + PathImpl.GetFullPath(path) + "'.", PathImpl.GetFullPath(path));
            return Posix.Size(st);
        }
    }

    [Shadows("System.IO.Directory")]
    internal static unsafe class DirectoryImpl
    {
        public static string GetCurrentDirectory()
        {
            nuint size = 256;
            while (true)
            {
                byte* buffer = (byte*)Libc.malloc(size);
                byte* r = Posix.getcwd(buffer, size);
                if (r != null)
                {
                    string s = Utf8.FromNative(buffer);
                    Libc.free(buffer);
                    return s;
                }
                int errno = Posix.Errno;
                Libc.free(buffer);
                if (errno != 34 /* ERANGE */) throw Posix.IOError(errno, ".", true);
                size *= 2;
            }
        }

        public static void SetCurrentDirectory(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Length == 0) throw new ArgumentException("The value cannot be an empty string. (Parameter 'path')");
            byte* p = Utf8.ToNative(path);
            int r = Posix.chdir(p);
            int errno = Posix.Errno;
            Libc.free(p);
            if (r != 0) throw Posix.IOError(errno == Posix.ENOENT ? Posix.ENOTDIR : errno, path, true);
        }

        public static bool Exists(string path) => !string.IsNullOrEmpty(path) && Posix.IsDirectory(path);

        public static DirectoryInfo CreateDirectory(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Length == 0) throw new ArgumentException("The value cannot be an empty string. (Parameter 'path')");
            string full = PathImpl.GetFullPath(path);
            CreateRecursive(PathImpl.TrimEndingDirectorySeparator(full));
            return new DirectoryInfo(path);
        }

        private static void CreateRecursive(string full)
        {
            int type = Posix.FileType(full);
            if (type == Posix.S_IFDIR) return;
            if (type != 0) throw new IOException("The file '" + full + "' already exists.");
            string parent = PathImpl.GetDirectoryName(full);
            if (parent != null) CreateRecursive(parent);
            byte* p = Utf8.ToNative(full);
            int r = Posix.mkdir(p, 0x1FF);
            int errno = Posix.Errno;
            Libc.free(p);
            if (r != 0 && errno != Posix.EEXIST) throw Posix.IOError(errno, full, true);
        }

        public static void Delete(string path) => Delete(path, false);

        public static void Delete(string path, bool recursive)
        {
            if (path == null) throw new ArgumentNullException("path");
            string full = PathImpl.GetFullPath(path);
            if (!Posix.IsDirectory(full))
                throw new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.");
            if (recursive) DeleteRecursive(full);
            else RemoveDir(full);
        }

        private static void DeleteRecursive(string dir)
        {
            foreach (var entry in Enumerate(dir, "*", false, true, true, false))
            {
                if (Posix.IsDirectory(entry) && !IsSymlink(entry)) DeleteRecursive(entry);
                else FileImpl.Delete(entry);
            }
            RemoveDir(dir);
        }

        private static bool IsSymlink(string path)
        {
            byte* st = stackalloc byte[Posix.StatSize];
            byte* p = Utf8.ToNative(path);
            int r = Posix.lstat(p, st);
            Libc.free(p);
            return r == 0 && (Posix.Mode(st) & Posix.S_IFMT) == Posix.S_IFLNK;
        }

        private static void RemoveDir(string full)
        {
            byte* p = Utf8.ToNative(full);
            int r = Posix.rmdir(p);
            int errno = Posix.Errno;
            Libc.free(p);
            if (r == 0) return;
            if (errno == Posix.ENOTEMPTY || errno == Posix.EEXIST) throw new IOException("Directory not empty : '" + full + "'");
            throw Posix.IOError(errno, full, true);
        }

        public static void Move(string sourceDirName, string destDirName)
        {
            if (sourceDirName == null) throw new ArgumentNullException("sourceDirName");
            if (destDirName == null) throw new ArgumentNullException("destDirName");
            string src = PathImpl.GetFullPath(sourceDirName), dst = PathImpl.GetFullPath(destDirName);
            if (Posix.FileType(src) == 0) throw new DirectoryNotFoundException("Could not find a part of the path '" + src + "'.");
            if (Posix.FileType(dst) != 0) throw new IOException("Cannot create '" + dst + "' because a file or directory with the same name already exists.");
            byte* a = Utf8.ToNative(src), b = Utf8.ToNative(dst);
            int r = Posix.rename(a, b);
            int errno = Posix.Errno;
            Libc.free(a); Libc.free(b);
            if (r != 0) throw Posix.IOError(errno, src, true);
        }

        public static string GetParent(string path) => PathImpl.GetDirectoryName(PathImpl.GetFullPath(path));

        public static string[] GetFiles(string path) => ToArray(Enumerate(path, "*", false, true, false, true));
        public static string[] GetFiles(string path, string searchPattern) => ToArray(Enumerate(path, searchPattern, false, true, false, true));
        public static string[] GetFiles(string path, string searchPattern, SearchOption searchOption) =>
            ToArray(Enumerate(path, searchPattern, searchOption == SearchOption.AllDirectories, true, false, true));
        public static string[] GetDirectories(string path) => ToArray(Enumerate(path, "*", false, false, true, true));
        public static string[] GetDirectories(string path, string searchPattern) => ToArray(Enumerate(path, searchPattern, false, false, true, true));
        public static string[] GetDirectories(string path, string searchPattern, SearchOption searchOption) =>
            ToArray(Enumerate(path, searchPattern, searchOption == SearchOption.AllDirectories, false, true, true));
        public static string[] GetFileSystemEntries(string path) => ToArray(Enumerate(path, "*", false, true, true, true));
        public static string[] GetFileSystemEntries(string path, string searchPattern) => ToArray(Enumerate(path, searchPattern, false, true, true, true));
        public static string[] GetFileSystemEntries(string path, string searchPattern, SearchOption searchOption) =>
            ToArray(Enumerate(path, searchPattern, searchOption == SearchOption.AllDirectories, true, true, true));

        public static IEnumerable<string> EnumerateFiles(string path) => Enumerate(path, "*", false, true, false, true);
        public static IEnumerable<string> EnumerateFiles(string path, string searchPattern) => Enumerate(path, searchPattern, false, true, false, true);
        public static IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
            Enumerate(path, searchPattern, searchOption == SearchOption.AllDirectories, true, false, true);
        public static IEnumerable<string> EnumerateDirectories(string path) => Enumerate(path, "*", false, false, true, true);
        public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern) => Enumerate(path, searchPattern, false, false, true, true);
        public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) =>
            Enumerate(path, searchPattern, searchOption == SearchOption.AllDirectories, false, true, true);
        public static IEnumerable<string> EnumerateFileSystemEntries(string path) => Enumerate(path, "*", false, true, true, true);
        public static IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern) => Enumerate(path, searchPattern, false, true, true, true);

        private static string[] ToArray(List<string> list) => list.ToArray();

        /// <summary>
        /// Lists entries of <paramref name="path"/> (results keep the caller's path prefix, as in .NET).
        /// Order is readdir order, like .NET on Unix.
        /// </summary>
        internal static List<string> Enumerate(string path, string pattern, bool recursive, bool files, bool dirs, bool checkPath)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (pattern == null) throw new ArgumentNullException("searchPattern");
            var result = new List<string>();
            if (checkPath && !Posix.IsDirectory(path))
            {
                if (Posix.FileType(path) != 0) throw new IOException("The directory name is invalid. : '" + PathImpl.GetFullPath(path) + "'");
                throw new DirectoryNotFoundException("Could not find a part of the path '" + PathImpl.GetFullPath(path) + "'.");
            }
            Walk(path, pattern, recursive, files, dirs, result);
            return result;
        }

        private static void Walk(string dir, string pattern, bool recursive, bool files, bool dirs, List<string> result)
        {
            byte* p = Utf8.ToNative(dir.Length == 0 ? "." : dir);
            void* handle = Posix.opendir(p);
            int errno = Posix.Errno;
            Libc.free(p);
            if (handle == null) throw Posix.IOError(errno, dir, true);
            var subdirs = recursive ? new List<string>() : null;
            try
            {
                while (true)
                {
                    byte* entry = Posix.readdir(handle);
                    if (entry == null) break;
                    byte* name = entry + Posix.DirentNameOffset;
                    if (name[0] == '.' && (name[1] == 0 || name[1] == '.' && name[2] == 0)) continue;
                    string fileName = Utf8.FromNative(name);
                    string full = PathImpl.Join(dir, fileName);
                    byte type = entry[Posix.DirentTypeOffset];
                    bool isDir = type == Posix.DT_DIR || (type == Posix.DT_UNKNOWN || type == Posix.DT_LNK) && Posix.IsDirectory(full);
                    if ((isDir ? dirs : files) && MatchPattern(fileName, pattern)) result.Add(full);
                    if (isDir && recursive && type != Posix.DT_LNK) subdirs.Add(full);
                }
            }
            finally { Posix.closedir(handle); }
            if (subdirs != null)
                foreach (var sub in subdirs) Walk(sub, pattern, true, files, dirs, result);
        }

        /// <summary>Win32-style wildcard match ('*', '?'); "*.*" and "*" match everything.</summary>
        internal static bool MatchPattern(string name, string pattern)
        {
            if (pattern == "*" || pattern == "*.*" || pattern.Length == 0) return true;
            return Match(name, 0, pattern, 0);
        }

        private static bool Match(string s, int si, string p, int pi)
        {
            while (pi < p.Length)
            {
                char c = p[pi];
                if (c == '*')
                {
                    while (pi < p.Length && p[pi] == '*') pi++;
                    if (pi == p.Length) return true;
                    for (int k = si; k <= s.Length; k++) if (Match(s, k, p, pi)) return true;
                    return false;
                }
                if (si >= s.Length) return false;
                if (c != '?' && c != s[si]) return false;
                si++; pi++;
            }
            return si == s.Length;
        }
    }

    [Shadows("System.IO.FileSystemInfo")]
    public abstract class FileSystemInfoImpl
    {
        protected string FullPath;
        protected string OriginalPath;

        public virtual string FullName => FullPath;
        public abstract string Name { get; }
        public abstract bool Exists { get; }
        public string Extension => PathImpl.GetExtension(FullPath);
        public DateTime LastWriteTimeUtc => FileImpl.GetLastWriteTimeUtc(FullPath);
        public DateTime LastWriteTime => FileImpl.GetLastWriteTime(FullPath);
        public abstract void Delete();
        public void Refresh() { }
        public override string ToString() => OriginalPath;
    }

    [Shadows("System.IO.FileInfo")]
    public sealed class FileInfoImpl : FileSystemInfoImpl
    {
        public FileInfoImpl(string fileName)
        {
            if (fileName == null) throw new ArgumentNullException("fileName");
            OriginalPath = fileName;
            FullPath = PathImpl.GetFullPath(fileName);
        }

        public override string Name => PathImpl.GetFileName(FullPath);
        public override bool Exists => Posix.IsFile(FullPath);
        public long Length => FileImpl.Length(FullPath);
        public string DirectoryName => PathImpl.GetDirectoryName(FullPath);
        public DirectoryInfo Directory => new DirectoryInfo(DirectoryName);
        public bool IsReadOnly => false;
        public override void Delete() => FileImpl.Delete(FullPath);
        public FileStream Open(FileMode mode) => new FileStream(FullPath, mode);
        public FileStream Open(FileMode mode, FileAccess access) => new FileStream(FullPath, mode, access);
        public FileStream OpenRead() => new FileStream(FullPath, FileMode.Open, FileAccess.Read);
        public FileStream OpenWrite() => new FileStream(FullPath, FileMode.OpenOrCreate, FileAccess.Write);
        public FileStream Create() => new FileStream(FullPath, FileMode.Create, FileAccess.ReadWrite);
        public StreamReader OpenText() => new StreamReader(FullPath);
        public StreamWriter CreateText() => new StreamWriter(FullPath, false);
        public StreamWriter AppendText() => new StreamWriter(FullPath, true);
        public FileInfo CopyTo(string destFileName) => CopyTo(destFileName, false);
        public FileInfo CopyTo(string destFileName, bool overwrite) { FileImpl.Copy(FullPath, destFileName, overwrite); return new FileInfo(destFileName); }
        public void MoveTo(string destFileName) => MoveTo(destFileName, false);
        public void MoveTo(string destFileName, bool overwrite)
        {
            FileImpl.Move(FullPath, destFileName, overwrite);
            OriginalPath = destFileName;
            FullPath = PathImpl.GetFullPath(destFileName);
        }
    }

    [Shadows("System.IO.DirectoryInfo")]
    public sealed class DirectoryInfoImpl : FileSystemInfoImpl
    {
        public DirectoryInfoImpl(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            OriginalPath = path;
            FullPath = PathImpl.GetFullPath(path);
        }

        public override string Name
        {
            get
            {
                string trimmed = PathImpl.TrimEndingDirectorySeparator(FullPath);
                return trimmed == "/" ? "/" : PathImpl.GetFileName(trimmed);
            }
        }

        public override bool Exists => Posix.IsDirectory(FullPath);
        public DirectoryInfo Parent
        {
            get
            {
                string parent = PathImpl.GetDirectoryName(PathImpl.TrimEndingDirectorySeparator(FullPath));
                return parent == null ? null : new DirectoryInfo(parent);
            }
        }
        public DirectoryInfo Root => new DirectoryInfo("/");
        public void Create() => DirectoryImpl.CreateDirectory(FullPath);
        public DirectoryInfo CreateSubdirectory(string path) => DirectoryImpl.CreateDirectory(PathImpl.Combine(FullPath, path));
        public override void Delete() => DirectoryImpl.Delete(FullPath, false);
        public void Delete(bool recursive) => DirectoryImpl.Delete(FullPath, recursive);
        public void MoveTo(string destDirName)
        {
            DirectoryImpl.Move(FullPath, destDirName);
            OriginalPath = destDirName;
            FullPath = PathImpl.GetFullPath(destDirName);
        }

        public FileInfo[] GetFiles() => GetFiles("*", SearchOption.TopDirectoryOnly);
        public FileInfo[] GetFiles(string searchPattern) => GetFiles(searchPattern, SearchOption.TopDirectoryOnly);
        public FileInfo[] GetFiles(string searchPattern, SearchOption searchOption)
        {
            var names = DirectoryImpl.Enumerate(FullPath, searchPattern, searchOption == SearchOption.AllDirectories, true, false, true);
            var result = new FileInfo[names.Count];
            for (int i = 0; i < result.Length; i++) result[i] = new FileInfo(names[i]);
            return result;
        }

        public DirectoryInfo[] GetDirectories() => GetDirectories("*", SearchOption.TopDirectoryOnly);
        public DirectoryInfo[] GetDirectories(string searchPattern) => GetDirectories(searchPattern, SearchOption.TopDirectoryOnly);
        public DirectoryInfo[] GetDirectories(string searchPattern, SearchOption searchOption)
        {
            var names = DirectoryImpl.Enumerate(FullPath, searchPattern, searchOption == SearchOption.AllDirectories, false, true, true);
            var result = new DirectoryInfo[names.Count];
            for (int i = 0; i < result.Length; i++) result[i] = new DirectoryInfo(names[i]);
            return result;
        }

        public IEnumerable<FileInfo> EnumerateFiles() => GetFiles();
        public IEnumerable<FileInfo> EnumerateFiles(string searchPattern) => GetFiles(searchPattern);
        public IEnumerable<FileInfo> EnumerateFiles(string searchPattern, SearchOption searchOption) => GetFiles(searchPattern, searchOption);
        public IEnumerable<DirectoryInfo> EnumerateDirectories() => GetDirectories();
    }
}
