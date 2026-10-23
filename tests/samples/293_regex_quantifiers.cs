// Greedy, lazy and bounded quantifiers, atomic groups, nested quantifiers and empty iterations.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string pattern, string input)
    {
        Console.Write($"{pattern,-18} {input,-18} =>");
        foreach (Match m in Regex.Matches(input, pattern))
        {
            Console.Write($" [{m.Index}:'{m.Value}'");
            for (int i = 1; i < m.Groups.Count; i++) Console.Write($" {i}='{m.Groups[i].Value}'#{m.Groups[i].Captures.Count}");
            Console.Write("]");
        }
        Console.WriteLine();
    }

    static void Main()
    {
        Show(@"<.+>", "<a><b>");
        Show(@"<.+?>", "<a><b>");
        Show(@"a{2}", "aaaaa");
        Show(@"a{2,}", "a aa aaaa");
        Show(@"a{1,3}", "aaaaaaa");
        Show(@"a{1,3}?", "aaaa");
        Show(@"a{0}b", "ab b");
        Show(@"x??y", "xy y");
        Show(@"(ab)*c", "ababc abc c");
        Show(@"(a|b)*?c", "abac");
        Show(@"(?>a+)ab", "aaab");
        Show(@"(?>a+)b", "aaab");
        Show(@"(a+)+b", "aaab aab");
        Show(@"(a*)*", "aa");
        Show(@"(a*)+", "b");
        Show(@"(a?)*?x", "aax");
        Show(@"(\w+?)(\d*)$", "abc123");
        Show(@"\d{2,3}?\d", "12345");
        Show(@"(?:a{2}){2}", "aaaaa");
        Show(@"[ab]{3,}?c", "ababc");
        Show(@"(\s*,\s*)+", "a , ,b");
        Show(@"(?:(a)|b)+", "aba");
        Show(@"\b\w{4}\b", "this word is long enough");
        Show(@"a{,2}", "a{,2}");
    }
}
