// Lookahead and lookbehind assertions (positive, negative, nested, variable length, with captures).
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string pattern, string input)
    {
        Console.Write($"{pattern,-28} {input,-22} =>");
        foreach (Match m in Regex.Matches(input, pattern))
        {
            Console.Write($" '{m.Value}'@{m.Index}");
            if (m.Groups.Count > 1) Console.Write($"(g1='{m.Groups[1].Value}')");
        }
        Console.WriteLine();
    }

    static void Main()
    {
        Show(@"\w+(?=:)", "key: value, other: x");
        Show(@"\d+(?!px)\b", "10px 20em 30");
        Show(@"(?<=\$)\d+(\.\d\d)?", "$5 and $12.50, 7");
        Show(@"(?<!-)\b\d+", "3 -4 5 -6");
        Show(@"(?<=a.+)b", "ab axxb cb");
        Show(@"(?<=^|,)[^,]*", "a,,b,c");
        Show(@"(?=(\w\w))", "abcd");
        Show(@"(?<=(\d))x", "1x2x x");
        Show(@"q(?!u)", "qatar quit qi");
        Show(@"(?=.*\d)(?=.*[a-z])\w{6,}", "abc123 abcdef 123456 a1b2c3");
        Show(@"\b(?!(?:the|a)\b)\w+", "the cat and a dog");
        Show(@"(?<=(?<!x)y)z", "yz xyz");
        Show(@"(?<=\b\w{3})\s", "one two three");
        Show(@"(?=a)", "banana");
        Show(@"(?<=[aeiou])[^aeiou\s]", "hello world");
        Show(@"^(?=.{4}$).*", "abcd");
        Show(@"(?<=(?<v>[aeiou]))n", "banana inn");
    }
}
