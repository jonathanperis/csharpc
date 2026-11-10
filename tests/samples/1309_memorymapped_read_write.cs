// Read-write memory-mapped files: CreateFromFile with MemoryMappedFileAccess.ReadWrite grows the file to the capacity, a
// ReadWrite view accessor reads and writes structs, scalars and arrays (UnmanagedMemoryAccessor), the writes reach the file,
// and the accessor's bounds, access and disposal checks throw .NET's exceptions.
using System;
using System.IO;
using System.IO.MemoryMappedFiles;

struct Checkpoint { public ulong ItemId; public short Kind; public int Count; }

static class Program
{
    static void Try(string label, Action action)
    {
        try { action(); Console.WriteLine(label + ": ok"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var path = Path.Combine(Path.GetTempPath(), "csharpc-mmap-sample-" + Environment.ProcessId + ".bin");
        File.Delete(path);
        try
        {
            using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            using (var file = MemoryMappedFile.CreateFromFile(stream, null, 64, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false))
            using (var view = file.CreateViewAccessor(0, 64, MemoryMappedFileAccess.ReadWrite))
            {
                Console.WriteLine($"file {new FileInfo(path).Length} capacity {view.Capacity} read {view.CanRead} write {view.CanWrite}");
                var checkpoint = new Checkpoint { ItemId = 0x1122334455667788, Kind = 3, Count = -5 };
                view.Write(0, ref checkpoint);
                view.Write(24, 0x7FFFFFFF);
                view.Write(28, (byte)9);
                view.Write(30, (short)-2);
                view.Write(32, 1.5);
                view.WriteArray(40, new int[] { 10, 20, 30 }, 0, 3);
                view.Read(0, out Checkpoint back);
                Console.WriteLine($"{back.ItemId:X} {back.Kind} {back.Count} {view.ReadInt32(24)} {view.ReadByte(28)} {view.ReadInt16(30)} {view.ReadDouble(32) == 1.5}");
                var ints = new int[5];
                Console.WriteLine($"{view.ReadArray(40, ints, 1, 3)} [{string.Join(",", ints)}]");
                Console.WriteLine($"{view.ReadArray(56, ints, 0, 5)} [{string.Join(",", ints)}]");
                Try("write past the end", () => view.Write(60, 1L));
                Try("read at the capacity", () => view.ReadInt32(64));
                Try("negative position", () => view.ReadByte(-1));
                Try("struct past the end", () => view.Read(56, out Checkpoint _));
                Try("struct write past the end", () => view.Write(56, ref checkpoint));
                Try("array past the end", () => view.WriteArray(60, new int[2], 0, 2));
                Try("array at the capacity", () => view.ReadArray(64, ints, 0, 1));
                Try("array null", () => view.ReadArray(0, (int[])null!, 0, 1));
                Try("array count", () => view.ReadArray(0, ints, 4, 2));
            }

            var bytes = File.ReadAllBytes(path);
            Console.WriteLine($"on disk {bytes.Length}: {bytes[0]:X2} {bytes[7]:X2} {bytes[8]} {bytes[24]:X2} {bytes[28]} {bytes[40]} {bytes[44]}");

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var file = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false))
            {
                var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                Console.WriteLine($"read-only: {view.ReadInt32(24)} write {view.CanWrite}");
                Try("write to a read view", () => view.Write(0, 1));
                view.Dispose();
                Console.WriteLine($"disposed: read {view.CanRead}");
                Try("read after dispose", () => view.ReadInt32(0));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
