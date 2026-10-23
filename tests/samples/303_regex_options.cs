// RegexOptions: IgnorePatternWhitespace, ExplicitCapture, Singleline, ECMAScript, Compiled (same
// results as the interpreter), CultureInvariant, inline options, and the argument validation.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string pattern, string input, RegexOptions options)
    {
        Match m = Regex.Match(input, pattern, options);
        Console.Write($"[{options}] {m.Success} '{m.Value}' groups={m.Groups.Count}");
        for (int i = 1; i < m.Groups.Count; i++) Console.Write($" {m.Groups[i].Name}='{m.Groups[i].Value}'");
        Console.WriteLine();
    }

    static void Main()
    {
        Show(@"\d+  # number
               \s* [a-z]+  # word", "42  apples", RegexOptions.IgnorePatternWhitespace);
        Show(@"a b", "a b", RegexOptions.IgnorePatternWhitespace);
        Show(@"a\ b[ ]c", "a b c", RegexOptions.IgnorePatternWhitespace);
        Show(@"(a)(?<n>b)(c)", "abc", RegexOptions.ExplicitCapture);
        Show(@"(a)(?<n>b)(c)", "abc", RegexOptions.None);
        Show(@"(?n)(a)(?-n)(b)", "ab", RegexOptions.None);
        Show(@"a.c", "a\nc", RegexOptions.Singleline);
        Show(@"a.c", "a\nc", RegexOptions.None);
        Show(@"\w+", "naïve", RegexOptions.ECMAScript);
        Show(@"(a)\1", "aa", RegexOptions.ECMAScript);
        Show(@"\1(a)", "aa", RegexOptions.ECMAScript);
        Show(@"(\w+)\s\1", "hey hey", RegexOptions.Compiled);
        Show(@"HELLO", "hello", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Show(@"(?imnsx-imnsx:a)", "a", RegexOptions.None);
        Show(@"(?x) a b # c", "ab", RegexOptions.None);
        Show(@"^b", "a\nb", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var r = new Regex("x", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        Console.WriteLine(r.Options + " " + (int)r.Options + " " + r.MatchTimeout + " " + (r.MatchTimeout == Regex.InfiniteMatchTimeout));
        var t = new Regex("x", RegexOptions.None, TimeSpan.FromSeconds(2));
        Console.WriteLine(t.MatchTimeout + " " + Regex.InfiniteMatchTimeout.Ticks);

        RegexOptions[] invalid =
        {
            RegexOptions.ECMAScript | RegexOptions.Singleline,
            RegexOptions.ECMAScript | RegexOptions.ExplicitCapture,
            (RegexOptions)0x1000,
            (RegexOptions)(-1),
        };
        foreach (RegexOptions o in invalid)
        {
            try { _ = new Regex("a", o); Console.WriteLine("accepted " + (int)o); }
            catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        }
        TimeSpan[] timeouts = { TimeSpan.Zero, TimeSpan.FromMilliseconds(-2), TimeSpan.FromDays(30), TimeSpan.FromMilliseconds(1) };
        foreach (TimeSpan ts in timeouts)
        {
            try { _ = new Regex("a", RegexOptions.None, ts); Console.WriteLine("accepted " + ts); }
            catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        }
    }
}
