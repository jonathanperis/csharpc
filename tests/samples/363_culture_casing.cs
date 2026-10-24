// Culture casing (TextInfo): Turkish and Azerbaijani dotted/dotless i, invariant casing, char casing,
// ToTitleCase, and the casing the current culture gives string.ToUpper/ToLower.
using System;
using System.Globalization;

string[] texts = { "istanbul", "ISTANBUL", "Iİıi", "straße", "ǅungla", "ΣΊΣΥΦΟΣ", "ﬀ", "Ǆ", "àéîõü", "ǈ", "groß", "ﬁ" };
foreach (string name in new[] { "", "en-US", "tr-TR", "az-Latn-AZ", "lt-LT", "el-GR", "de-DE", "nl-NL" })
{
    var c = new CultureInfo(name);
    var ti = c.TextInfo;
    Console.Write($"[{name}] {ti.CultureName} {ti.ListSeparator} {ti.IsRightToLeft}:");
    foreach (string s in texts) Console.Write(" " + ti.ToUpper(s) + "/" + ti.ToLower(s));
    Console.WriteLine(" | " + ti.ToUpper('i') + ti.ToLower('I') + ti.ToUpper('ı') + ti.ToLower('İ') + " | " + ti.ToTitleCase("hello wORLD ijsselmeer ılık"));
    Console.WriteLine("   " + "istanbul".ToUpper(c) + " " + "ISTANBUL".ToLower(c) + " " + char.ToUpper('i', c) + char.ToLower('I', c));
}
Console.WriteLine("ıİ".ToUpperInvariant() + " " + "ıİ".ToLowerInvariant() + " " + char.ToUpperInvariant('ı') + char.ToLowerInvariant('İ') + " " + "Straße".ToUpperInvariant());
CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
Console.WriteLine("title".ToUpper() + " " + "TITLE".ToLower() + " " + char.ToUpper('i') + " " + "i".Equals("I", StringComparison.CurrentCultureIgnoreCase) + " " + "i".Equals("İ", StringComparison.CurrentCultureIgnoreCase));
CultureInfo.CurrentCulture = new CultureInfo("en-US");
Console.WriteLine("title".ToUpper() + " " + "TITLE".ToLower() + " " + char.ToUpper('i'));
