// Regex.Split: captured delimiters are included, count limits, startat, empty matches and RTL.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string[] parts) => Console.WriteLine(parts.Length + ": [" + string.Join("|", parts) + "]");

    static void Main()
    {
        Show(Regex.Split("a,b;c d", "[,; ]"));
        Show(Regex.Split("a,b;c", "([,;])"));
        Show(Regex.Split("a1b22c", @"(\d)(\d)?"));
        Show(Regex.Split("a,,b,", ","));
        Show(Regex.Split("", ","));
        Show(Regex.Split("abc", ""));
        Show(Regex.Split("abc", "x*"));
        Show(Regex.Split("one  two   three", @"\s+"));
        Show(Regex.Split("CamelCaseString", "(?=[A-Z])"));
        Show(Regex.Split("a-b-c", "-", RegexOptions.RightToLeft));
        Show(Regex.Split("A b C", "b", RegexOptions.IgnoreCase));

        var r = new Regex(",");
        Show(r.Split("a,b,c,d", 2));
        Show(r.Split("a,b,c,d", 1));
        Show(r.Split("a,b,c,d", 0));
        Show(r.Split("a,b,c,d", 3, 2));
        Show(new Regex("(,)").Split("a,b,c", 2));
        Show(new Regex(",", RegexOptions.RightToLeft).Split("a,b,c,d", 2));
        Show(new Regex(",", RegexOptions.RightToLeft).Split("a,b,c,d", 3, 5));
        try { r.Split("a", -1); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { r.Split("a", 1, 2); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}
