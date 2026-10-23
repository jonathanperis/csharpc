// Case-insensitive matching (invariant culture): options, inline (?i) / (?-i) scopes, classes and
// ranges, backreferences, and Unicode case equivalences (Kelvin sign, Greek sigma, dotted I).
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Show(string pattern, string input, RegexOptions options = RegexOptions.IgnoreCase)
    {
        Console.Write($"{pattern,-18} {options,-28} =>");
        foreach (Match m in Regex.Matches(input, pattern, options)) Console.Write($" '{Esc(m.Value)}'");
        Console.WriteLine();
    }

    static string Esc(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) sb.Append(c < 128 ? c.ToString() : $"\\u{(int)c:X4}");
        return sb.ToString();
    }

    static void Main()
    {
        Show("hello", "Hello HELLO hello hElLo");
        Show("[a-c]+", "ABC abc AbC xyz");
        Show("[^a-z]+", "abcXYZ123");
        Show(@"\w+", "ÀÉÎ àéî");
        Show("é", "É é e");
        Show("k", "K k K");
        Show("K", "K k K");
        Show("[k-l]", "K L K");
        Show("σ", "Σ σ ς");
        Show("i", "I i İ ı");
        Show("I", "I i İ ı");
        Show("i", "I i İ ı", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Show("ss", "ß SS ss");
        Show(@"(\w)\1", "aA Bb cc dE");
        Show(@"(?<c>[a-z])\k<c>", "xX yy");
        Show("a(?i)b(?-i)c", "aBc abC ABC abc", RegexOptions.None);
        Show("a(?i:b)c", "aBc ABC", RegexOptions.None);
        Show("(?i)straße", "STRASSE Straße STRAẞE", RegexOptions.None);
        Show("ǅ", "Ǆ ǅ ǆ");
        Show("[α-ω]+", "ΑΒΓ αβγ");
        Show("Ꭰ", "Ꭰ ꭰ");
        Console.WriteLine(Regex.IsMatch("TEST", "test") + " " + Regex.IsMatch("TEST", "test", RegexOptions.IgnoreCase));
        Console.WriteLine(Regex.Replace("Hello World", "o", "0", RegexOptions.IgnoreCase));
    }
}
