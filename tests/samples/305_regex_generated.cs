// [GeneratedRegex] partial methods and properties: options, timeouts, culture names, nesting,
// namespaces, and the cached instance.
using System;
using System.Text.RegularExpressions;

namespace Demo.Parsing
{
    public static partial class Patterns
    {
        [GeneratedRegex(@"(?<key>\w+)=(?<value>[^;]*)")]
        public static partial Regex KeyValue();

        [GeneratedRegex("^hello", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
        internal static partial Regex Hello();

        [GeneratedRegex("i", RegexOptions.IgnoreCase, "tr-TR")]
        internal static partial Regex TurkishI();

        // Distinct patterns: .NET's generator shares one implementation between members with the same
        // pattern and options, whatever their cultures.
        [GeneratedRegex("i{1}", RegexOptions.IgnoreCase, "en-US")]
        internal static partial Regex EnglishI();

        [GeneratedRegex("(?:i)", RegexOptions.IgnoreCase)]
        internal static partial Regex InvariantI();

        [GeneratedRegex(@"\d+", RegexOptions.None, 1000)]
        internal static partial Regex Number { get; }

        public partial class Nested
        {
            [GeneratedRegex(@"\s+")]
            public static partial Regex Spaces();

            [GeneratedRegex("a", RegexOptions.ECMAScript | RegexOptions.Singleline)]
            public static partial Regex OddOptions();
        }
    }
}

partial class Program
{
    [GeneratedRegex(@"\b[A-Z]\w*")]
    private partial Regex Capitalized();

    static void Main()
    {
        foreach (Match m in Demo.Parsing.Patterns.KeyValue().Matches("a=1;bb=two;c="))
            Console.WriteLine(m.Groups["key"].Value + " -> '" + m.Groups["value"].Value + "'");
        Console.WriteLine(Demo.Parsing.Patterns.Hello().Matches("Hello\nhello\nHELLO there").Count);
        Console.WriteLine(Demo.Parsing.Patterns.Hello().Options);
        Console.WriteLine(string.Join(",", Demo.Parsing.Patterns.TurkishI().Matches("I i İ ı")).Replace("İ", "U+0130"));
        Console.WriteLine(string.Join(",", Demo.Parsing.Patterns.EnglishI().Matches("I i İ ı")).Replace("İ", "U+0130"));
        Console.WriteLine(string.Join(",", Demo.Parsing.Patterns.InvariantI().Matches("I i İ ı")).Replace("İ", "U+0130"));
        Console.WriteLine(Demo.Parsing.Patterns.Number.Match("abc 123").Value + " " + Demo.Parsing.Patterns.Number.MatchTimeout);
        Console.WriteLine(ReferenceEquals(Demo.Parsing.Patterns.Number, Demo.Parsing.Patterns.Number));
        Console.WriteLine(ReferenceEquals(Demo.Parsing.Patterns.KeyValue(), Demo.Parsing.Patterns.KeyValue()));
        Console.WriteLine(string.Join("|", Demo.Parsing.Patterns.Nested.Spaces().Split("a  b \t c")));
        Console.WriteLine(Demo.Parsing.Patterns.Nested.OddOptions().IsMatch("cat") + " " + Demo.Parsing.Patterns.Nested.OddOptions().Options);
        Console.WriteLine(Demo.Parsing.Patterns.KeyValue().ToString() + " " + Demo.Parsing.Patterns.KeyValue().MatchTimeout);
        var p = new Program();
        Console.WriteLine(string.Join(",", p.Capitalized().Matches("Alice met bob and Carol")));
    }
}
