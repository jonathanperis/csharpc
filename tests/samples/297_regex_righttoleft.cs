// RegexOptions.RightToLeft: match order, startat, lazy quantifiers, lookarounds, Replace and Split.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(Regex r, string input, int? startat = null)
    {
        Console.Write($"{r,-14} {input,-16} =>");
        MatchCollection ms = startat is int s ? r.Matches(input, s) : r.Matches(input);
        foreach (Match m in ms)
        {
            Console.Write($" [{m.Index}:'{m.Value}'");
            for (int i = 1; i < m.Groups.Count; i++) Console.Write($" '{m.Groups[i].Value}'");
            Console.Write("]");
        }
        Console.WriteLine();
    }

    static void Main()
    {
        const RegexOptions rtl = RegexOptions.RightToLeft;
        Show(new Regex(@"\d+", rtl), "12 345 6");
        Show(new Regex(@"\d+?", rtl), "12 345");
        Show(new Regex(@"(\w)(\d)", rtl), "a1b2c3");
        Show(new Regex(@"\d+", rtl), "12 345 6", 5);
        Show(new Regex(@"a.*b", rtl), "axbxb");
        Show(new Regex(@"(?<=a)b", rtl), "abab cb");
        Show(new Regex(@"b(?=c)", rtl), "bc bd bc");
        Show(new Regex(@"^\w+|\w+$", rtl), "one two three");
        Show(new Regex(@"(a)\1", rtl), "aa ba aa");
        Show(new Regex(@"\1(a)", rtl), "aa ba aa");
        Show(new Regex(@"x*", rtl), "axxb");
        Show(new Regex(@"(?i)AB", rtl), "ab Ab aB");

        var r = new Regex(@"\d", rtl);
        Console.WriteLine(r.RightToLeft + " " + r.Options);
        Console.WriteLine(r.Match("a1b2c3").Value + " " + r.Match("a1b2c3").NextMatch().Value);
        Console.WriteLine(r.Replace("a1b2c3", "#"));
        Console.WriteLine(r.Replace("a1b2c3", "#", 2));
        Console.WriteLine(r.Replace("a1b2c3", m => "<" + m.Value + ">", 2, 4));
        Console.WriteLine(string.Join("|", r.Split("a1b2c3d")));
        Console.WriteLine(string.Join("|", r.Split("a1b2c3d", 2)));
        Console.WriteLine(string.Join("|", new Regex(@"(\d)", rtl).Split("a1b2c")));
        Console.WriteLine(Regex.Match("abc", "", rtl).Index);
    }
}
