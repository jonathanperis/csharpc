// Rune throughput, the program behind the NativeAOT column of docs/il-first/System.Text.Rune.md (tools/il-first/measure-aot.sh):
// rune enumeration over a string and a span, classification and invariant casing, UTF-8 rune decoding and encoding. Checksums only.
using System;
using System.Text;

var text = new StringBuilder();
for (int i = 0; i < 4000; i++) text.Append("Größe ").Append(i).Append(" naïve Ünïcödé 日本 😀 ").Append(i % 3 == 0 ? "\uD800" : "x").Append('\n');
string s = text.ToString();
byte[] utf8 = Encoding.UTF8.GetBytes(s);
byte[] buffer = new byte[4];
long check = 0;
for (int round = 0; round < 40; round++)
{
    foreach (var r in s.EnumerateRunes())
        if (Rune.IsLetterOrDigit(r)) check += Rune.ToUpperInvariant(r).Value & 7;
        else if (Rune.IsWhiteSpace(r)) check++;
    foreach (var r in s.AsSpan(0, s.Length / 2).EnumerateRunes()) check += r.Utf8SequenceLength + (Rune.GetUnicodeCategory(r) == System.Globalization.UnicodeCategory.OtherSymbol ? 1 : 0);
    for (int i = 0; i < utf8.Length;)
    {
        Rune.DecodeFromUtf8(utf8.AsSpan(i), out Rune r, out int n);
        check += r.EncodeToUtf8(buffer);
        i += n;
    }
}
Console.WriteLine(check);
