// The file system served from the framework's IL over csharpc's libSystem.Native contract (corelib/IO/SystemNative.cs):
// the entry points the common File/Directory/FileStream/RandomAccess/Path APIs reach, with their error mapping.
#pragma warning disable CA1416 // FileStream.Lock/Unlock: macOS throws PlatformNotSupportedException, recorded live there
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

string root = Path.Combine(Path.GetTempPath(), "csharpc-fs-contract-" + Environment.ProcessId);
string Show(string s) => s.Replace(root, "<root>");
void Try(string label, Action action)
{
    try { action(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().FullName + ": " + Show(e.Message)); }
}

Directory.CreateDirectory(root);
try
{
    string a = Path.Combine(root, "a.bin");

    // Preallocation reserves blocks without changing the length (fallocate FALLOC_FL_KEEP_SIZE).
    using (var fs = new FileStream(a, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, PreallocationSize = 1 << 20 }))
    {
        Console.WriteLine("preallocated length " + fs.Length);
        fs.Write(new byte[] { 1, 2, 3, 4, 5 });
        Console.WriteLine("written length " + fs.Length + " position " + fs.Position);
    }
    Console.WriteLine("file length " + new FileInfo(a).Length);

    // Scatter/gather at offsets (pwritev/preadv).
    using (var handle = File.OpenHandle(a, FileMode.Open, FileAccess.ReadWrite))
    {
        RandomAccess.Write(handle, new List<ReadOnlyMemory<byte>> { new byte[] { 10, 11 }, new byte[] { 12, 13, 14 } }, 3);
        var x = new byte[4];
        var y = new byte[10];
        long n = RandomAccess.Read(handle, new List<Memory<byte>> { x, y }, 0);
        Console.WriteLine("vectored read " + n + ": " + string.Join(",", x) + " | " + string.Join(",", y.Take((int)n - 4)));
        Console.WriteLine("handle length " + RandomAccess.GetLength(handle));
        RandomAccess.SetLength(handle, 2);
        Console.WriteLine("truncated " + RandomAccess.GetLength(handle));
    }

    // Byte-range locks (fcntl F_SETLK) and FileShare (flock).
    using (var fs = new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
    {
        fs.Lock(0, 1);
        fs.Unlock(0, 1);
        Try("unlock twice", () => fs.Unlock(0, 1));
        Try("lock negative", () => fs.Lock(-1, 1));
    }
    using (var exclusive = new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Try("open while FileShare.None", () => new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read).Dispose());

    // Copy keeps the data, the modification time and the rwx bits, never setuid/setgid/sticky.
    File.WriteAllText(a, "copy me");
    File.SetLastWriteTimeUtc(a, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(a, UnixFileMode.SetUser | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherExecute);
    string b = Path.Combine(root, "b.bin");
    File.Copy(a, b);
    Console.WriteLine("copy: " + File.ReadAllText(b) + " " + File.GetLastWriteTimeUtc(b).ToString("o"));
    if (!OperatingSystem.IsWindows())
        Console.WriteLine("copy mode " + File.GetUnixFileMode(b));
    Try("copy onto existing", () => File.Copy(a, b));
    File.Copy(a, b, overwrite: true);

    // Enumeration (opendir/readdir) with a custom filter and transform.
    Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"));
    File.WriteAllText(Path.Combine(root, "sub", "c.txt"), "c");
    File.WriteAllText(Path.Combine(root, "sub", "deeper", "d.txt"), "dd");
    var entries = new FileSystemEnumerable<string>(root,
        (ref FileSystemEntry e) => (e.IsDirectory ? "dir " : "file ") + e.FileName.ToString() + " " + (e.IsDirectory ? 0 : e.Length),
        new EnumerationOptions { RecurseSubdirectories = true })
    { ShouldIncludePredicate = (ref FileSystemEntry e) => !e.FileName.StartsWith("b") };
    foreach (string e in entries.OrderBy(s => s, StringComparer.Ordinal)) Console.WriteLine("  " + e);
    Console.WriteLine("txt files " + Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories).Length);

    // Moves, renames and their errors.
    Directory.Move(Path.Combine(root, "sub"), Path.Combine(root, "moved"));
    Console.WriteLine("moved: " + Directory.Exists(Path.Combine(root, "moved", "deeper")) + " " + Directory.Exists(Path.Combine(root, "sub")));
    Try("move missing", () => Directory.Move(Path.Combine(root, "sub"), Path.Combine(root, "x")));
    Try("delete non-empty", () => Directory.Delete(Path.Combine(root, "moved")));
    Try("read a directory", () => File.ReadAllText(Path.Combine(root, "moved")));
    Try("open in missing dir", () => File.ReadAllText(Path.Combine(root, "nope", "f")));
    Try("create file under a file", () => File.WriteAllText(Path.Combine(a, "f"), ""));
    Try("create dir over a file", () => Directory.CreateDirectory(a));
    Try("FileMode.CreateNew on existing", () => new FileStream(a, FileMode.CreateNew).Dispose());

    // Links (symlink/readlink/lstat).
    if (!OperatingSystem.IsWindows())
    {
        string link = Path.Combine(root, "link");
        File.CreateSymbolicLink(link, a);
        var info = new FileInfo(link);
        Console.WriteLine("link target " + Show(info.LinkTarget ?? "null") + " resolved " + Show(info.ResolveLinkTarget(true)?.FullName ?? "null"));
        File.Delete(a);
        Console.WriteLine("dangling: exists " + File.Exists(link) + " target " + Show(new FileInfo(link).LinkTarget ?? "null"));
        Try("read dangling link", () => File.ReadAllText(link));
    }

    // Temporary files and directories (mkstemps/mkdtemp).
    string temp = Path.GetTempFileName();
    Console.WriteLine("temp file: " + File.Exists(temp) + " " + new FileInfo(temp).Length + " " + temp.StartsWith(Path.GetTempPath()));
    File.Delete(temp);
    DirectoryInfo tempDir = Directory.CreateTempSubdirectory("csharpc-");
    Console.WriteLine("temp dir: " + tempDir.Exists + " " + tempDir.Name.StartsWith("csharpc-"));
    if (!OperatingSystem.IsWindows()) Console.WriteLine("temp dir mode " + tempDir.UnixFileMode);
    tempDir.Delete();

    // Mount points (/proc/mounts).
    Console.WriteLine("logical drives include root: " + Directory.GetLogicalDrives().Contains(OperatingSystem.IsWindows() ? "C:\\" : "/"));
}
finally
{
    Directory.Delete(root, recursive: true);
}
Console.WriteLine("cleaned " + !Directory.Exists(root));
