// File.GetAttributes/SetAttributes, FileSystemInfo.Attributes and FileInfo.IsReadOnly as setters (M6 survey, #169):
// on Unix ReadOnly is the write permission, a file without owner read permission is not made writable, and a missing
// file is FileNotFoundException (or DirectoryNotFoundException when its directory is missing too).
using System;
using System.IO;

static void Show(string label, Func<object> f)
{
    try { Console.WriteLine(label + ": " + f()); }
    catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name); }
}

// File attributes: ReadOnly is the write permission, and a missing file is FileNotFoundException.
string dir = Path.Combine(Path.GetTempPath(), "csharpc-attrs-" + Environment.ProcessId);
Directory.CreateDirectory(dir);
try
{
    string path = Path.Combine(dir, "a.txt");
    File.WriteAllText(path, "x");
    File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("644", 8));
    Console.WriteLine(File.GetAttributes(path) + " " + new FileInfo(path).IsReadOnly);
    File.SetAttributes(path, FileAttributes.ReadOnly);
    Console.WriteLine(File.GetAttributes(path) + " " + new FileInfo(path).IsReadOnly + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    var info = new FileInfo(path);
    info.IsReadOnly = false;
    Console.WriteLine(info.Attributes + " " + info.IsReadOnly + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    info.Attributes = FileAttributes.ReadOnly | FileAttributes.Archive;
    Console.WriteLine(info.Attributes + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    File.SetAttributes(path, FileAttributes.Normal);
    Console.WriteLine(File.GetAttributes(path) + " " + Convert.ToString((int)File.GetUnixFileMode(path), 8));
    // Without owner read permission nobody is granted write permission.
    File.SetUnixFileMode(path, UnixFileMode.GroupRead);
    File.SetAttributes(path, FileAttributes.Archive);
    Console.WriteLine(Convert.ToString((int)File.GetUnixFileMode(path), 8));
    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.WriteLine(File.GetAttributes(dir));
    File.SetAttributes(dir, FileAttributes.Directory);
    Console.WriteLine(Convert.ToString((int)File.GetUnixFileMode(dir) & 0x80, 8));
    Show("SetAttributes(missing)", () => { File.SetAttributes(Path.Combine(dir, "none"), FileAttributes.ReadOnly); return "no"; });
    Show("GetAttributes(missing)", () => File.GetAttributes(Path.Combine(dir, "none")));
    Show("GetAttributes(missing dir)", () => File.GetAttributes(Path.Combine(dir, "none", "x")));
    Show("SetAttributes(invalid)", () => { File.SetAttributes(path, (FileAttributes)0x40000000); return "no"; });
    Show("FileInfo.Attributes(missing)", () => (int)new FileInfo(Path.Combine(dir, "none")).Attributes);
    Show("FileInfo.IsReadOnly(missing)", () => new FileInfo(Path.Combine(dir, "none")).IsReadOnly);
    Show("FileInfo.IsReadOnly = (missing)", () => { new FileInfo(Path.Combine(dir, "none")).IsReadOnly = true; return "no"; });
}
finally { Directory.Delete(dir, true); }

