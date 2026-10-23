// Character classes: ranges, negation, subtraction, shorthand classes, Unicode categories and
// named blocks, escapes inside classes, and ECMAScript's ASCII-only shorthands.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string pattern, string input, RegexOptions options = RegexOptions.None)
    {
        Console.Write($"{pattern,-26} =>");
        foreach (Match m in Regex.Matches(input, pattern, options)) Console.Write($" '{m.Value}'");
        Console.WriteLine();
    }

    static void Main()
    {
        const string text = "Hello, Wörld! ÀÉÎ 123 ٣٤ αβγ ΔΣ _x-y\ttab";
        Show(@"[a-z]+", text);
        Show(@"[^a-zA-Z\s]+", text);
        Show(@"[a-z-[aeiou]]+", text);
        Show(@"[\w-[\d_]]+", text);
        Show(@"[a-z-[d-f-[e]]]+", "abcdefgh");
        Show(@"\d+", text);
        Show(@"\d+", text, RegexOptions.ECMAScript);
        Show(@"\w+", text, RegexOptions.ECMAScript);
        Show(@"\W+", "a-b c");
        Show(@"\s+", "a \t\n b");
        Show(@"\S+", "a \t\n b");
        Show(@"\p{Lu}+", text);
        Show(@"\p{Ll}+", text);
        Show(@"\p{L}+", text);
        Show(@"\P{L}+", "ab12cd");
        Show(@"\p{N}+", text);
        Show(@"\p{Nd}", text);
        Show(@"\p{P}", text);
        Show(@"\p{IsGreek}+", text);
        Show(@"\p{IsGreekandCoptic}+", text);
        Show(@"\p{IsBasicLatin}+", "abc é d");
        Show(@"[\p{Lu}\d]+", text);
        Show(@"[^\p{L}\s]+", text);
        Show(@"[\]\[\\\-^]+", @"a[b]c\d-e^f");
        Show(@"[]a]+", "]a]b");
        Show(@"[a\-z]+", "a-z b");
        Show(@"[.+*?]+", "a.+*?b");
        Show(@"[\x41-\x43a]+", "ABCDabc");
        Show(@"[\t ]+", "a\t b");
        Show(@"[\b]", "a\bb");
        Show(@"\x41B\103\cA", "ABC\u0001");
        Show(@".", "a\nb");
        Show(@"[^\n]+", "line1\nline2");
        Show(@"\p{Sc}", "$5 €6 £7");
        Show(@"[\p{Pd}\p{Ps}\p{Pe}]", "a-(b)[c]");
    }
}
