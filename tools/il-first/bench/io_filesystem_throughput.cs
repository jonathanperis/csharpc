// File-system throughput, the program behind the part-2 NativeAOT column of docs/il-first/System.IO.md (tools/il-first/measure-aot.sh):
// files written and read whole (File.WriteAllBytes/ReadAllText/ReadAllLines), FileStream sequential writes and reads with small and large
// buffers, RandomAccess at offsets, metadata (File.Exists, FileInfo.Length, Directory.Exists), directory creation, enumeration and
// recursive deletion, File.Copy and File.Move. Every call goes through the libSystem.Native contract (corelib/IO/SystemNative.cs) in the
// IL build. The output is checksums only.
using System;
using System.IO;
using System.Text;

string root = Path.Combine(Path.GetTempPath(), "csharpc-fsbench-" + Environment.ProcessId);
long check = 0;
var block = new byte[64 * 1024];
for (int i = 0; i < block.Length; i++) block[i] = (byte)(i * 31);
var small = new byte[100];
var lines = new StringBuilder();
for (int i = 0; i < 200; i++) lines.Append("line ").Append(i).Append('\n');
string text = lines.ToString();

for (int round = 0; round < 10; round++)
{
    Directory.CreateDirectory(root);
    // Many small files: create, stat, read, copy, move.
    for (int i = 0; i < 300; i++)
    {
        string dir = Path.Combine(root, "d" + (i % 10));
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "f" + i + ".txt");
        File.WriteAllText(f, text);
        if (File.Exists(f)) check += new FileInfo(f).Length;
        check += File.ReadAllLines(f).Length + File.ReadAllText(f).Length;
        if (i % 3 == 0) { File.Copy(f, f + ".copy"); check += File.ReadAllBytes(f + ".copy").Length; }
        if (i % 5 == 0) File.Move(f, f + ".moved");
    }
    // Enumeration.
    foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) check += Path.GetFileName(f).Length;
    foreach (var d in new DirectoryInfo(root).EnumerateDirectories()) check += d.Name.Length + (Directory.Exists(d.FullName) ? 1 : 0);

    // One large file: FileStream sequential writes (64 KiB and 100-byte chunks) and reads, then RandomAccess.
    string big = Path.Combine(root, "big.bin");
    using (var fs = new FileStream(big, FileMode.Create, FileAccess.Write, FileShare.None, 4096))
    {
        for (int i = 0; i < 256; i++) fs.Write(block, 0, block.Length);
        for (int i = 0; i < 20_000; i++) { small[i % small.Length] = (byte)i; fs.Write(small, 0, small.Length); }
    }
    using (var fs = new FileStream(big, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
    {
        int n;
        var buffer = new byte[16 * 1024];
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0) check += n + buffer[n - 1];
        fs.Position = 0;
        for (int i = 0; i < 20_000; i++) check += fs.ReadByte();
    }
    using (var handle = File.OpenHandle(big))
    {
        long length = RandomAccess.GetLength(handle);
        var buffer = new byte[4096];
        for (long offset = 0; offset < length; offset += 997 * 4096 / 7) check += RandomAccess.Read(handle, buffer, offset) + buffer[0];
    }
    check += new FileInfo(big).Length;
    Directory.Delete(root, recursive: true);
}
Console.WriteLine(check);
