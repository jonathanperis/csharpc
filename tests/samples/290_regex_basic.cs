// System.Text.RegularExpressions basics: IsMatch, Match/NextMatch, Matches, Count, the static
// helpers (cached instances) and the startat / beginning+length overloads.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Print(Match m) =>
        Console.WriteLine(m.Success ? $"match '{m.Value}' at {m.Index} (length {m.Length})" : "no match");

    static void Main()
    {
        var digits = new Regex(@"\d+");
        Console.WriteLine(digits.IsMatch("abc123"));
        Console.WriteLine(digits.IsMatch("abcdef"));
        Console.WriteLine(Regex.IsMatch("Hello", "^h", RegexOptions.IgnoreCase));

        Print(digits.Match("order 66, then 1138"));
        Print(digits.Match("no digits here"));
        Print(digits.Match("a1b22c333", 3));
        Print(digits.Match("a1b22c333", 2, 3));
        Print(Regex.Match("key=value", @"(\w+)=(\w+)"));

        for (Match m = digits.Match("10 20 30 40"); m.Success; m = m.NextMatch())
            Console.Write(m.Value + ";");
        Console.WriteLine();

        MatchCollection all = Regex.Matches("the quick brown fox", @"\b\w");
        Console.WriteLine(all.Count);
        foreach (Match m in all) Console.Write(m.Value);
        Console.WriteLine();
        Console.WriteLine(all[2].Value + " " + all[2].Index);

        Console.WriteLine(Regex.Count("banana", "a"));
        Console.WriteLine(digits.Count("1 2 3 x 4"));

        // Empty matches advance by one character.
        Console.WriteLine(Regex.Matches("abc", "x*").Count);
        foreach (Match m in Regex.Matches("ab", "")) Console.Write($"[{m.Index}]");
        Console.WriteLine();

        Match empty = Regex.Match("abc", "z");
        Console.WriteLine($"{empty.Success} '{empty.Value}' {empty.Index} {empty.Length} {empty.Groups.Count}");
        Console.WriteLine(ReferenceEquals(Match.Empty, Match.Empty) + " " + Match.Empty.Success);

        Console.WriteLine(digits.ToString() + " " + digits.Options + " " + digits.RightToLeft);
        Console.WriteLine(Regex.Match("2024-05-17", @"(\d+)-(\d+)-(\d+)").Groups[3].Value);
        Console.WriteLine(Regex.Match("aaa", "a+?").Value + " " + Regex.Match("aaa", "a+").Value);

        try { digits.Match("abc", 4); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { digits.Match("abc", 1, 5); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { digits.IsMatch((string)null!); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { _ = new Regex(null!); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
