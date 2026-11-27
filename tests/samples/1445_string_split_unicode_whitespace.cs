// string.IsNullOrWhiteSpace and string.Split with no separators (and TrimEntries) use char.IsWhiteSpace: every Unicode White_Space
// character counts, not only the ASCII ones, NBSP and the U+2000 block (U+0085 NEL, U+1680, U+2028, U+2029, U+202F, U+205F, U+3000).
using System;

static class Program
{
    static void Show(string[] parts) => Console.WriteLine(parts.Length + ": " + Escape(string.Join("|", parts)));

    static string Escape(string s)
    {
        var b = new System.Text.StringBuilder();
        foreach (var c in s) b.Append(c is >= ' ' and <= '~' ? c.ToString() : "\\u" + ((int)c).ToString("X4"));
        return b.ToString();
    }

    static void Main()
    {
        foreach (var c in new[] { ' ', '\t', '\u0085', '\u00A0', '\u1680', '\u2000', '\u200A', '\u200B', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF', 'x' })
            Console.WriteLine($"U+{(int)c:X4} {string.IsNullOrWhiteSpace(" " + c + "\t")} {char.IsWhiteSpace(c)}");
        Console.WriteLine(string.IsNullOrWhiteSpace(null) + " " + string.IsNullOrWhiteSpace(""));

        var text = "one\u2028two\u3000three\u1680four\u0085five six";
        Show(text.Split());
        Show(text.Split((char[]?)null));
        Show(text.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
        Show(text.Split((string?)null, StringSplitOptions.None));
        Show("a,\u2029b\u2029,\u205Fc\u205F, ,".Split(',', StringSplitOptions.TrimEntries));
        Show("a,\u2029b\u2029,\u205Fc\u205F, ,".Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        Show("a--b----c".Split("--"));
        Show("a--b----c".Split("--", StringSplitOptions.RemoveEmptyEntries));
        Show("abc".Split(""));
        Show("a;b,c".Split(new[] { ';', ',' }));
        Show("a b  c".Split(' ', 2));
        Show("\u2028a\u2028".Split(new char[0], StringSplitOptions.TrimEntries));
    }
}
