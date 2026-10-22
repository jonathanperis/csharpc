using System;
using System.IO;
using System.Text;

static string Hex(byte[] b)
{
    var sb = new StringBuilder();
    foreach (var x in b) { if (sb.Length > 0) sb.Append('-'); sb.Append(x.ToString("X2")); }
    return sb.ToString();
}

string text = "Aé€😀";
Console.WriteLine(Hex(Encoding.UTF8.GetBytes(text)));
Console.WriteLine(Hex(Encoding.Unicode.GetBytes(text)));
Console.WriteLine(Hex(Encoding.BigEndianUnicode.GetBytes(text)));
Console.WriteLine(Hex(Encoding.ASCII.GetBytes(text)));
Console.WriteLine(Hex(Encoding.Latin1.GetBytes(text)));
Console.WriteLine(Encoding.UTF8.GetByteCount(text) + " " + Encoding.Unicode.GetByteCount(text) + " " + Encoding.ASCII.GetByteCount(text));
Console.WriteLine(Encoding.UTF8.GetCharCount(Encoding.UTF8.GetBytes(text)));
Console.WriteLine(Hex(Encoding.UTF8.GetPreamble()) + " | " + Hex(Encoding.Unicode.GetPreamble()) + " | " + Hex(new UTF8Encoding(false).GetPreamble()));
Console.WriteLine(Encoding.UTF8.WebName + " " + Encoding.Unicode.WebName + " " + Encoding.ASCII.WebName + " " + Encoding.Latin1.WebName);
Console.WriteLine(Encoding.UTF8.GetString(new byte[] { 0x41, 0xFF, 0x42, 0xE2, 0x82 }).Length);
Console.WriteLine(Encoding.ASCII.GetString(new byte[] { 0x41, 0xC3, 0x42 }));
Console.WriteLine(Encoding.Latin1.GetString(new byte[] { 0x41, 0xE9 }));
Console.WriteLine(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes("round trip ✓"), 0, 10));
Console.WriteLine(Encoding.GetEncoding("utf-8").WebName + " " + Encoding.GetEncoding(1200).WebName);

// StreamReader detects BOMs; StreamWriter emits the encoding's preamble.
foreach (var enc in new Encoding[] { new UTF8Encoding(true), new UTF8Encoding(false), Encoding.Unicode, Encoding.BigEndianUnicode })
{
    var ms = new MemoryStream();
    using (var w = new StreamWriter(ms, enc, 16, leaveOpen: true)) { w.Write("línea 1\r\nline2\n"); w.Write("€ end"); }
    byte[] raw = ms.ToArray();
    ms.Position = 0;
    using var r = new StreamReader(ms);
    Console.WriteLine($"{raw.Length} [{r.ReadLine()}] [{r.ReadLine()}] [{r.ReadLine()}] {r.ReadLine() == null} {r.CurrentEncoding.WebName}");
}

// A multi-byte char split across the reader's buffer boundary.
var big = new StringBuilder();
for (int i = 0; i < 3000; i++) big.Append(i % 7 == 0 ? '€' : (char)('a' + i % 26));
var bms = new MemoryStream(Encoding.UTF8.GetBytes(big.ToString()));
var br = new StreamReader(bms, Encoding.UTF8, false, 128);
Console.WriteLine(br.ReadToEnd() == big.ToString());

// MemoryStream semantics.
var m = new MemoryStream();
m.Write(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
Console.WriteLine(m.Position + " " + m.Length + " " + m.Capacity.ToString().Length);
m.Seek(-2, SeekOrigin.End);
Console.WriteLine(m.ReadByte() + " " + m.ReadByte() + " " + m.ReadByte());
m.SetLength(2);
Console.WriteLine(Hex(m.ToArray()) + " " + m.Position);
m.Position = 6; m.WriteByte(9);
Console.WriteLine(Hex(m.ToArray()));
var fixedMs = new MemoryStream(new byte[] { 10, 20, 30 });
var buf = new byte[8];
Console.WriteLine(fixedMs.Read(buf, 0, 8) + " " + buf[2] + " " + fixedMs.Read(buf, 0, 8));
try { fixedMs.WriteByte(1); } catch (NotSupportedException e) { Console.WriteLine(e.GetType().Name); }

// TextReader/TextWriter polymorphism.
TextWriter tw = new StringWriter();
tw.Write(1); tw.Write(' '); tw.Write(2.5); tw.Write(' '); tw.Write(true); tw.WriteLine("!");
tw.WriteLine("{0}-{1}", "a", 3);
Console.Write(tw.ToString());
TextReader tr = new StringReader("x\ny");
Console.WriteLine((char)tr.Peek() + " " + (char)tr.Read() + " " + tr.ReadToEnd().Replace("\n", "\\n"));
