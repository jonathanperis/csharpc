// FileSystemInfo.Attributes as .NET reports them on Unix (ReadOnly from the permission bits, Hidden
// for dot names, Directory, ReparsePoint for links), LinkTarget/ResolveLinkTarget, file system infos
// of a directory, and FileStream's FileOptions (DeleteOnClose, Asynchronous, invalid values).
using System;
using System.IO;
using System.Linq;

class Program
{
    static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "csharpc-605-" + Environment.ProcessId);
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "data.txt");
            File.WriteAllText(file, "hello");
            var hidden = Path.Combine(root, ".hidden");
            File.WriteAllText(hidden, "x");
            var sub = Directory.CreateDirectory(Path.Combine(root, "sub"));
            Console.WriteLine(new FileInfo(file).Attributes);
            Console.WriteLine(new FileInfo(hidden).Attributes);
            Console.WriteLine(sub.Attributes);
            Console.WriteLine((int)new FileInfo(Path.Combine(root, "missing")).Attributes);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                Console.WriteLine(new FileInfo(file).Attributes);
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var link = Path.Combine(root, "link");
                File.CreateSymbolicLink(link, "data.txt");
                var info = new FileInfo(link);
                Console.WriteLine($"{info.Attributes} {info.LinkTarget} {new FileInfo(file).LinkTarget == null}");
                Console.WriteLine(Path.GetFileName(info.ResolveLinkTarget(false)!.FullName) + " " + Path.GetFileName(info.ResolveLinkTarget(true)!.FullName));
                File.CreateSymbolicLink(Path.Combine(root, "dangling"), "nowhere");
                Console.WriteLine(new FileInfo(Path.Combine(root, "dangling")).Attributes);
            }
            var names = new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                .Select(i => (i is DirectoryInfo ? "d:" : "f:") + i.Name).OrderBy(n => n, StringComparer.Ordinal);
            Console.WriteLine(string.Join(" ", names));

            var temp = Path.Combine(root, "temp.bin");
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose | FileOptions.Asynchronous))
            {
                stream.WriteByte(1);
                Console.WriteLine(File.Exists(temp));
            }
            Console.WriteLine(File.Exists(temp));
            try { new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1, (FileOptions)1); }
            catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.ParamName} {File.Exists(temp)}"); }
            using (var read = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan))
                Console.WriteLine(read.ReadByte());
        }
        finally { Directory.Delete(root, true); }
    }
}
