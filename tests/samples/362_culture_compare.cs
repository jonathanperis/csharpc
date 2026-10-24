// Culture-sensitive string comparison, sorting and searching (ICU collation through CompareInfo):
// tailorings (Swedish å/ä/ö, German, Turkish dotless i, Spanish), ignorable characters, CompareOptions.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

string[] words = { "apple", "Banana", "éclair", "Zebra", "zebra", "ångström", "co-op", "coop", "é", "é", "Ä", "ä", "a", "z", "ö", "o", "1", "_", "ı", "i", "I", "İ", "ch", "cz", "ll", "lz", "ß", "ss", "Æ", "ae", "­soft" };
foreach (string name in new[] { "", "en-US", "sv-SE", "de-DE", "tr-TR", "es-ES", "da-DK", "cs-CZ", "fr-FR", "ja-JP" })
{
    var c = new CultureInfo(name);
    var sorted = (string[])words.Clone();
    Array.Sort(sorted, StringComparer.Create(c, false));
    Console.WriteLine($"[{name}] " + string.Join(" ", sorted));
    var ci = c.CompareInfo;
    Console.WriteLine("   " + ci.Compare("ä", "z") + " " + ci.Compare("Å", "z", CompareOptions.IgnoreCase) + " " + ci.Compare("i", "I", CompareOptions.IgnoreCase)
        + " " + ci.Compare("ı", "I", CompareOptions.IgnoreCase) + " " + ci.Compare("café", "cafe", CompareOptions.IgnoreNonSpace) + " " + ci.Compare("co-op", "coop", CompareOptions.IgnoreSymbols)
        + " " + ci.IndexOf("Straße", "SS", CompareOptions.IgnoreCase) + " " + ci.IndexOf("abc­def", "cd") + " " + ci.IsPrefix("İstanbul", "is", CompareOptions.IgnoreCase)
        + " " + ci.IsSuffix("naïve", "ive", CompareOptions.IgnoreNonSpace) + " " + ci.LastIndexOf("abcabc", "B", CompareOptions.IgnoreCase) + " " + ci.Name);
}
var list = new List<string>(words);
list.Sort();
Console.WriteLine(string.Join(" ", list));
Console.WriteLine(string.Join(" ", words.OrderBy(w => w, StringComparer.InvariantCultureIgnoreCase)));
Console.WriteLine(string.Join(" ", words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase)));
Console.WriteLine(string.Compare("é", "é") + " " + string.Compare("a", "B") + " " + string.Compare("a", "B", StringComparison.Ordinal) + " " + "x".CompareTo("X"));
Console.WriteLine("abc­def".IndexOf("cd") + " " + "abc­def".IndexOf("cd", StringComparison.Ordinal) + " " + "Hello".StartsWith("he", StringComparison.CurrentCultureIgnoreCase)
    + " " + "straße".Equals("STRASSE", StringComparison.InvariantCultureIgnoreCase) + " " + "\0abc".StartsWith("abc") + " " + "abc".EndsWith("­"));
Console.WriteLine(string.Compare("ä", "z", new CultureInfo("sv-SE"), CompareOptions.None) + " " + string.Compare("Ä", "ä", true, new CultureInfo("de-DE"))
    + " " + string.Compare("xxÅbc", 2, "ÅBc", 0, 3, new CultureInfo("sv-SE"), CompareOptions.IgnoreCase));
var dict = new Dictionary<string, int>(StringComparer.Create(new CultureInfo("de-DE"), true)) { ["Straße"] = 1 };
Console.WriteLine(dict.ContainsKey("STRASSE") + " " + dict.ContainsKey("straße") + " " + StringComparer.CurrentCulture.Equals("a", "A") + " " + StringComparer.FromComparison(StringComparison.CurrentCultureIgnoreCase).Equals("a", "A"));
CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
Console.WriteLine(string.Join(" ", words.OrderBy(w => w)) + " " + string.Compare("ä", "z") + " " + "Älg".StartsWith("ä", StringComparison.CurrentCultureIgnoreCase));
