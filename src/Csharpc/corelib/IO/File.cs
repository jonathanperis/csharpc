// System.IO.File / Directory / FileInfo / DirectoryInfo over libc (Unix semantics as in .NET).
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.File")]
    internal static unsafe class FileImpl
    {
        public static bool Exists(string path) => !string.IsNullOrEmpty(path) && Posix.IsFile(path);

        public static void SetUnixFileMode(string path, UnixFileMode mode)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            FileStatusImpl.SetUnixFileMode(path, mode);
        }

        public static UnixFileMode GetUnixFileMode(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            return FileStatusImpl.GetUnixFileMode(path);
        }

        public static FileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            ArgumentException.ThrowIfNullOrEmpty(pathToTarget);
            FileStatusImpl.CreateSymbolicLink(path, pathToTarget);
            return new FileInfo(path);
        }

        public static FileStream Open(string path, FileMode mode) => new FileStream(path, mode);
        public static FileStream Open(string path, FileMode mode, FileAccess access) => new FileStream(path, mode, access);
        public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) => new FileStream(path, mode, access, share);
        public static FileStream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read);
        public static FileStream OpenWrite(string path) => new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
        public static FileStream Create(string path) => new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        // Stack traces: the frames .NET 11 lists below File's text readers when opening fails, on Unix
        // (CoreIntrinsics.AppendLibraryFrames, docs/adr/0012-stack-traces.md).
        private const string OpenReaderFrames = "Interop.ThrowExceptionForIoErrno(ErrorInfo errorInfo, String path, Boolean isDirError)\nMicrosoft.Win32.SafeHandles.SafeFileHandle.Open(String path, OpenFlags flags, Int32 mode, Boolean failForSymlink, Boolean& wasSymlink, Func`4 createOpenException)\nMicrosoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, UnixFileMode openPermissions, Int64& fileLength, UnixFileMode& filePermissions, Boolean failForSymlink, Boolean& wasSymlink, Func`4 createOpenException)\nSystem.IO.Strategies.OSFileStreamStrategy..ctor(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)\nSystem.IO.StreamReader.ValidateArgsAndOpenPath(String path, Int32 bufferSize)";

        public static StreamReader OpenText(string path)
        {
            StreamReader reader;
#if CSHARPC_TARGET_WINDOWS
            reader = new StreamReader(path);
#else
            try { reader = new StreamReader(path); }
            catch (IOException e) when (CoreIntrinsics.AppendLibraryFrames(e, OpenReaderFrames + "\nSystem.IO.File.OpenText(String path)")) { throw; }
            catch (UnauthorizedAccessException e) when (CoreIntrinsics.AppendLibraryFrames(e, OpenReaderFrames + "\nSystem.IO.File.OpenText(String path)")) { throw; }
#endif
            return reader;
        }
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
            StreamReader reader;
#if CSHARPC_TARGET_WINDOWS
            reader = new StreamReader(path, encoding, true);
#else
            try { reader = new StreamReader(path, encoding, true); }
            catch (IOException e) when (CoreIntrinsics.AppendLibraryFrames(e, OpenReaderFrames + "\nSystem.IO.File.ReadAllText(String path, Encoding encoding)")) { throw; }
            catch (UnauthorizedAccessException e) when (CoreIntrinsics.AppendLibraryFrames(e, OpenReaderFrames + "\nSystem.IO.File.ReadAllText(String path, Encoding encoding)")) { throw; }
