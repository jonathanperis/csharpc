// Char's library members served from CoreLib's IL (#423): classification over every BMP char, numeric
// values, UTF-32 conversion, casing and parsing, with .NET's argument checks.
using System;
using System.Globalization;

static class Program
{
    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine($"{label}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // One checksum per predicate over U+0000..U+FFFF: any differing char changes it.
        var predicates = new (string Name, Func<char, bool> Test)[]
        {
            ("IsLetter", char.IsLetter), ("IsDigit", char.IsDigit), ("IsLetterOrDigit", char.IsLetterOrDigit),
            ("IsNumber", char.IsNumber), ("IsUpper", char.IsUpper), ("IsLower", char.IsLower),
            ("IsPunctuation", char.IsPunctuation), ("IsSymbol", char.IsSymbol), ("IsSeparator", char.IsSeparator),
            ("IsWhiteSpace", char.IsWhiteSpace), ("IsControl", char.IsControl), ("IsSurrogate", char.IsSurrogate),
            ("IsAsciiHexDigitLower", char.IsAsciiHexDigitLower), ("IsAsciiHexDigitUpper", char.IsAsciiHexDigitUpper),
        };
        foreach (var (name, test) in predicates)
        {
            long sum = 0; int count = 0;
            for (int c = 0; c <= 0xFFFF; c++) if (test((char)c)) { sum += c * 31L + count; count++; }
            Console.WriteLine($"{name}: {count} {sum}");
        }
        long categories = 0, upper = 0, lower = 0;
        double numeric = 0;
        for (int c = 0; c <= 0xFFFF; c++)
        {
            categories += (int)char.GetUnicodeCategory((char)c) * (c % 97 + 1);
            upper += char.ToUpperInvariant((char)c) - c;
            lower += char.ToLowerInvariant((char)c) - c;
            numeric += char.GetNumericValue((char)c) * (c % 7 + 1);
        }
        Console.WriteLine($"categories {categories} upper {upper} lower {lower} numeric {numeric}");

        Console.WriteLine(char.GetNumericValue('½'));
        Console.WriteLine(char.GetNumericValue("x\U0001D7D8", 1));
        Console.WriteLine(char.IsLetter("a\U0001D400", 1) + " " + char.IsSurrogatePair("\U0001F600", 0));
        Console.WriteLine(char.ConvertToUtf32("\U0001F600", 0).ToString("X") + " " + char.ConvertFromUtf32(0x1F601).Length);
        Console.WriteLine(char.ToUpper('ı', new CultureInfo("tr-TR")) + " " + char.ToLower('I', new CultureInfo("tr-TR")) + " " + char.ToUpper('ÿ'));
        Console.WriteLine(char.Parse("Q") + " " + char.TryParse("QQ", out char r) + " " + (int)r);
        Console.WriteLine(char.IsBetween('m', 'a', 'z') + " " + ((IFormattable)'x').ToString(null, null) + 'y'.ToString(CultureInfo.InvariantCulture));

        Try("ConvertFromUtf32 surrogate", () => char.ConvertFromUtf32(0xD800));
        Try("ConvertToUtf32 bad pair", () => char.ConvertToUtf32('a', 'b'));
        Try("ConvertToUtf32 lone high", () => char.ConvertToUtf32("\uD800", 0));
        Try("IsLetter index", () => char.IsLetter("abc", 3));
        Try("IsDigit null", () => char.IsDigit(null, 0));
        Try("Parse long", () => char.Parse("ab"));
        Try("Parse null", () => char.Parse(null));
    }
}
