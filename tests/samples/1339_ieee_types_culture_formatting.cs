// BFloat16 and Decimal32/64/128 (served from the framework's CoreLib IL, #405) formatted and parsed with a named culture's symbols:
// the generic number code reads the group and decimal separators, the signs and the currency symbol (a multi-byte € in UTF-8) of de-DE through
// NumberFormatInfo, in UTF-16 and UTF-8.
using System;
using System.Globalization;
using System.Numerics;
using System.Text;

static class Program
{
    static void Show<T>(string name, string text) where T : INumber<T>, IUtf8SpanParsable<T>, IUtf8SpanFormattable
    {
        var value = T.Parse(text, CultureInfo.InvariantCulture);
        foreach (var culture in new[] { "de-DE" })
        {
            var ci = CultureInfo.GetCultureInfo(culture);
            foreach (var format in new[] { "N2", "C", "E3", "P1", "G" })
            {
                var s = value.ToString(format, ci);
                Span<byte> utf8 = stackalloc byte[64];
                var u = value.TryFormat(utf8, out var written, format, ci) ? Encoding.UTF8.GetString(utf8[..written]) : "false";
                Console.WriteLine($"{name} {culture} {format}: {s} | utf8 {u} | utf8 matches {s == u}");
            }
            var local = value.ToString("N3", ci);
            Console.WriteLine($"{name} {culture} round trip '{local}': {T.TryParse(local, NumberStyles.Number, ci, out var back)} {back.ToString(null, CultureInfo.InvariantCulture)}"
                + $" utf8 {T.TryParse(Encoding.UTF8.GetBytes(local), NumberStyles.Number, ci, out var back8)} {back8.ToString(null, CultureInfo.InvariantCulture)}");
            Console.WriteLine($"{name} {culture} negative: {(-value).ToString("N1", ci)}");
        }
    }

    static void Main()
    {
        Show<BFloat16>("BFloat16", "1234.5678");
        Show<Decimal32>("Decimal32", "1234.5678");
        Show<Decimal64>("Decimal64", "-98765.4321");
        Show<Decimal128>("Decimal128", "12345678901234567890.125");
    }
}