#endif
            using (reader) return reader.ReadToEnd();
        }

        public static string[] ReadAllLines(string path) => ReadAllLines(path, new UTF8Encoding(false));

        public static string[] ReadAllLines(string path, Encoding encoding)
        {
            var lines = new List<string>();
            StreamReader opened;
#if CSHARPC_TARGET_WINDOWS
            opened = new StreamReader(path, encoding, true);
#else
            try { opened = new StreamReader(path, encoding, true); }
            catch (IOException e) when (CoreIntrinsics.AppendLibraryFrames(e, OpenReaderFrames + "\nSystem.IO.File.ReadAllLines(String path, Encoding encoding)")) { throw; }
            catch (UnauthorizedAccessException e) when (CoreIntrinsics.AppendLibraryFrames(e, OpenReaderFrames + "\nSystem.IO.File.ReadAllLines(String path, Encoding encoding)")) { throw; }
#endif
            using (var reader = opened)
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

        // The token and encoding overloads: a token canceled up front gives a canceled task, as .NET's; the
        // work itself runs synchronously like the overloads above.
        public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) => ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        public static Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
        {
            Validate(path, encoding);
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled<string>(cancellationToken) : AsyncText.Run(() => ReadAllText(path, encoding));
        }
        public static Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken) => ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);
        public static Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
        {
            Validate(path, encoding);
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled<string[]>(cancellationToken) : AsyncText.Run(() => ReadAllLines(path, encoding));
        }
        public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
        {
            Validate(path, Encoding.UTF8);
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled<byte[]>(cancellationToken) : AsyncText.Run(() => ReadAllBytes(path));
        }
        public static Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken) =>
            WriteAllTextAsync(path, contents, new UTF8Encoding(false), cancellationToken);
        public static Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default)
        {
            Validate(path, encoding);
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : AsyncText.Run(() => WriteAllText(path, contents, encoding));
        }
        public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken) =>
            WriteAllLinesAsync(path, contents, new UTF8Encoding(false), cancellationToken);
        public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default)
        {
            Validate(path, encoding);
            if (contents == null) throw new ArgumentNullException("contents");
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : AsyncText.Run(() => WriteAllLines(path, contents, encoding));
        }
        public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            Validate(path, Encoding.UTF8);
            if (bytes == null) throw new ArgumentNullException("bytes");
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : AsyncText.Run(() => WriteAllBytes(path, bytes));
        }
        public static Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken) =>
            AppendAllTextAsync(path, contents, new UTF8Encoding(false), cancellationToken);
        public static Task AppendAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default)
        {
            Validate(path, encoding);
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : AsyncText.Run(() => AppendAllText(path, contents, encoding));
        }

        // .NET's ArgumentException.ThrowIfNullOrEmpty(path) and ArgumentNullException.ThrowIfNull(encoding), thrown synchronously.
        private static void Validate(string path, Encoding encoding)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            if (encoding == null) throw new ArgumentNullException("encoding");
        }

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

        // CreationTime as .NET reports it per OS (Posix.CreationTime): the birth time on macOS and Windows,
        // the older of mtime and ctime on Linux; a missing file gives 1601-01-01 like the other times.
        public static DateTime GetCreationTimeUtc(string path)
        {
            byte* st = stackalloc byte[Posix.StatSize];
            byte* p = Utf8.ToNative(path);
            int r = Posix.stat(p, st);
            Libc.free(p);
            if (r != 0) return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Posix.CreationTime(st, out long seconds, out long nanoseconds);
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(seconds * 10000000 + nanoseconds / 100);
        }

        public static DateTime GetCreationTime(string path) => GetCreationTimeUtc(path).ToLocalTime();

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
                if (errno != Posix.ERANGE) throw Posix.IOError(errno, ".", true);
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
            if (errno == Posix.ENOTEMPTY || errno == Posix.EEXIST) throw Posix.DirectoryNotEmpty(full);
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
        public DateTime CreationTimeUtc => FileImpl.GetCreationTimeUtc(FullPath);
        public DateTime CreationTime => FileImpl.GetCreationTime(FullPath);
        public abstract void Delete();
        public void Refresh() { }
        public override string ToString() => OriginalPath;

        /// <summary>The attributes .NET reports (FileStatusImpl); -1 when the entry doesn't exist.</summary>
        public FileAttributes Attributes => FileStatusImpl.GetAttributes(FullPath, Name);

        /// <summary>The target of a symbolic link as stored in the link, or null when this is not a link.</summary>
        public string LinkTarget => FileStatusImpl.ReadLink(FullPath);

        public FileSystemInfo ResolveLinkTarget(bool returnFinalTarget) =>
            FileStatusImpl.ResolveLinkTarget(FullPath, returnFinalTarget, this is DirectoryInfoImpl);
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

        public FileSystemInfo[] GetFileSystemInfos() => GetFileSystemInfos("*", SearchOption.TopDirectoryOnly);
        public FileSystemInfo[] GetFileSystemInfos(string searchPattern) => GetFileSystemInfos(searchPattern, SearchOption.TopDirectoryOnly);
        public FileSystemInfo[] GetFileSystemInfos(string searchPattern, SearchOption searchOption)
        {
            if (searchOption != SearchOption.TopDirectoryOnly && searchOption != SearchOption.AllDirectories)
                throw new ArgumentOutOfRangeException("searchOption", "Enum value was out of legal range.");
            // Entries in directory order, each a DirectoryInfo or FileInfo by its kind (readdir's, links followed).
            var names = DirectoryImpl.Enumerate(FullPath, searchPattern, searchOption == SearchOption.AllDirectories, true, true, true);
            var result = new FileSystemInfo[names.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = Posix.IsDirectory(names[i]) ? new DirectoryInfo(names[i]) : new FileInfo(names[i]);
            return result;
        }

        public IEnumerable<FileSystemInfo> EnumerateFileSystemInfos() => GetFileSystemInfos();
        public IEnumerable<FileSystemInfo> EnumerateFileSystemInfos(string searchPattern) => GetFileSystemInfos(searchPattern);
        public IEnumerable<FileSystemInfo> EnumerateFileSystemInfos(string searchPattern, SearchOption searchOption) => GetFileSystemInfos(searchPattern, searchOption);
    }
}
