// Integer TryFormat into UTF-16 and UTF-8 destinations (ISpanFormattable / IUtf8SpanFormattable, what
// interpolation handlers, RazorSlices' WriteNumber and Kestrel call) and Utf8Formatter: digits of every
// width, the extremes, destinations one too small (false, nothing written), negative numbers under a
// culture whose NegativeSign isn't '-', formats, and Utf8Formatter's 'D'/'X' with precision.
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

static class Program
{
    static string Utf16<T>(T value, int size, string format = null, IFormatProvider provider = null) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[size];
        buffer.Fill('#');
        bool ok = value.TryFormat(buffer, out int written, format, provider);
        return ok + " " + written + " '" + new string(buffer) + "'";
    }

    static string Utf8<T>(T value, int size, string format = null, IFormatProvider provider = null) where T : IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[size];
        buffer.Fill((byte)'#');
        bool ok = value.TryFormat(buffer, out int written, format, provider);
        return ok + " " + written + " '" + Encoding.UTF8.GetString(buffer) + "'";
    }

    static string Formatter(Func<Span<byte>, (bool, int)> format, int size)
    {
        var buffer = new byte[size];
        buffer.AsSpan().Fill((byte)'#');
        var (ok, written) = format(buffer);
        return ok + " " + written + " '" + Encoding.ASCII.GetString(buffer) + "'";
    }

    static void Main()
    {
        long p = 1;
        for (int digits = 1; digits <= 19; digits++, p *= 10)
        {
            Console.WriteLine($"{digits}: {Utf8(p, 20)} {Utf16(p - 1, 20)}");
        }
        Console.WriteLine("int.MaxValue " + Utf8(int.MaxValue, 10) + " " + Utf16(int.MaxValue, 10));
        Console.WriteLine("int.MaxValue short " + Utf8(int.MaxValue, 9) + " " + Utf16(int.MaxValue, 9));
        Console.WriteLine("ulong.MaxValue " + Utf8(ulong.MaxValue, 20) + " " + Utf16(ulong.MaxValue, 19));
        Console.WriteLine("zero " + Utf8(0, 1) + " " + Utf16(0, 0));
        Console.WriteLine("byte " + Utf8((byte)255, 3) + " sbyte " + Utf8((sbyte)-128, 4) + " short " + Utf16((short)-32768, 6));
        Console.WriteLine("ushort " + Utf16((ushort)65535, 5) + " uint " + Utf8(4000000000u, 10));
        Console.WriteLine("negative " + Utf8(-42, 3) + " " + Utf16(int.MinValue, 11) + " " + Utf8(long.MinValue, 19));

        var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        custom.NumberFormat.NegativeSign = "~";
        Console.WriteLine("custom culture " + Utf8(-42, 4, null, custom) + " " + Utf16(42, 4, null, custom) + " " + Utf16(-7L, 3, null, custom));
        Console.WriteLine("formats " + Utf8(42, 6, "D4") + " " + Utf16(255, 4, "X") + " " + Utf8(1234567, 12, "N0", CultureInfo.InvariantCulture));

        Console.WriteLine("Formatter int " + Formatter(b => (Utf8Formatter.TryFormat(-1234, b, out int w), w), 6));
        Console.WriteLine("Formatter short dest " + Formatter(b => (Utf8Formatter.TryFormat(-1234, b, out int w), w), 4));
        Console.WriteLine("Formatter long D8 " + Formatter(b => (Utf8Formatter.TryFormat(42L, b, out int w, new StandardFormat('D', 8)), w), 9));
        Console.WriteLine("Formatter X " + Formatter(b => (Utf8Formatter.TryFormat(-1, b, out int w, new StandardFormat('X')), w), 9));
        Console.WriteLine("Formatter x4 " + Formatter(b => (Utf8Formatter.TryFormat(0xABu, b, out int w, new StandardFormat('x', 4)), w), 5));
        Console.WriteLine("Formatter ulong max " + Formatter(b => (Utf8Formatter.TryFormat(ulong.MaxValue, b, out int w), w), 20));
        Console.WriteLine("Formatter long min " + Formatter(b => (Utf8Formatter.TryFormat(long.MinValue, b, out int w), w), 20));
        Console.WriteLine("Formatter byte X2 " + Formatter(b => (Utf8Formatter.TryFormat((byte)10, b, out int w, new StandardFormat('X', 2)), w), 2));
        Console.WriteLine("Formatter sbyte X " + Formatter(b => (Utf8Formatter.TryFormat((sbyte)-2, b, out int w, new StandardFormat('X')), w), 3));

        Console.WriteLine($"interpolated {12345} {-6} {0u} {ulong.MaxValue}");
    }
}
