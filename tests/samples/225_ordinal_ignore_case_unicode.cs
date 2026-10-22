// StringComparison.OrdinalIgnoreCase folds with the full simple upper-case mapping (not just
// ASCII), including surrogate pairs; Compare returns the difference of the first differing
// upper-cased characters. StringComparer.OrdinalIgnoreCase hashes consistently with Equals.
using System;
using System.Collections.Generic;

string a = "Владимир", b = "ВЛАДИМИР";
Console.WriteLine(string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Equals(a, b, StringComparison.OrdinalIgnoreCase));
Console.WriteLine("straße".Equals("STRAßE", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("ÉCOLE", "école", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Equals("Ωμέγα", "ΩΜΈΓΑ", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Equals("ǆemal", "ǄEMAL", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Equals("ﬀ", "FF", StringComparison.OrdinalIgnoreCase)); // no full case folding
Console.WriteLine(string.Compare("ı", "I", StringComparison.OrdinalIgnoreCase)); // dotless i has no simple upper
Console.WriteLine(string.Compare("ſ", "S", StringComparison.OrdinalIgnoreCase)); // nor long s, for ordinal casing
Console.WriteLine(string.Compare("é", "a", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("a", "Ω", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("ä", "z", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("abc", "ABCD", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("Abé", "aBÉx", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("b", "A", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("Ärger", "ärgerlich", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare(null, "x", StringComparison.OrdinalIgnoreCase) + " " + string.Compare("x", null, StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Equals(null, null, StringComparison.OrdinalIgnoreCase) + " " + "x".Equals(null, StringComparison.OrdinalIgnoreCase));

// Surrogate pairs (Deseret, Adlam): case pairs outside the BMP.
Console.WriteLine(string.Equals("\U00010428\U00010429", "\U00010400\U00010401", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("\U0001E922", "\U0001E900", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(string.Compare("\U00010428", "\U00010401", StringComparison.OrdinalIgnoreCase));
Console.WriteLine(Math.Sign(string.Compare("\U00010428", "a", StringComparison.OrdinalIgnoreCase)));
Console.WriteLine(string.Compare("\uDC00", "\uD800", StringComparison.OrdinalIgnoreCase)); // lone surrogates compare as is

// Every Latin-1 / Latin Extended-A / Greek / Cyrillic letter equals its upper and lower case.
int mismatches = 0, letters = 0;
foreach (var (from, to) in new[] { (0xC0, 0x24F), (0x370, 0x3FF), (0x400, 0x52F) })
    for (int c = from; c <= to; c++)
    {
        string s = ((char)c).ToString();
        if (!char.IsLetter((char)c)) continue;
        letters++;
        if (!string.Equals(s.ToUpperInvariant(), s.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            && s.ToUpperInvariant() != s && s.ToLowerInvariant() != s) mismatches++;
    }
Console.WriteLine($"{letters} letters, {mismatches} mismatches");

var comparer = StringComparer.OrdinalIgnoreCase;
Console.WriteLine(comparer.Equals("Ωμέγα", "ΩΜΈΓΑ") + " " + comparer.Compare("éa", "ÉB"));
Console.WriteLine(comparer.GetHashCode("Ωμέγα") == comparer.GetHashCode("ΩΜΈΓΑ"));
Console.WriteLine(comparer.GetHashCode("\U00010428x") == comparer.GetHashCode("\U00010400X"));
var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
foreach (var word in new[] { "Straße", "STRASSE", "straße", "Владимир", "ВЛАДИМИР", "\U00010428", "\U00010400" })
    counts[word] = counts.TryGetValue(word, out var n) ? n + 1 : 1;
foreach (var (k, v) in counts) Console.WriteLine($"{k}: {v}");
var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ärger", "ÄRGER", "ärger", "arger" };
Console.WriteLine(set.Count);

try { string.Compare("a", "b", (StringComparison)42); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { "a".Equals("b", (StringComparison)(-1)); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
