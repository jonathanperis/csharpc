// Rune and its enumerators from CoreLib's IL (#423): UTF-8/UTF-16 decoding with .NET's maximal-subpart rules (first and last
// sequence), classification, numeric values and invariant casing over the Unicode data, encoding into short buffers, comparisons,
// argument checks, and the string/span rune enumerators replacing ill-formed surrogates with U+FFFD.
using System;
using System.Globalization;
using System.Text;

static class Program
{
    static void Main()
    {
        // Rune decoding: ill-formed sequences report the maximal subpart consumed.
        byte[][] utf8 =
        {
            new byte[] { 0x41 }, new byte[] { 0xC3, 0xA9 }, new byte[] { 0xE2, 0x82 }, new byte[] { 0xF0, 0x9F, 0x98, 0x80 },
            new byte[] { 0xC0, 0x80 }, new byte[] { 0xED, 0xA0, 0x80 }, new byte[] { 0xF4, 0x90, 0x80, 0x80 }, new byte[] { 0x80 }, new byte[0],
        };
        foreach (var b in utf8)
        {
            var st = Rune.DecodeFromUtf8(b, out Rune r, out int n);
            var st2 = Rune.DecodeLastFromUtf8(b, out Rune r2, out int n2);
            Console.WriteLine($"utf8 {Convert.ToHexString(b)}: {st} U+{r.Value:X4} {n} | last {st2} U+{r2.Value:X4} {n2}");
        }
        string[] utf16 = { "a", "😀", "\uD83D", "\uDE00x", "" };
        foreach (var s in utf16)
        {
            var st = Rune.DecodeFromUtf16(s, out Rune r, out int n);
            Console.WriteLine($"utf16 {s.Length}: {st} U+{r.Value:X4} {n} valid-at-0 {Rune.TryGetRuneAt(s + " ", 0, out _)}");
        }

        // Classification and casing.
        foreach (int cp in new[] { 0x41, 0x61, 0x30, 0x0663, 0x2163, 0x1F600, 0x10400, 0x00DF, 0x0130, 0x20, 0x3000, 0xE000 })
        {
            var r = new Rune(cp);
            Console.WriteLine($"U+{cp:X4} {Rune.GetUnicodeCategory(r)} L{(Rune.IsLetter(r) ? 1 : 0)} D{(Rune.IsDigit(r) ? 1 : 0)} N{Rune.GetNumericValue(r)} W{(Rune.IsWhiteSpace(r) ? 1 : 0)} " +
                $"U+{Rune.ToUpperInvariant(r).Value:X4} U+{Rune.ToLowerInvariant(r).Value:X4} plane {r.Plane} u8 {r.Utf8SequenceLength} u16 {r.Utf16SequenceLength} '{r}'");
        }
        var smile = new Rune(0x1F600);
        Span<byte> u8 = stackalloc byte[4];
        Span<char> u16 = stackalloc char[2];
        Console.WriteLine($"encode {smile.TryEncodeToUtf8(u8, out int w8)} {w8} {Convert.ToHexString(u8)} {smile.EncodeToUtf16(u16)} {(int)u16[0]:X4} {(int)u16[1]:X4}");
        Console.WriteLine($"too small {smile.TryEncodeToUtf8(u8.Slice(0, 3), out w8)} {w8}");
        Console.WriteLine($"compare {new Rune('a').CompareTo(new Rune('b'))} {new Rune('a') == new Rune(0x61)} {Rune.IsValid(0xD800)} {Rune.IsValid(0x10FFFF)} {Rune.ReplacementChar.Value:X4}");
        try { _ = new Rune(0xD800); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.GetType().Name} {e.ParamName}"); }
        try { _ = new Rune('\uDC00'); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.GetType().Name} {e.ParamName}"); }
        Console.WriteLine($"format {smile.ToString()} {((ISpanFormattable)new Rune('x')).ToString(null, CultureInfo.InvariantCulture)}");

        // Enumerators: a lone surrogate is U+FFFD.
        var sb = new StringBuilder();
        foreach (var r in "a😀\uD800b".EnumerateRunes()) sb.Append(r.Value.ToString("X")).Append(' ');
        foreach (var r in "x\uDFFFy".AsSpan().EnumerateRunes()) sb.Append(r.Value.ToString("X")).Append(' ');
        sb.Append(new Rune(0x1F680));
        Console.WriteLine(sb.ToString().Length + " " + sb);

        Console.WriteLine($"{Rune.ToUpper(new Rune('i'), CultureInfo.InvariantCulture).Value:X4} {Rune.IsControl(new Rune(7))} {Rune.IsPunctuation(new Rune('!'))} {Rune.IsSymbol(new Rune('+'))} {Rune.IsSeparator(new Rune(0x2028))}");
        Console.WriteLine($"{Rune.GetRuneAt("a\U0001F600", 1).Value:X} {new Rune('z').EncodeToUtf8(new byte[4])} {Rune.TryCreate('\uD83D', '\uDE00', out var made)} {made.Value:X}");
    }
}
