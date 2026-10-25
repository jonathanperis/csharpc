// System.Text.Encoding as .NET has it: replacement/exception/custom fallbacks, UTF-8's maximal-subpart
// rule for invalid bytes, stateful Decoder/Encoder (split sequences and surrogate pairs), Convert,
// Clone/IsReadOnly, GetEncoding's names, Latin-1's best-fit fallback, and a derived encoding.
using System;
using System.Linq;
using System.Text;

static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
static string Chars(string s) => string.Join(" ", s.Select(c => ((int)c).ToString("X4")));

// Invalid UTF-8: one U+FFFD per maximal subpart.
byte[][] invalid =
{
    [0xC0, 0x80], [0xE0, 0x80, 0x80], [0xED, 0xA0, 0x80], [0xF4, 0x90, 0x80, 0x80], [0xE2, 0x82], [0xF0, 0x9F, 0x98],
    [0x41, 0xE2, 0x28, 0xA1], [0xFF, 0xFE], [0xF0, 0x9F, 0x98, 0x80], [0xE2, 0x82, 0xAC, 0x80],
};
foreach (var bytes in invalid)
    Console.WriteLine($"utf-8 {Hex(bytes)}: {Chars(Encoding.UTF8.GetString(bytes))} (count {Encoding.UTF8.GetCharCount(bytes)})");

// Lone surrogates and unencodable chars.
string tricky = "a\uD800b\uDC00c😀éā€";
foreach (var e in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.ASCII, Encoding.Latin1 })
    Console.WriteLine($"{e.WebName}: {Hex(e.GetBytes(tricky))} -> {Chars(e.GetString(e.GetBytes(tricky)))}");
Console.WriteLine($"utf-16 odd byte: {Chars(Encoding.Unicode.GetString([0x41, 0x00, 0x42]))}, lone: {Chars(Encoding.Unicode.GetString([0x00, 0xD8, 0x41, 0x00]))}");
Console.WriteLine($"latin1 best fit: {Encoding.Latin1.GetString(Encoding.Latin1.GetBytes("Āć–“Ａ中"))}");

// Exception fallbacks.
var strict = new UTF8Encoding(false, true);
try { strict.GetString([0x41, 0xC3]); }
catch (DecoderFallbackException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message} [{Hex(e.BytesUnknown)}] at {e.Index}"); }
try { strict.GetBytes("x\uD800"); }
catch (EncoderFallbackException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message} {(int)e.CharUnknown:X4} at {e.Index}"); }
var asciiStrict = Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
try { asciiStrict.GetBytes("café"); }
catch (EncoderFallbackException e) { Console.WriteLine($"ascii: {e.Message}"); }
try { asciiStrict.GetString([0x41, 0x80]); }
catch (DecoderFallbackException e) { Console.WriteLine($"ascii: {e.Message}"); }

