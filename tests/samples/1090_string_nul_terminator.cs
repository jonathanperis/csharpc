#:property AllowUnsafeBlocks=true
// Every string ends with a 0 char after its last one (`fixed (char* p = s)` and native code reading wide strings rely on
// it): string literals of every length (the object's padding supplies it on some layouts, not on all), and strings the
// runtime and the library build: new string(char, n), Concat, Substring, Join, Replace, Insert, PadLeft, ToString of
// numbers, interpolation, StringBuilder, string.Create, ToUpper/ToLower, Trim.
using System.Text;

int bad = 0;
unsafe void Check(string s, string what)
{
    fixed (char* p = s)
        if (p[s.Length] != 0) { bad++; Console.WriteLine($"no terminator: {what} length {s.Length}"); }
}

string[] literals = ["", "a", "ab", "abc", "abcd", "abcde", "abcdef", "abcdefg", "abcdefgh", "abcdefghi", "abcdefghij", "abcdefghijk", "abcdefghijkl"];
foreach (var l in literals) Check(l, "literal");

for (int n = 0; n <= 40; n++)
{
    Check(new string('x', n), "new string");
    Check(string.Concat(new string('a', n), "b"), "Concat");
    Check("0123456789012345678901234567890123456789".Substring(0, n), "Substring");
    Check(string.Join(",", new string[n]), "Join");
    Check(new string('q', n).Replace("q", "rr"), "Replace");
    Check(new string('z', n).Insert(n / 2, "--"), "Insert");
    Check("k".PadLeft(n + 1), "PadLeft");
    Check(n.ToString(), "int ToString");
    Check($"{n}-{n}", "interpolation");
    Check(new StringBuilder().Append('s', n).ToString(), "StringBuilder");
    Check(string.Create(n, 'c', (span, c) => span.Fill(c)), "string.Create");
    Check(new string('a', n).ToUpperInvariant(), "ToUpper");
    Check(("  " + new string('t', n) + "  ").Trim(), "Trim");
    Check(((long)n * 100000007).ToString(), "long ToString");
}
Check(Guid.Empty.ToString(), "Guid");
Check(1.5.ToString(), "double");
Check(DateTime.UnixEpoch.ToString("yyyy-MM-dd"), "DateTime");
Console.WriteLine($"strings without a terminator: {bad}");
