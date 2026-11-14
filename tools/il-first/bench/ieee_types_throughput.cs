// BFloat16 and Decimal32/64/128 served from the framework's CoreLib IL (#405): arithmetic, conversions, and UTF-8 / UTF-16 formatting
// and parsing round trips. The program behind that change's speed numbers against NativeAOT (tools/il-first/measure-aot.sh). Checksums only.
using System;
using System.Globalization;
using System.Numerics;

static class Program
{
    static long Work<T>(T[] values, int rounds) where T : IFloatingPointIeee754<T>, IUtf8SpanParsable<T>, IUtf8SpanFormattable
    {
        var ci = CultureInfo.InvariantCulture;
        Span<byte> utf8 = stackalloc byte[64];
        Span<char> utf16 = stackalloc char[64];
        long sum = 0;
        for (var r = 0; r < rounds; r++)
            foreach (var x in values)
            {
                var y = x * x + T.One;
                sum += long.CreateSaturating(T.Round(y / (x + T.One + T.One)));
                if (y.TryFormat(utf8, out int written, default, ci) && T.TryParse(utf8[..written], ci, out var back)) sum += written + (back == y ? 1 : 0);
                if (x.TryFormat(utf16, out int chars, "E3", ci)) sum += chars;
            }
        return sum;
    }

    static T[] Values<T>(int count) where T : IFloatingPointIeee754<T>
    {
        var values = new T[count];
        uint state = 2463534242;
        for (var i = 0; i < count; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            values[i] = T.CreateTruncating((double)(state % 100000) / 64.0);
        }
        return values;
    }

    static void Main()
    {
        const int Count = 1 << 14, Rounds = 20;
        long total = Work(Values<BFloat16>(Count), Rounds) + Work(Values<Decimal32>(Count), Rounds)
                   + Work(Values<Decimal64>(Count), Rounds) + Work(Values<Decimal128>(Count), Rounds);
        Console.WriteLine(total);
    }
}
