#:property AllowUnsafeBlocks=true
// StringBuilder.Append(IFormatProvider, $"...") (Npgsql's NpgsqlCube.Write): holes formatted with the provider,
// its ICustomFormatter first; and TextEncoder's TextWriter, span and UTF-8 members (RazorSlices writes HTML
// with them), for the inbox HtmlEncoder and for an encoder derived from TextEncoder.
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

var de = CultureInfo.GetCultureInfo("de-DE");
var sb = new StringBuilder();
double x = 1234.5;
sb.Append(CultureInfo.InvariantCulture, $"({x}, {x:N1}, {-0.25})");
sb.Append(' ').Append(de, $"[{x}|{x,10:F2}|{"text",-6}|{(ReadOnlySpan<char>)"span"}]");
sb.AppendLine(new Upper(), $" {"shout"} {42}");
sb.Append((IFormatProvider?)null, $"{x}!");
Console.WriteLine(sb.ToString());

var html = HtmlEncoder.Default;
var writer = new StringWriter();
html.Encode(writer, "a<b & 'c' + \"d\" é 😀", 0, 15);
writer.Write('|');
html.Encode(writer, "x>y".ToCharArray(), 1, 2);
writer.Write('|');
html.Encode(writer, "plain");
Console.WriteLine(writer);
Span<char> chars = stackalloc char[64];
Console.WriteLine(html.Encode("<tag>".AsSpan(), chars, out int consumed, out int written) + $" {consumed} {written} {chars[..written].ToString()}");
Console.WriteLine(html.Encode("<tag>".AsSpan(), chars[..6], out consumed, out written) + $" {consumed} {written}");
Span<byte> bytes = stackalloc byte[64];
var source = Encoding.UTF8.GetBytes("1 < 2 é \"q\"");
Console.WriteLine(html.EncodeUtf8(source, bytes, out consumed, out written) + $" {consumed} {written} {Encoding.UTF8.GetString(bytes[..written])}");
Console.WriteLine(html.EncodeUtf8(new byte[] { 0x61, 0xC3 }, bytes, out consumed, out written, isFinalBlock: false) + $" {consumed} {written}");
Console.WriteLine(html.EncodeUtf8(new byte[] { 0x61, 0xFF, 0x62 }, bytes, out consumed, out written) + $" {consumed} {written} {Encoding.UTF8.GetString(bytes[..written])}");
Console.WriteLine(html.FindFirstCharacterToEncodeUtf8("abc<"u8) + " " + html.FindFirstCharacterToEncodeUtf8("abc"u8));
try { html.Encode(writer, "abc", 2, 5); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }

var custom = new BracketEncoder();
var w2 = new StringWriter();
custom.Encode(w2, "a#b#c", 0, 5);
Console.WriteLine(w2 + " " + custom.Encode("##") + " " + custom.EncodeUtf8("x#y"u8, bytes, out consumed, out written) + " " + Encoding.UTF8.GetString(bytes[..written]));

sealed class Upper : IFormatProvider, ICustomFormatter
{
    public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
    public string Format(string? format, object? arg, IFormatProvider? formatProvider) => arg is string s ? s.ToUpperInvariant() : $"<{arg}>";
}

// Encodes '#' as "[#]" and lets everything else through.
sealed class BracketEncoder : TextEncoder
{
    public override int MaxOutputCharactersPerInputCharacter => 3;
    public override bool WillEncode(int unicodeScalar) => unicodeScalar == '#';
    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        for (int i = 0; i < textLength; i++) if (text[i] == '#') return i;
        return -1;
    }
    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        string text = unicodeScalar == '#' ? "[#]" : char.ConvertFromUtf32(unicodeScalar);
        numberOfCharactersWritten = 0;
        if (text.Length > bufferLength) return false;
        for (int i = 0; i < text.Length; i++) buffer[i] = text[i];
        numberOfCharactersWritten = text.Length;
        return true;
    }
}
