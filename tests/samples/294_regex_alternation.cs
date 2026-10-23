// Alternation order (leftmost alternative wins, not the longest) and conditional constructs.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string pattern, string input, RegexOptions options = RegexOptions.None)
    {
        Console.Write($"{pattern,-34} {input,-14} =>");
        foreach (Match m in Regex.Matches(input, pattern, options)) Console.Write($" '{m.Value}'@{m.Index}");
        Console.WriteLine();
    }

    static void Main()
    {
        Show(@"a|ab", "ab");
        Show(@"ab|a", "ab");
        Show(@"(a|ab)(c|bcd)", "abcd");
        Show(@"cat|category|dog", "category dog");
        Show(@"\b(?:one|two|three)\b", "one twothree three");
        Show(@"x(?:a|b|c)y|z", "xay xdy z xcy");
        Show(@"|a", "aa");
        Show(@"a|", "ba");
        Show(@"(a)?(?(1)b|c)", "ab c b");
        Show(@"(?<q>"")?\w+(?(q)"")", "\"hi\" there \"no");
        Show(@"(?(?=\d)\d{3}|[a-z]+)", "123 abc 45");
        Show(@"(?(\d)\d\d|x)", "12 x 3");
        Show(@"^(?:(?<a>a)|b)(?(a)1|2)$", "a1");
        Show(@"^(?:(?<a>a)|b)(?(a)1|2)$", "b2");
        Show(@"(foo|foobar)baz", "foobarbaz");
        Show(@"(?>foo|foobar)baz", "foobarbaz");
        Show(@"[abc]|[cde]", "edcba", RegexOptions.RightToLeft);
    }
}
