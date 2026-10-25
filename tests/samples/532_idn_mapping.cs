// System.Globalization.IdnMapping (Punycode): GetAscii/GetUnicode, the label checks and their
// exceptions (docs-standard-base-types-how-to-verify-that-strings-are-in-valid-email-format).
using System;
using System.Globalization;
using System.Text.RegularExpressions;

var idn = new IdnMapping();
string[] names = { "contoso.中国", "bücher.example", "example.com", "Example.COM", "münchen.de", "日本語.jp", "xn--fsq.com",
    "a.b.c", "πόλη.gr", "café", "שלום.co.il", "abc١.com", "-start.com" };
foreach (var name in names)
{
    try { Console.WriteLine($"{name} -> {idn.GetAscii(name)}"); }
    catch (ArgumentException e) { Console.WriteLine($"{name} -> {e.GetType().Name}: {e.Message}"); }
}
foreach (var ascii in new[] { "xn--fiqs8s", "xn--bcher-kva.example", "xn--mnchen-3ya.de", "example.com", "xn--zz", "xn--wgv71a119e.jp" })
{
    try { Console.WriteLine($"{ascii} <- {idn.GetUnicode(ascii)}"); }
    catch (ArgumentException e) { Console.WriteLine($"{ascii} <- {e.GetType().Name}: {e.Message}"); }
}

string[] bad = { "", "a..b", new string('a', 64) + ".com", "a\u0000", "ab\u0001c.com" };
foreach (var name in bad)
{
    try { Console.WriteLine(idn.GetAscii(name)); }
    catch (ArgumentException e) { Console.WriteLine($"{name.Length} chars: {e.GetType().Name}: {e.Message}"); }
}
try { idn.GetAscii(null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { idn.GetAscii("abc", 4); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }

var std3 = new IdnMapping { UseStd3AsciiRules = true };
foreach (var name in new[] { "under_score.com", "ok-name.com", "-bad.com" })
{
    try { Console.WriteLine($"std3 {name} -> {std3.GetAscii(name)}"); }
    catch (ArgumentException e) { Console.WriteLine($"std3 {name} -> {e.Message}"); }
}
Console.WriteLine($"{idn.Equals(new IdnMapping())} {idn.Equals(std3)} {idn.GetHashCode()} {std3.GetHashCode()} {idn.AllowUnassigned}");

Span<char> buffer = stackalloc char[64];
Console.WriteLine(idn.TryGetAscii("bücher.example", buffer, out int written) ? buffer[..written].ToString() : "too small");
Console.WriteLine(idn.TryGetAscii("bücher.example", buffer[..5], out written) + " " + written);

// The docs' email check: the domain goes through IdnMapping inside a Regex evaluator.
foreach (var email in new[] { "david.jones@proseware.com", "js@contoso.中国", "js@proseware..com", "j_9@[129.126.118.1]" })
{
    string normalized;
    try
    {
        normalized = Regex.Replace(email, @"(@)(.+)$", m => m.Groups[1].Value + new IdnMapping().GetAscii(m.Groups[2].Value), RegexOptions.None, TimeSpan.FromMilliseconds(200));
    }
    catch (ArgumentException)
    {
        Console.WriteLine($"Invalid: {email}");
        continue;
    }
    Console.WriteLine($"{(Regex.IsMatch(normalized, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase) ? "Valid" : "Invalid")}: {email} ({normalized})");
}
