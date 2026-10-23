// Regex.Replace: every substitution token, count/startat, MatchEvaluator, Match.Result.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        Console.WriteLine(Regex.Replace("John Smith", @"(\w+) (\w+)", "$2, $1"));
        Console.WriteLine(Regex.Replace("John Smith", @"(?<first>\w+) (?<last>\w+)", "${last}, ${first}"));
        Console.WriteLine(Regex.Replace("abc", "b", "[$&]"));
        Console.WriteLine(Regex.Replace("abc", "b", "[$`]"));
        Console.WriteLine(Regex.Replace("abc", "b", "[$']"));
        Console.WriteLine(Regex.Replace("abc", "(a)(b)?", "[$+]"));
        Console.WriteLine(Regex.Replace("abc", "b", "[$_]"));
        Console.WriteLine(Regex.Replace("abc", "b", "$$"));
        Console.WriteLine(Regex.Replace("abc", "(b)", "$9|${9}|$x|${x}|$|${1"));
        Console.WriteLine(Regex.Replace("abc", "(b)", "$10"));
        Console.WriteLine(Regex.Replace("abcdefghijk", "(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k)", "$11$10$1"));
        Console.WriteLine(Regex.Replace("aaa", "a", ""));
        Console.WriteLine(Regex.Replace("abc", "", "-"));
        Console.WriteLine(Regex.Replace("abc", "x*", "-"));
        Console.WriteLine(Regex.Replace("no match", "z", "y"));
        Console.WriteLine(Regex.Replace("Hello World", "o", "0", RegexOptions.IgnoreCase));

        var r = new Regex(@"\d");
        Console.WriteLine(r.Replace("1a2b3c4", "#", 2));
        Console.WriteLine(r.Replace("1a2b3c4", "#", -1));
        Console.WriteLine(r.Replace("1a2b3c4", "#", 0));
        Console.WriteLine(r.Replace("1a2b3c4", "#", 2, 3));
        Console.WriteLine(r.Replace("1a2b3c4", m => (int.Parse(m.Value) * 10).ToString()));
        Console.WriteLine(r.Replace("1a2b3c4", m => m.Index.ToString(), 2, 1));
        Console.WriteLine(Regex.Replace("one two", @"\w+", m => m.Value.ToUpperInvariant()));
        Console.WriteLine(Regex.Replace("one two", @"\w+", m => m.Groups[0].Length.ToString(), RegexOptions.None));
        int calls = 0;
        Console.WriteLine(Regex.Replace("a-b-c", "-", m => (++calls).ToString()) + " " + calls);
        Console.WriteLine(Regex.Replace("x y z", @"\s", m => m.NextMatch().Success ? "_" : "!"));

        Match match = Regex.Match("2024-06-01", @"(?<y>\d+)-(?<m>\d+)-(?<d>\d+)");
        Console.WriteLine(match.Result("${d}/${m}/${y} [$0] $$"));
        try { Match.Empty.Result("x"); }
        catch (NotSupportedException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { r.Replace("abc", (string)null!); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { r.Replace("abc", (MatchEvaluator)null!); }
        catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { r.Replace("abc", "x", -2); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
