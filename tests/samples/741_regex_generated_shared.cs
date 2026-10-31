// [GeneratedRegex] members with the same pattern and options: .NET's generator emits one implementation
// for them, so both members return the same cached instance; an instance member and a property too.
using System;
using System.Text.RegularExpressions;

var p = new Parser();
Console.WriteLine(ReferenceEquals(Parser.Digits(), Parser.MoreDigits()));
Console.WriteLine(ReferenceEquals(Parser.Digits(), Parser.Words));
Console.WriteLine(ReferenceEquals(p.Upper(), p.Upper()));
Console.WriteLine(string.Join(",", Parser.Digits().Matches("a1b22c333")));
Console.WriteLine(string.Join(",", Parser.Words.Matches("one two  three")));
Console.WriteLine(p.Upper().Replace("Hello World", "_"));
Console.WriteLine(Parser.Digits().Options + " " + Parser.Words.Options + " " + p.Upper().Options);
Console.WriteLine(ReferenceEquals(Parser.Digits(), Parser.TimedDigits()) + " " + Parser.TimedDigits().MatchTimeout.TotalMilliseconds);
Console.WriteLine(ReferenceEquals(Parser.TurkishI(), Parser.EnglishI()));
Console.WriteLine((string.Join(",", Parser.TurkishI().Matches("I i \u0130 \u0131")) + " / " + string.Join(",", Parser.EnglishI().Matches("I i \u0130 \u0131"))).Replace("\u0130", "U+0130").Replace("\u0131", "U+0131"));

partial class Parser
{
    [GeneratedRegex(@"\d+")]
    public static partial Regex Digits();

    [GeneratedRegex(@"\d+")]
    public static partial Regex MoreDigits();

    [GeneratedRegex(@"\w+", RegexOptions.CultureInvariant)]
    public static partial Regex Words { get; }

    [GeneratedRegex(@"\d+", RegexOptions.None, 500)]
    public static partial Regex TimedDigits();

    [GeneratedRegex("i", RegexOptions.IgnoreCase, "tr-TR")]
    public static partial Regex TurkishI();

    [GeneratedRegex("i", RegexOptions.IgnoreCase, "en-US")]
    public static partial Regex EnglishI();

    [GeneratedRegex("[A-Z]")]
    public partial Regex Upper();
}
