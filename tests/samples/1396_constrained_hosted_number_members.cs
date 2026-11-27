// Interface members of built-in number types reached through a generic constraint (ISpanFormattable, IUtf8SpanFormattable, IFormattable):
// the implementation is the type's own member, hosted from the framework's IL. The call must reach it directly; boxing and dispatching
// reached the boxed type's corelib thunk, which made the same constrained call again (a stack overflow for decimal, as in
// string.Format's "{0:F2}" of a boxed decimal).
using System;
using System.Globalization;
using System.Text;

static class Program
{
    static string Span<T>(T value, string format) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[96];
        bool ok = value.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture);
        return ok ? new string(buffer[..written]) : "(too small)";
    }

    static string Small<T>(T value, string format) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[3];
        return value.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture) ? new string(buffer[..written]) : "(too small)";
    }

    static string Utf8<T>(T value, string format) where T : IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[96];
        return value.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture) ? Encoding.UTF8.GetString(buffer[..written]) : "(too small)";
    }

    static string Text<T>(T value, string format, IFormatProvider provider) where T : IFormattable => value.ToString(format, provider);

    // A boxed number through the interface, as StringBuilder.AppendFormat and the interpolation handlers reach it.
    static string Boxed(object value, string format)
    {
        Span<char> buffer = stackalloc char[96];
        return value is ISpanFormattable formattable && formattable.TryFormat(buffer, out int written, format, CultureInfo.InvariantCulture)
            ? new string(buffer[..written]) : "(not formatted)";
    }

    static void Main()
    {
        Console.WriteLine(Boxed(1.25m, "F1") + " " + Boxed(-7.5, "F0") + " " + Boxed(42, "D5") + " " + Boxed((byte)200, "X"));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F2} {1,8:N1} {2:E2}", 3.14159m, 1234.56m, 0.000123m));
        Console.WriteLine(Span(1234.5678m, "F2") + " " + Span(-0.5m, "C") + " " + Span(79228162514264337593543950335m, "N0"));
        Console.WriteLine(Span(Math.PI, "R") + " " + Span(1e-7, "G") + " " + Span(2.5f, "E3"));
        Console.WriteLine(Span(int.MinValue, "X") + " " + Span((short)-1, "X") + " " + Span(ulong.MaxValue, "N0"));
        Console.WriteLine(Span(Int128.MinValue, "D") + " " + Span((Half)0.1, "R"));
        Console.WriteLine(Small(1234.5m, "F2") + " " + Small(12, "D"));
        Console.WriteLine(Utf8(-1234.5m, "F3") + " " + Utf8(0.1 + 0.2, "R") + " " + Utf8(255, "x4"));
        var de = CultureInfo.GetCultureInfo("de-DE");
        Console.WriteLine(Text(1234567.891m, "N2", de) + " | " + Text(1234567.891, "N2", de) + " | " + Text(-42L, "C", CultureInfo.InvariantCulture));
    }
}
