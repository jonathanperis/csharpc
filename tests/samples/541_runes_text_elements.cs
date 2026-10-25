// Runes and text elements (extended grapheme clusters, UAX #29, with .NET's own break data):
// string/span EnumerateRunes, StringBuilder.Append(Rune), StringInfo and TextElementEnumerator
// (docs-standard-base-types-character-encoding-introduction-RuneSamples).
using System;
using System.Globalization;
using System.Linq;
using System.Text;

string[] samples =
{
    "a", "á", "á", "👩🏽‍🚒", "🇧🇷🇺🇸", "\r\n", "x\r\ny", "क्षि", "நி", "각", "각", "é̂̃",
    "🏳️‍🌈", "👍🏿", "؀x", "ab\uD800c", "\uDC00", "",
};
foreach (var s in samples)
{
    var info = new StringInfo(s);
    var elements = new System.Collections.Generic.List<string>();
    var e = StringInfo.GetTextElementEnumerator(s);
    while (e.MoveNext()) elements.Add($"{e.ElementIndex}:{Escape(e.GetTextElement())}");
    Console.WriteLine($"{Escape(s)}: chars {s.Length}, runes {s.EnumerateRunes().Count()}, elements {info.LengthInTextElements} " +
        $"[{string.Join(" ", elements)}] indexes [{string.Join(",", StringInfo.ParseCombiningCharacters(s))}]");
}

string Escape(string s) => string.Concat(s.Select(c => c < 0x80 && !char.IsControl(c) ? c.ToString() : $"\\u{(int)c:X4}"));

var text = "Hello 👩🏽‍🚒 wörld";
Console.WriteLine($"GetNextTextElement: {Escape(StringInfo.GetNextTextElement(text, 6))} length {StringInfo.GetNextTextElementLength(text, 6)}");
Console.WriteLine($"SubstringByTextElements: {Escape(new StringInfo(text).SubstringByTextElements(6, 3))}");
try { new StringInfo(text).SubstringByTextElements(50); }
catch (ArgumentOutOfRangeException ex) { Console.WriteLine(ex.Message.Split(Environment.NewLine)[0]); }

var upper = new StringBuilder();
foreach (Rune rune in "naïve café 🦄 straße".EnumerateRunes()) upper.Append(Rune.ToUpperInvariant(rune));
Console.WriteLine(Escape(upper.ToString()));
int count = 0;
foreach (Rune rune in "a😀b".AsSpan().EnumerateRunes()) count += rune.Utf16SequenceLength;
Console.WriteLine($"span runes utf16 length {count}");
