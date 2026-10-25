// System.Text.Rune (decoding with .NET's maximal-subpart rules, encoding, classification) and the
// static Uri helpers ASP.NET Core's path handling uses (EscapeDataString, IsHexEncoding).
using System.Buffers;
using System.Globalization;
using System.Text;

var a = new Rune('a');
var euro = new Rune(0x20AC);
var emoji = new Rune(0x1F600);
foreach (var r in new[] { a, euro, emoji, Rune.ReplacementChar })
    Console.WriteLine($"{r.Value:X} {r.IsAscii} {r.IsBmp} {r.Plane} {r.Utf16SequenceLength} {r.Utf8SequenceLength} {(r.IsBmp ? Rune.GetUnicodeCategory(r).ToString() : "supplementary")} {r.ToString().Length}");
Console.WriteLine($"{Rune.IsLetter(a)} {Rune.IsDigit(new Rune('7'))} {Rune.IsWhiteSpace(new Rune(' '))} {Rune.IsSymbol(euro)} {Rune.IsUpper(Rune.ToUpperInvariant(a))} {Rune.GetNumericValue(new Rune('9'))}");
Console.WriteLine($"{a < euro} {a.CompareTo(euro) < 0} {euro.Equals(new Rune('€'))} {euro == (Rune)0x20AC} {Rune.IsValid(0xD800)} {Rune.IsValid(0x10FFFF)}");

Span<byte> utf8 = stackalloc byte[4];
Span<char> utf16 = stackalloc char[2];
Console.WriteLine(Convert.ToHexString(utf8[..emoji.EncodeToUtf8(utf8)]) + " " + emoji.EncodeToUtf16(utf16) + " " + ((int)utf16[0]).ToString("X") + ((int)utf16[1]).ToString("X"));
Console.WriteLine($"{euro.TryEncodeToUtf8(utf8[..2], out int written)} {written}");

// Decoding valid and ill-formed input: status, scalar, units consumed.
byte[][] inputs = [[0x41], [0xC3, 0xA4], [0xE2, 0x82, 0xAC], [0xF0, 0x9F, 0x98, 0x80], [0xC3], [0xE2, 0x82], [0x80], [0xC0, 0x80], [0xED, 0xA0, 0x80], [0xF4, 0x90, 0x80, 0x80], []];
foreach (var input in inputs)
{
    var status = Rune.DecodeFromUtf8(input, out var rune, out int consumed);
    var last = Rune.DecodeLastFromUtf8(input, out var lastRune, out int lastConsumed);
    Console.WriteLine($"{Convert.ToHexString(input)}: {status} {rune.Value:X} {consumed} | {last} {lastRune.Value:X} {lastConsumed}");
}
foreach (var text in new[] { "x", "😀", "\ud83d", "\ude00a", "" })
{
    var status = Rune.DecodeFromUtf16(text, out var rune, out int consumed);
    var last = Rune.DecodeLastFromUtf16(text, out var lastRune, out int lastConsumed);
    Console.WriteLine($"{text.Length}: {status} {rune.Value:X} {consumed} | {last} {lastRune.Value:X} {lastConsumed}");
}
Console.WriteLine(Rune.GetRuneAt("a😀", 1).Value.ToString("X") + " " + Rune.TryGetRuneAt("a😀", 2, out _));
try { new Rune('\ud800'); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { new Rune(0x110000); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
try { Rune.GetRuneAt("\ude00", 0); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { emoji.EncodeToUtf16(utf16[..1]); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine(Rune.ToUpper(new Rune('i'), CultureInfo.InvariantCulture) + " " + Rune.ToLowerInvariant(new Rune('Q')));

Console.WriteLine(Uri.EscapeDataString("a b/c?d=é&€~-._") + " " + Uri.EscapeDataString("\ud800x".AsSpan()));
string plain = "plain-text_1.2~";
Console.WriteLine(ReferenceEquals(Uri.EscapeDataString(plain), plain));
Console.WriteLine($"{Uri.IsHexEncoding("a%2Fb", 1)} {Uri.IsHexEncoding("a%2", 1)} {Uri.IsHexEncoding("%zz", 0)} {Uri.IsHexDigit('f')} {Uri.IsHexDigit('g')}");