// Custom replacement fallbacks and read-only instances.
var custom = (Encoding)Encoding.ASCII.Clone();
Console.WriteLine($"read-only: {Encoding.ASCII.IsReadOnly} clone {custom.IsReadOnly}");
custom.EncoderFallback = new EncoderReplacementFallback("[?]");
custom.DecoderFallback = new DecoderReplacementFallback("<bad>");
Console.WriteLine($"custom: {Encoding.ASCII.GetString(custom.GetBytes("naïve 😀"))} {custom.GetString([0x61, 0xC8, 0x62])} max {custom.GetMaxByteCount(4)} {custom.GetMaxCharCount(4)}");
try { Encoding.ASCII.EncoderFallback = EncoderFallback.ExceptionFallback; }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
try { new EncoderReplacementFallback("\uD800"); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine($"equals: {Encoding.UTF8.Equals(new UTF8Encoding(true))} {Encoding.UTF8.Equals(new UTF8Encoding(false))} {custom.Equals(Encoding.ASCII)} {new UnicodeEncoding(true, false).Equals(Encoding.BigEndianUnicode)}");

// Stateful decoding: a sequence split between calls.
var decoder = Encoding.UTF8.GetDecoder();
byte[] euro = [0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x98, 0x80];
var chars = new char[8];
int total = 0;
foreach (var chunk in new[] { euro[..1], euro[1..4], euro[4..6], euro[6..] })
{
    int count = decoder.GetCharCount(chunk, 0, chunk.Length);
    int n = decoder.GetChars(chunk, 0, chunk.Length, chars, total);
    Console.WriteLine($"chunk {Hex(chunk)}: count {count} got {n}");
    total += n;
}
Console.WriteLine($"decoded {Chars(new string(chars, 0, total))}");
var flushing = Encoding.UTF8.GetDecoder();
Console.WriteLine($"incomplete then flush: {flushing.GetChars([0xE2, 0x82], 0, 2, chars, 0, false)} {flushing.GetChars([], 0, 0, chars, 0, true)} {Chars(new string(chars, 0, 1))}");

// Decoder.Convert into a small buffer.
var converter = Encoding.Unicode.GetDecoder();
byte[] utf16 = Encoding.Unicode.GetBytes("hello 😀!");
var small = new char[3];
int offset = 0;
while (offset < utf16.Length)
{
    converter.Convert(utf16, offset, utf16.Length - offset, small, 0, small.Length, false, out int bytesUsed, out int charsUsed, out bool completed);
    Console.WriteLine($"convert: {bytesUsed} bytes -> {Chars(new string(small, 0, charsUsed))} completed {completed}");
    offset += bytesUsed;
}

// Stateful encoding: a surrogate pair split between calls, and the flush of a dangling high surrogate.
var encoder = Encoding.UTF8.GetEncoder();
var buffer = new byte[16];
int a = encoder.GetBytes(['x', '\uD83D'], 0, 2, buffer, 0, false);
int b = encoder.GetBytes(['\uDE00'], 0, 1, buffer, a, false);
Console.WriteLine($"encoder: {a} + {b}: {Hex(buffer[..(a + b)])}");
int c = encoder.GetBytes(['\uD83D'], 0, 1, buffer, 0, false);
int d = encoder.GetBytes([], 0, 0, buffer, c, true);
Console.WriteLine($"dangling: {c} + {d}: {Hex(buffer[..(c + d)])}");
encoder.Convert("abcdef".ToCharArray(), 0, 6, buffer, 0, 4, true, out int cu, out int bu, out bool done);
Console.WriteLine($"encoder convert: {cu} chars {bu} bytes completed {done}");

// Overflow and argument errors.
try { Encoding.UTF8.GetBytes("€", 0, 1, new byte[2], 0); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { Encoding.UTF8.GetString(new byte[3], 2, 2); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { Encoding.UTF8.GetByteCount((string)null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
Console.WriteLine($"max: {Encoding.UTF8.GetMaxByteCount(10)} {Encoding.UTF8.GetMaxCharCount(10)} {Encoding.Unicode.GetMaxByteCount(10)} {Encoding.Unicode.GetMaxCharCount(11)} {Encoding.ASCII.GetMaxCharCount(10)} {Encoding.Latin1.GetMaxByteCount(10)}");

// GetEncoding by name and code page.
foreach (var name in new[] { "utf-8", "UTF-16LE", "unicodeFFFE", "latin1", "ascii", "us", "utf8", "utf-7", "x-unknown" })
{
    try { var e = Encoding.GetEncoding(name); Console.WriteLine($"{name}: {e.CodePage} {e.WebName} {e.EncodingName} {e.BodyName} {e.HeaderName}"); }
    catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message}"); }
}
foreach (var cp in new[] { 0, 1200, 1201, 20127, 28591, 65001, 1, 437, 70000 })
{
    try { Console.WriteLine($"{cp}: {Encoding.GetEncoding(cp).WebName}"); }
    catch (Exception e) { Console.WriteLine($"{cp}: {e.GetType().Name}: {e.Message}"); }
}

// A derived encoding: the base class's members run on its overrides; its coders are the defaults.
var rot = new Rot13Encoding();
Console.WriteLine($"rot13: {Hex(rot.GetBytes("Hello"))} {rot.GetString(rot.GetBytes("Hello"))} {rot.GetByteCount("abc")} {rot.GetCharCount([1, 2])}");
var rotDecoder = rot.GetDecoder();
Console.WriteLine($"rot13 decoder: {rotDecoder.GetCharCount([0x41], 0, 1)} {Encoding.Convert(rot, Encoding.UTF8, rot.GetBytes("Uryyb"))[0]}");
try { _ = rot.WebName; }
catch (NotSupportedException e) { Console.WriteLine(e.Message); }

sealed class Rot13Encoding : Encoding
{
    public Rot13Encoding() : base(0) { }
    private static char Rot(char c) => c is >= 'a' and <= 'z' ? (char)('a' + (c - 'a' + 13) % 26) : c is >= 'A' and <= 'Z' ? (char)('A' + (c - 'A' + 13) % 26) : c;
    public override int GetByteCount(char[] chars, int index, int count) => count;
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        for (int i = 0; i < charCount; i++) bytes[byteIndex + i] = (byte)Rot(chars[charIndex + i]);
        return charCount;
    }
    public override int GetCharCount(byte[] bytes, int index, int count) => count;
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        for (int i = 0; i < byteCount; i++) chars[charIndex + i] = Rot((char)bytes[byteIndex + i]);
        return byteCount;
    }
    public override int GetMaxByteCount(int charCount) => charCount;
    public override int GetMaxCharCount(int byteCount) => byteCount;
}
