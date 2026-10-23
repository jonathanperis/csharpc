// Span-based Regex APIs: IsMatch / Count over ReadOnlySpan<char>, EnumerateMatches (ValueMatch)
// and EnumerateSplits (Range).
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        ReadOnlySpan<char> text = "id=42; id=7; name=x".AsSpan();
        var number = new Regex(@"\d+");
        Console.WriteLine(number.IsMatch(text));
        Console.WriteLine(number.IsMatch(text.Slice(13)));
        Console.WriteLine(Regex.IsMatch(text, @"^id"));
        Console.WriteLine(Regex.IsMatch(text, "NAME", RegexOptions.IgnoreCase));
        Console.WriteLine(number.Count(text));
        Console.WriteLine(Regex.Count(text, "id"));

        foreach (ValueMatch m in number.EnumerateMatches(text))
            Console.Write($"[{m.Index},{m.Length}:{text.Slice(m.Index, m.Length).ToString()}]");
        Console.WriteLine();
        foreach (ValueMatch m in Regex.EnumerateMatches("a1b22c333", @"\d+", RegexOptions.RightToLeft))
            Console.Write($"[{m.Index},{m.Length}]");
        Console.WriteLine();
        foreach (ValueMatch m in number.EnumerateMatches("1 2 3 4", 3))
            Console.Write($"[{m.Index}]");
        Console.WriteLine();
        int empties = 0;
        foreach (ValueMatch m in Regex.EnumerateMatches("abc", "x*")) empties += m.Length == 0 ? 1 : 0;
        Console.WriteLine(empties);

        string csv = "a,b,,c";
        foreach (Range r in Regex.EnumerateSplits(csv, ","))
            Console.Write("<" + csv[r] + ">");
        Console.WriteLine();
        foreach (Range r in new Regex(",").EnumerateSplits(csv, 2))
            Console.Write("<" + csv[r] + ">");
        Console.WriteLine();
        foreach (Range r in new Regex(",", RegexOptions.RightToLeft).EnumerateSplits(csv))
            Console.Write("<" + csv[r] + ">");
        Console.WriteLine();
    }
}
