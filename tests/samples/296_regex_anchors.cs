// Anchors and boundaries with and without Multiline / Singleline: ^ $ \A \Z \z \G \b \B.
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string pattern, string input, RegexOptions options = RegexOptions.None)
    {
        Console.Write($"{pattern,-12} {options,-22} =>");
        foreach (Match m in Regex.Matches(input, pattern, options))
            Console.Write($" [{m.Index}:{m.Value.Replace("\n", "\\n")}]");
        Console.WriteLine();
    }

    static void Main()
    {
        const string text = "first line\nsecond line\nthird\n";
        Show(@"^\w+", text);
        Show(@"^\w+", text, RegexOptions.Multiline);
        Show(@"\w+$", text);
        Show(@"\w+$", text, RegexOptions.Multiline);
        Show(@"\w+\z", text);
        Show(@"\w+\Z", text);
        Show(@"\A\w+", text, RegexOptions.Multiline);
        Show(@"$", text, RegexOptions.Multiline);
        Show(@"^", text, RegexOptions.Multiline);
        Show(@"line.second", text);
        Show(@"line.second", text, RegexOptions.Singleline);
        Show(@"(?m)^t", text);
        Show(@"(?s)d.s", "end\nsecond");
        Show(@"\G\d", "123x45");
        Show(@"\G,?\d", "1,2,3 4");
        Show(@"\bcat\b", "cat concat cat's bobcat");
        Show(@"\Bcat\B", "concatenate cat");
        Show(@"\b", "a b");
        Show(@"\B", "ab c");
        Show(@"^$", "", RegexOptions.Multiline);
        Show(@"^$", "a\n\nb", RegexOptions.Multiline);
        Show(@"\r?$", "a\r\nb\r\n", RegexOptions.Multiline);
        Show(@"^\s*$", "x\n \n\ny", RegexOptions.Multiline);
    }
}
