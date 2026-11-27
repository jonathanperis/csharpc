// char.GetNumericValue (#439): the Unicode numeric value of any numeral, not only of the ASCII digits: vulgar fractions, other
// scripts' digits, Roman numerals, superscripts, circled numbers; -1 for a character with none. The string overload reads the
// code point at the index, a surrogate pair included (MATHEMATICAL DOUBLE-STRUCK DIGIT ZERO), and checks its arguments.
using System;

static class Program
{
    static void Main()
    {
        char[] chars = ['7', '½', '¼', '¾', '٣', '۹', '५', '๘', 'Ⅷ', 'ⅿ', '²', '⑫', '〇', '万', 'a', ' ', '⅕'];
        foreach (var c in chars) Console.WriteLine($"U+{(int)c:X4} {char.GetNumericValue(c)}");

        var s = "x½\U0001D7D8\U0001D7E1Ⅻ9";
        for (var i = 0; i < s.Length; i++) Console.WriteLine($"[{i}] {char.GetNumericValue(s, i)}");

        var sum = 0.0;
        for (var c = 0; c <= 0xFFFF; c++)
        {
            var v = char.GetNumericValue((char)c);
            if (v != -1) sum += v * (c % 7 + 1);
        }
        Console.WriteLine($"weighted sum over the BMP {sum:R}");

        try { char.GetNumericValue(s, s.Length); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + " " + e.ParamName); }
        try { char.GetNumericValue(null!, 0); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + " " + e.ParamName); }
    }
}
