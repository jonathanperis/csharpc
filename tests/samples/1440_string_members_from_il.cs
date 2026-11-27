// String's library members served from CoreLib's IL (#423): searching, comparing, casing, trimming, padding,
// splitting, joining and replacing, with the argument checks and exception messages of .NET.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

static class Program
{
    static void Show(string label, object value) => Console.WriteLine($"{label}: {value}");

    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine($"{label}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static string Join(string[] parts) => "[" + string.Join("|", parts) + "]";

    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string s = "  Hello, World! Hello again.  ";

        // Searching
        Show("IndexOf char", s.IndexOf('o'));
        Show("IndexOf char start count", s.IndexOf('o', 10, 5));
        Show("IndexOf string ordinal", s.IndexOf("Hello", 5, StringComparison.Ordinal));
        Show("IndexOf ignore case", s.IndexOf("WORLD", StringComparison.OrdinalIgnoreCase));
        Show("IndexOf culture ignore case", s.IndexOf("WORLD", StringComparison.InvariantCultureIgnoreCase));
        Show("IndexOf char ignore case", s.IndexOf('W', StringComparison.OrdinalIgnoreCase));
        Show("LastIndexOf", s.LastIndexOf("Hello", StringComparison.Ordinal));
        Show("LastIndexOf char range", s.LastIndexOf('l', 12, 6));
        Show("IndexOfAny", s.IndexOfAny(new[] { '!', ',' }));
        Show("LastIndexOfAny", s.LastIndexOfAny(new[] { 'H', 'a' }, 20));
        Show("Contains ignore case", s.Contains("AGAIN", StringComparison.OrdinalIgnoreCase));
        Show("Contains char", s.Contains('!'));
        Show("StartsWith char", "abc".StartsWith('a'));
        Show("EndsWith ignore case", "File.TXT".EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        Show("StartsWith culture", "Ångström".StartsWith("Å", StringComparison.InvariantCulture));
        Show("IndexOf empty", "abc".IndexOf("", 2));
        Show("LastIndexOf empty", "abc".LastIndexOf(""));

        // Manipulation
        Show("Substring", s.Substring(2, 5));
        Show("Insert", "abef".Insert(2, "cd"));
        Show("Remove", "abcdef".Remove(1, 3));
        Show("Remove tail", "abcdef".Remove(4));
        Show("Replace char", "a-b-c".Replace('-', '+'));
        Show("Replace string", "one two one".Replace("one", "1"));
        Show("Replace null", "one two one".Replace("one", null));
        Show("Replace ignore case", "One two ONE".Replace("one", "1", StringComparison.OrdinalIgnoreCase));
        Show("Replace culture", "One two ONE".Replace("one", "1", true, CultureInfo.InvariantCulture));
        Show("ReplaceLineEndings", "a\r\nb\nc\rd\u2028e".ReplaceLineEndings("|"));
        Show("PadLeft", "7".PadLeft(3, '0'));
        Show("PadRight", "ab".PadRight(5) + "|");
        Show("Trim", "[" + s.Trim() + "]");
        Show("TrimStart chars", "xxabcxx".TrimStart('x'));
        Show("TrimEnd chars", "abc;;,".TrimEnd(';', ','));
        Show("Trim unicode space", "[" + "  word　".Trim() + "]");
        Show("ToUpperInvariant", "straße été".ToUpperInvariant());
        Show("ToLower culture", "İSTANBUL".ToLower(new CultureInfo("tr-TR")));
        Show("ToUpper culture", "istanbul".ToUpper(new CultureInfo("tr-TR")));
        Show("ToCharArray", new string("hello".ToCharArray(1, 3)));
        var buffer = new char[5];
        "abcdefg".CopyTo(2, buffer, 1, 3);
        Show("CopyTo", new string(buffer).Replace('\0', '.'));
        Span<char> small = stackalloc char[2];
        Show("TryCopyTo", "abc".TryCopyTo(small));

        // Splitting and joining
        Show("Split char", Join("a,b,,c".Split(',')));
        Show("Split remove empty", Join("a,b,,c".Split(',', StringSplitOptions.RemoveEmptyEntries)));
        Show("Split trim", Join(" a , b ,  , c ".Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        Show("Split count", Join("a b c d".Split(' ', 2)));
        Show("Split strings", Join("a<>b<=>c".Split(new[] { "<>", "<=>" }, StringSplitOptions.None)));
        Show("Split whitespace", Join(" a\tb\n c ".Split((char[])null, StringSplitOptions.RemoveEmptyEntries)));
        Show("Split string separator", Join("1::2::3".Split("::")));
        Show("Split empty string separator", Join("abc".Split("")));
        Show("Split many chars", Join("a1b2c3d".Split(new[] { '1', '2', '3' }, 3)));
        Show("Join enumerable", string.Join(", ", new List<int> { 1, 2, 3 }));
        Show("Join objects with null", string.Join("/", new object[] { "x", null, 3 }));
        Show("Join char separator", string.Join('-', "a", "b", "c"));
        Show("Join range", string.Join(";", new[] { "a", "b", "c", "d" }, 1, 2));
        Show("Concat enumerable", string.Concat(new List<string> { "x", null, "y" }));
        Show("Concat generic", string.Concat(new[] { 1, 2, 3 }));
        Show("Concat spans", string.Concat("ab".AsSpan(), "cd".AsSpan(), "ef".AsSpan()));
        Show("Create", string.Create(5, 'z', (span, c) => { for (int i = 0; i < span.Length; i++) span[i] = (char)(c - i); }));
        Show("IsNullOrWhiteSpace", string.IsNullOrWhiteSpace(" \u2028\t"));

        // Comparing
        Show("Compare", Math.Sign(string.Compare("apple", "Apple")));
        Show("Compare ignore case", string.Compare("apple", "APPLE", ignoreCase: true));
        Show("Compare ordinal", Math.Sign(string.CompareOrdinal("a", "B")));
        Show("Compare ordinal ranges", Math.Sign(string.CompareOrdinal("xxabc", 2, "abd", 0, 3)));
        Show("Compare substrings culture", string.Compare("Hello", 1, "jello", 1, 4, StringComparison.InvariantCulture));
        Show("Compare options", string.Compare("co-op", "coop", CultureInfo.InvariantCulture, CompareOptions.IgnoreSymbols));
        Show("Compare culture ignore case", string.Compare("STRASSE", "strasse", true, CultureInfo.InvariantCulture));

        // Errors: argument checks and messages
        Try("Substring past end", () => "abc".Substring(4));
        Try("Substring length", () => "abc".Substring(1, 5));
        Try("Remove negative", () => "abc".Remove(-1));
        Try("Insert null", () => "abc".Insert(0, null));
        Try("IndexOf bad start", () => "abc".IndexOf('a', 4));
        Try("IndexOf bad comparison", () => "abc".IndexOf("a", (StringComparison)9));
        Try("Replace empty", () => "abc".Replace("", "x"));
        Try("PadLeft negative", () => "abc".PadLeft(-1));
        Try("Split bad options", () => "abc".Split(',', (StringSplitOptions)8));
        Try("Split negative count", () => "abc".Split(new[] { ',' }, -1));
        Try("Join null", () => string.Join(",", (string[])null));
        Try("CopyTo range", () => { "abc".CopyTo(1, new char[2], 0, 3); return "copied"; });
        Try("ToCharArray range", () => "abc".ToCharArray(2, 2));
        Try("Compare bad length", () => string.Compare("a", 0, "b", 0, -1));
        string nothing = null;
        Try("Instance call on null", () => nothing.Trim());
        Try("Split on null", () => nothing.Split(','));

        var sb = new StringBuilder();
        foreach (var part in "x=1;y=2;z=3".Split(';'))
        {
            int eq = part.IndexOf('=');
            sb.Append(part.Substring(eq + 1)).Append(part.Substring(0, eq).ToUpperInvariant());
        }
        Show("Combined", sb);
    }
}
