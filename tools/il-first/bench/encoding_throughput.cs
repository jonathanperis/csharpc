// Encoding throughput, the program behind the NativeAOT column of docs/il-first/System.Text.Encoding.md
// (tools/il-first/measure-aot.sh): UTF-8 encoding and decoding of ASCII and mixed text, counts, a streaming decoder fed in chunks,
// Latin-1, ASCII and UTF-16 round trips, and replacement of invalid input. The output is checksums only.
using System;
using System.Text;

var ascii = new StringBuilder();
for (int i = 0; i < 2000; i++) ascii.Append("GET /plaintext HTTP/1.1 Host: localhost:8080 Accept: */* ").Append(i).Append('\n');
string asciiText = ascii.ToString();
var mixed = new StringBuilder();
for (int i = 0; i < 2000; i++) mixed.Append("Größe café naïve Ünïcödé ").Append(i).Append(" 日本語テキスト 😀\n");
string mixedText = mixed.ToString();
byte[] invalid = new byte[4096];
for (int i = 0; i < invalid.Length; i++) invalid[i] = (byte)(i * 37 + 11);

long check = 0;
var utf8 = Encoding.UTF8;
for (int round = 0; round < 100; round++)
{
    byte[] a = utf8.GetBytes(asciiText);
    byte[] m = utf8.GetBytes(mixedText);
    check += a.Length + m.Length + utf8.GetCharCount(m) + utf8.GetByteCount(mixedText);
    check += utf8.GetString(a).Length + utf8.GetString(m).Length;
    var decoder = utf8.GetDecoder();
    var chars = new char[1024];
    for (int at = 0; at < m.Length; at += 777)
        check += decoder.GetChars(m, at, Math.Min(777, m.Length - at), chars, 0, flush: at + 777 >= m.Length);
    check += utf8.GetString(invalid).Length;
    byte[] latin = Encoding.Latin1.GetBytes(mixedText);
    check += latin.Length + Encoding.Latin1.GetString(latin).Length;
    check += Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(asciiText)).Length;
    byte[] utf16 = Encoding.Unicode.GetBytes(mixedText);
    check += Encoding.Unicode.GetString(utf16).Length;
}
Console.WriteLine(check);
