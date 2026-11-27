// Streams and text readers/writers throughput, the program behind the NativeAOT column of docs/il-first/System.IO.md
// (tools/il-first/measure-aot.sh): MemoryStream growth, StreamWriter/StreamReader (UTF-8) lines, BinaryWriter/BinaryReader primitives
// and strings, BufferedStream over a MemoryStream, StringWriter/StringReader. The output is checksums only.
using System;
using System.IO;
using System.Text;

long check = 0;
for (int round = 0; round < 20; round++)
{
    var ms = new MemoryStream();
    using (var writer = new StreamWriter(ms, new UTF8Encoding(false), 4096, leaveOpen: true))
        for (int i = 0; i < 100_000; i++) { writer.Write("line "); writer.Write(i); writer.Write(' '); writer.WriteLine(i * 0.25); }
    check += ms.Length;
    ms.Position = 0;
    using (var reader = new StreamReader(ms, Encoding.UTF8, false, 4096, leaveOpen: true))
    {
        string? line;
        while ((line = reader.ReadLine()) != null) check += line.Length + line[line.Length - 1];
    }

    var bin = new MemoryStream();
    using (var bw = new BinaryWriter(bin, Encoding.UTF8, leaveOpen: true))
        for (int i = 0; i < 100_000; i++) { bw.Write(i); bw.Write((long)i * round); bw.Write(i * 1.5); if (i % 16 == 0) bw.Write("s" + i); }
    bin.Position = 0;
    using (var br = new BinaryReader(bin, Encoding.UTF8, leaveOpen: true))
        for (int i = 0; i < 100_000; i++) { check += br.ReadInt32() + br.ReadInt64() + (long)br.ReadDouble(); if (i % 16 == 0) check += br.ReadString().Length; }

    var buffered = new BufferedStream(new MemoryStream(), 1024);
    var chunk = new byte[37];
    for (int i = 0; i < 50_000; i++) { chunk[i % 37] = (byte)i; buffered.Write(chunk, 0, 1 + i % 37); }
    buffered.Flush();
    check += buffered.Length;

    var sw = new StringWriter();
    for (int i = 0; i < 50_000; i++) { sw.Write(i); sw.Write(','); }
    var sr = new StringReader(sw.ToString());
    int c;
    while ((c = sr.Read()) != -1) check += c;
}
Console.WriteLine(check);
