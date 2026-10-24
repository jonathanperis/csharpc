// IFormattable.ToString(format, provider) on boxed primitives honours the format and the provider
// (each primitive's own formatter); char ignores both.
using System.Globalization;

var german = new CultureInfo("de-DE");
var custom = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
custom.NegativeSign = "~";
custom.NumberDecimalSeparator = "_";
object[] values = [-1234567, 42L, (short)-7, (sbyte)-8, (byte)200, (ushort)65000, 4000000000u, 18000000000000000000ul, -1234.5678, 2.5f, 'x'];

static string Try(IFormattable f, string? format, IFormatProvider? provider)
{
    try { return f.ToString(format, provider); }
    catch (FormatException e) { return e.GetType().Name; }
}

foreach (var value in values)
{
    var f = (IFormattable)value;
    Console.WriteLine($"{value.GetType().Name}: [{Try(f, null, null)}] [{Try(f, "", german)}] [{Try(f, "N2", german)}] [{Try(f, "N1", custom)}] [{Try(f, null, custom)}] [{Try(f, "X", null)}]");
}
