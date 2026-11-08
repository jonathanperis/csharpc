// RegexOptions.NonBacktracking (the framework's symbolic engine, compiled from its IL): matches, captures,
// Replace/Split, the static methods, and the constructs the engine rejects when the Regex is constructed.
using System;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        var digits = new Regex(@"\d+", RegexOptions.NonBacktracking);
        Console.WriteLine(digits.IsMatch("abc 123 def"));
        var m = digits.Match("abc 123 def 4567");
        Console.WriteLine(m.Value + " " + m.Index + " " + m.Length);
        Console.WriteLine(string.Join(",", digits.Matches("a1b22c333").Select(x => x.Value)));
        Console.WriteLine(digits.Replace("a1b22c333", "#"));
        Console.WriteLine(string.Join("|", new Regex(@"\s*,\s*", RegexOptions.NonBacktracking).Split("a , b,c ,  d")));

        var date = new Regex(@"(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})", RegexOptions.NonBacktracking);
        var dm = date.Match("on 2026-11-08 and 2027-01-02");
        Console.WriteLine(dm.Groups["y"].Value + "/" + dm.Groups["m"].Value + "/" + dm.Groups["d"].Value + " at " + dm.Groups["m"].Index);
        Console.WriteLine(date.Replace("2026-11-08", "${d}.${m}.${y}"));

        Console.WriteLine(Regex.IsMatch("HELLO", "hello", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase));
        Console.WriteLine(Regex.Match("key=value", @"(\w+)=(\w+)", RegexOptions.NonBacktracking).Groups[2].Value);
        Console.WriteLine(Regex.Replace("a-b-c", "-", "+", RegexOptions.NonBacktracking));

        // Alternation: the leftmost match, and the earliest alternative among matches that start there.
        Console.WriteLine(Regex.Match("xabcx", "ab|abc", RegexOptions.NonBacktracking).Value);

        // A catastrophic pattern for a backtracking engine finishes at once.
        Console.WriteLine(new Regex(@"^(a+)+$", RegexOptions.NonBacktracking).IsMatch(new string('a', 40) + "b"));

        foreach (var pattern in new[] { @"(a)\1", @"(?<=a)b", @"a(?=b)", @"(?>a)" })
        {
            try { new Regex(pattern, RegexOptions.NonBacktracking); Console.WriteLine(pattern + " accepted"); }
            catch (NotSupportedException e) { Console.WriteLine(pattern + " -> " + e.GetType().Name + ": " + e.Message); }
        }
        try { new Regex("a", RegexOptions.NonBacktracking | RegexOptions.RightToLeft); }
        catch (Exception e) { Console.WriteLine("RightToLeft -> " + e.GetType().Name + ": " + e.Message); }
    }
}
