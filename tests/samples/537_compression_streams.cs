// System.IO.Compression compiled from the shared framework's IL, its zlib-ng/brotli calls bound to .NET's
// own native shim from the NativeAOT runtime pack: the compressed bytes are .NET's
// (docs-core-whats-new-dotnet-core-2-1, docs-standard-io-zip-tar-best-practices).
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

byte[] data = Encoding.UTF8.GetBytes(string.Join(" ", Enumerable.Range(0, 400).Select(i => $"line {i % 37} of the sample text")));
Console.WriteLine($"input {data.Length} bytes");

foreach (var level in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.SmallestSize, CompressionLevel.NoCompression })
{
    Console.WriteLine($"{level}: deflate {Compress(s => new DeflateStream(s, level, leaveOpen: true))}, gzip {Compress(s => new GZipStream(s, level, leaveOpen: true))}, " +
        $"zlib {Compress(s => new ZLibStream(s, level, leaveOpen: true))}, brotli {Compress(s => new BrotliStream(s, level, leaveOpen: true))}");
}

string Compress(Func<Stream, Stream> create)
{
    var compressed = new MemoryStream();
    using (var z = create(compressed)) z.Write(data, 0, data.Length);
    byte[] bytes = compressed.ToArray();
    string head = Convert.ToHexString(bytes, 0, Math.Min(6, bytes.Length));
    return $"{bytes.Length} ({head}...)";
}

// Round trips, through each decompressor and CopyTo.
var gz = new MemoryStream();
using (var z = new GZipStream(gz, CompressionMode.Compress, leaveOpen: true)) z.Write(data);
gz.Position = 0;
using (var z = new GZipStream(gz, CompressionMode.Decompress))
{
    var back = new MemoryStream();
    z.CopyTo(back);
    Console.WriteLine($"gzip round trip: {back.ToArray().AsSpan().SequenceEqual(data)}");
}
var br = new MemoryStream();
using (var z = new BrotliStream(br, CompressionMode.Compress, leaveOpen: true)) z.Write(data);
br.Position = 0;
using (var z = new BrotliStream(br, CompressionMode.Decompress))
{
    var reader = new StreamReader(z);
    Console.WriteLine($"brotli round trip: {reader.ReadToEnd() == Encoding.UTF8.GetString(data)}");
}
byte[] encoded = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
Console.WriteLine($"BrotliEncoder.TryCompress: {BrotliEncoder.TryCompress(data, encoded, out int written)} {written}");
byte[] decoded = new byte[data.Length];
Console.WriteLine($"BrotliDecoder.TryDecompress: {BrotliDecoder.TryDecompress(encoded.AsSpan(0, written), decoded, out int read)} {read}");

// A bad stream is an InvalidDataException, as in .NET.
try { new DeflateStream(new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }), CompressionMode.Decompress).ReadByte(); }
catch (InvalidDataException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

// Zip archives.
var zipBytes = new MemoryStream();
using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
{
    foreach (var (name, text) in new[] { ("readme.txt", "Hello, zip!"), ("docs/notes.md", string.Concat(Enumerable.Repeat("notes ", 50))), ("empty.bin", "") })
    {
        var entry = zip.CreateEntry(name, name.EndsWith(".md") ? CompressionLevel.SmallestSize : CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2024, 5, 6, 7, 8, 10, TimeSpan.Zero);
        using var w = new StreamWriter(entry.Open());
        w.Write(text);
    }
}
Console.WriteLine($"zip archive: {zipBytes.Length} bytes");
zipBytes.Position = 0;
using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Read))
{
    foreach (var e in zip.Entries)
    {
        using var r = new StreamReader(e.Open());
        string content = r.ReadToEnd();
        Console.WriteLine($"  {e.FullName} name={e.Name} length={e.Length} compressed={e.CompressedLength} crc={e.Crc32:X8} {e.LastWriteTime:yyyy-MM-dd HH:mm:ss} \"{(content.Length > 20 ? content[..20] + "..." : content)}\"");
    }
    Console.WriteLine($"  GetEntry: {zip.GetEntry("docs/notes.md")?.Length} {zip.GetEntry("missing") is null}");
}
