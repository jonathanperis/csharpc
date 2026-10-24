// char's (string, index) predicates, UTF-32 conversions and CharUnicodeInfo.GetUnicodeCategory(string, int)
// (Roslyn's lexer and ObjectDisplay use them).
using System.Globalization;

var text = "a1 B_é٣!\t";
for (var i = 0; i < text.Length; i++)
    Console.WriteLine($"{i}: {char.IsDigit(text, i)} {char.IsLetter(text, i)} {char.IsLetterOrDigit(text, i)} {char.IsUpper(text, i)} {char.IsLower(text, i)} " +
        $"{char.IsNumber(text, i)} {char.IsPunctuation(text, i)} {char.IsSymbol(text, i)} {char.IsSeparator(text, i)} {char.IsWhiteSpace(text, i)} " +
        $"{char.IsControl(text, i)} {CharUnicodeInfo.GetUnicodeCategory(text, i)}");
Console.WriteLine($"{char.IsSurrogate("x😀", 1)} {char.IsHighSurrogate("x😀", 1)} {char.IsLowSurrogate("x😀", 2)} {char.IsSurrogatePair("😀", 0)}");

foreach (var cp in new[] { 0x41, 0xE9, 0x20AC, 0x1F600, 0x10FFFF })
{
    var s = char.ConvertFromUtf32(cp);
    Console.WriteLine($"{cp:X}: {s.Length} {char.ConvertToUtf32(s, 0):X} {(s.Length == 2 ? char.ConvertToUtf32(s[0], s[1]).ToString("X") : "-")}");
}
foreach (var bad in new[] { -1, 0xD800, 0x110000 })
{
    try { char.ConvertFromUtf32(bad); }
    catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
}
try { char.ConvertToUtf32("\ud83d", 0); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { char.ConvertToUtf32('a', 'b'); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { char.IsDigit("abc", 3); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { char.IsLetter(null!, 0); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { CharUnicodeInfo.GetUnicodeCategory("x", 5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
Console.WriteLine('q'.ToString(CultureInfo.InvariantCulture));
