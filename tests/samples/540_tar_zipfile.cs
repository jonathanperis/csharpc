// System.Formats.Tar and System.IO.Compression.ZipFile compiled from IL (their file metadata calls bound
// to .NET's libSystem.Native from the NativeAOT pack): archives in memory and on disk, with fixed
// times and modes so the output is the same everywhere (docs-standard-io-zip-tar-best-practices).
using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;

var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
var tar = new MemoryStream();
using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
{
    var file = new PaxTarEntry(TarEntryType.RegularFile, "dir/hello.txt") { ModificationTime = when, Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite };
    file.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("Hello, tar!"));
    writer.WriteEntry(file);
    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "dir/sub/") { ModificationTime = when });
    writer.WriteEntry(new UstarTarEntry(TarEntryType.SymbolicLink, "dir/link") { LinkName = "hello.txt", ModificationTime = when });
}
Console.WriteLine($"tar: {tar.Length} bytes");
tar.Position = 0;
using (var reader = new TarReader(tar))
{
    TarEntry? entry;
    while ((entry = reader.GetNextEntry()) is not null)
    {
        string data = entry.DataStream is { } s ? new StreamReader(s).ReadToEnd() : "";
        Console.WriteLine($"  {entry.EntryType} {entry.Name} {entry.Length} {entry.Format} {entry.ModificationTime:O} mode={Convert.ToString((int)entry.Mode, 8)} link='{entry.LinkName}' \"{data}\"");
    }
}

// ZipFile on disk: create from files, list, extract.
string root = Path.Combine(Path.GetTempPath(), "csharpc-540-" + Environment.ProcessId);
Directory.CreateDirectory(Path.Combine(root, "src", "nested"));
File.WriteAllText(Path.Combine(root, "src", "a.txt"), "alpha");
File.WriteAllText(Path.Combine(root, "src", "nested", "b.txt"), "beta beta beta");
File.SetLastWriteTimeUtc(Path.Combine(root, "src", "a.txt"), when.UtcDateTime);
File.SetLastWriteTimeUtc(Path.Combine(root, "src", "nested", "b.txt"), when.UtcDateTime);
string zipPath = Path.Combine(root, "out.zip");
using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
{
    zip.CreateEntryFromFile(Path.Combine(root, "src", "a.txt"), "a.txt");
    zip.CreateEntryFromFile(Path.Combine(root, "src", "nested", "b.txt"), "nested/b.txt", CompressionLevel.SmallestSize);
}
using (var zip = ZipFile.OpenRead(zipPath))
    foreach (var e in zip.Entries)
        Console.WriteLine($"  zip {e.FullName.Replace('\\', '/')} {e.Length} {e.CompressedLength}");
string extracted = Path.Combine(root, "extracted");
ZipFile.ExtractToDirectory(zipPath, extracted);
Console.WriteLine($"extracted: {File.ReadAllText(Path.Combine(extracted, "nested", "b.txt"))} {File.GetLastWriteTimeUtc(Path.Combine(extracted, "a.txt")):O}");
using (var zip = ZipFile.OpenRead(zipPath))
{
    var entry = zip.GetEntry("a.txt")!;
    string copy = Path.Combine(root, "copy.txt");
    entry.ExtractToFile(copy);
    Console.WriteLine($"ExtractToFile: {File.ReadAllText(copy)}");
    try { entry.ExtractToFile(copy); }
    catch (IOException) { Console.WriteLine("ExtractToFile again: IOException"); }
}
Directory.Delete(root, recursive: true);
