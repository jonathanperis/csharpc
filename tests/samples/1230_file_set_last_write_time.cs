// File.SetLastWriteTime[Utc] and Directory.SetLastWriteTime[Utc]: the modification time round-trips exactly
// (100 ns ticks, a pre-1980 and a post-2038 instant included), the contents are untouched, a missing file is a
// FileNotFoundException, and the local-time overloads convert as .NET does. ZipFile and Tar extraction restore
// entry times through these.
using System;
using System.IO;

string root = Path.Combine(Path.GetTempPath(), "csharpc_write_time_" + Environment.ProcessId);
Directory.CreateDirectory(root);
try
{
    string file = Path.Combine(root, "a.txt");
    File.WriteAllText(file, "payload");

    var instants = new[]
    {
        new DateTime(2009, 2, 13, 23, 31, 30, DateTimeKind.Utc).AddTicks(1234567),
        new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(1969, 7, 20, 20, 17, 40, DateTimeKind.Utc),
        new DateTime(2040, 12, 31, 23, 59, 58, DateTimeKind.Utc),
    };
    foreach (var when in instants)
    {
        File.SetLastWriteTimeUtc(file, when);
        var back = File.GetLastWriteTimeUtc(file);
        Console.WriteLine($"file {when:O} -> {back:O} {back == when} {back.Kind}");
    }
    Console.WriteLine($"contents: {File.ReadAllText(file)}");

    string dir = Path.Combine(root, "sub");
    Directory.CreateDirectory(dir);
    var dirTime = new DateTime(2001, 9, 9, 1, 46, 40, DateTimeKind.Utc);
    Directory.SetLastWriteTimeUtc(dir, dirTime);
    Console.WriteLine($"directory {Directory.GetLastWriteTimeUtc(dir):O}");

    var local = new DateTime(2021, 6, 15, 12, 0, 0, DateTimeKind.Local);
    File.SetLastWriteTime(file, local);
    Console.WriteLine($"local round trip: {File.GetLastWriteTime(file) == local} {File.GetLastWriteTimeUtc(file) == local.ToUniversalTime()}");
    Directory.SetLastWriteTime(dir, local);
    Console.WriteLine($"directory local round trip: {Directory.GetLastWriteTime(dir) == local}");

    var info = new FileInfo(file);
    Console.WriteLine($"FileInfo: {info.LastWriteTimeUtc == local.ToUniversalTime()}");

    try { File.SetLastWriteTimeUtc(Path.Combine(root, "missing.txt"), instants[0]); }
    catch (Exception e) { Console.WriteLine($"missing: {e.GetType().Name}"); }
    try { File.SetLastWriteTimeUtc((string)null!, instants[0]); }
    catch (Exception e) { Console.WriteLine($"null: {e.GetType().Name}"); }
}
finally
{
    Directory.Delete(root, true);
}
