// BFloat16 and Decimal32/64/128 beyond samples 1290 and 1291 (#405): generic math through T (INumber, IFloatingPointIeee754), parsing
// and formatting in UTF-16 and UTF-8 (Parse/TryParse/TryFormat with spans and byte spans, number styles, case-insensitive NaN and
// infinity symbols), conversions to and from every primitive including Int128/UInt128 and decimal, and edge values (NaN, infinities,
// subnormals, signed zeros, MaxValue), and the canonical and non-canonical encodings of the decimal types (an over-large significand,
// a NaN payload or infinity with trailing bits set, read through Unsafe.BitCast). The conversion from decimal to BFloat16 rounds once: (BFloat16)(decimal)(2^90 + 2^82 + 1) is
// 2^90 + 2^83, where rounding through double first (to 2^90 + 2^82, a BFloat16 tie) gives 2^90.
using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

static class Program
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    static void R(string label, Func<object> f)
    {
        try { Console.WriteLine(label + ": " + f()); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + " " + e.Message); }
    }

    static string Fp<T>(T x) where T : IFloatingPointIeee754<T> =>
        $"{x.ToString(null, Ci)} nan={T.IsNaN(x)} inf={T.IsInfinity(x)} neg={T.IsNegative(x)} sub={T.IsSubnormal(x)} norm={T.IsNormal(x)} int={T.IsInteger(x)}";

    static void Generic<T>(string name, T[] values) where T : IFloatingPointIeee754<T>, IMinMaxValue<T>
    {
        Console.WriteLine("== generic " + name);
        R("constants", () => $"{T.Zero} {T.One} {T.NegativeOne} {T.NegativeZero} {T.NaN} {T.PositiveInfinity} {T.NegativeInfinity} {T.Epsilon} {T.MaxValue} {T.MinValue}");
        foreach (var x in values)
        {
            var n = name + "(" + x.ToString(null, Ci) + ")";
            R("classify " + n, () => Fp(x));
            R("Abs/Sign " + n, () => (T.Abs(x), T.IsNaN(x) ? 0 : T.Sign(x)));
            R("Min/Max/MinMagnitude/MaxMagnitude " + n, () => (T.Min(x, T.One), T.Max(x, T.One), T.MinMagnitude(x, T.NegativeOne), T.MaxMagnitude(x, T.NegativeOne)));
            R("MinNumber/MaxNumber " + n, () => (T.MinNumber(x, T.NaN), T.MaxNumber(T.NaN, x)));
            R("+ - * / % " + n, () => (x + T.One, x - T.One, x * (T.One + T.One), x / (T.One + T.One + T.One), x % (T.One + T.One)));
            R("Round/Floor/Ceiling/Truncate " + n, () => (T.Round(x), T.Floor(x), T.Ceiling(x), T.Truncate(x), T.Round(x, MidpointRounding.AwayFromZero)));
            R("compare " + n, () => (x < T.One, x == T.Zero, x.CompareTo(T.One), x.Equals(x)));
            R("CopySign/Clamp " + n, () => (T.CopySign(T.One, x), T.IsNaN(x) ? x : T.Clamp(x, T.NegativeOne, T.One)));
            R("to int/long/Int128 saturating " + n, () => (int.CreateSaturating(x), long.CreateSaturating(x), Int128.CreateSaturating(x)));
            R("to double/float " + n, () => (double.CreateTruncating(x), float.CreateTruncating(x)));
            R("to int checked " + n, () => int.CreateChecked(x));
        }
        R("from int/long/UInt128/double", () => (T.CreateTruncating(12345), T.CreateTruncating(long.MaxValue), T.CreateTruncating(UInt128.MaxValue), T.CreateTruncating(0.1), T.CreateTruncating(-0.0)));
        R("from Int128 saturating/checked", () => (T.CreateSaturating(Int128.MinValue), T.CreateChecked((Int128)1 << 100)));
        R("from decimal", () => (T.CreateTruncating(1.5m), T.CreateTruncating(decimal.MaxValue), T.CreateTruncating(-0.0000000000000000000000000001m)));
    }

    static void Text<T>(string name, string[] inputs) where T : INumber<T>, IUtf8SpanParsable<T>, IUtf8SpanFormattable
    {
        Console.WriteLine("== text " + name);
        foreach (var s in inputs)
        {
            var label = name + " '" + s + "'";
            R("TryParse " + label, () => T.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, Ci, out var v) ? v.ToString(null, Ci) : "false");
            R("TryParse utf8 " + label, () => T.TryParse(Encoding.UTF8.GetBytes(s), NumberStyles.Float | NumberStyles.AllowThousands, Ci, out var v) ? v.ToString(null, Ci) : "false");
            R("Parse " + label, () => T.Parse(s, Ci).ToString(null, Ci));
        }
        var value = T.Parse("1234.5678", Ci);
        foreach (var format in new[] { "", "G", "G3", "R", "E", "E2", "F2", "N2", "P1", "0.##", "#,##0.000", "C" })
        {
            R($"format {name} '{format}'", () => value.ToString(format, Ci));
            R($"TryFormat utf8 {name} '{format}'", () =>
            {
                Span<byte> buffer = stackalloc byte[64];
                return value.TryFormat(buffer, out int written, format, Ci) ? Encoding.UTF8.GetString(buffer[..written]) : "false";
            });
            R($"TryFormat small {name} '{format}'", () =>
            {
                Span<char> buffer = stackalloc char[3];
                return value.TryFormat(buffer, out int written, format, Ci) ? new string(buffer[..written]) : "false";
            });
        }
    }

    static string Describe<T>(T x) where T : IFloatingPointIeee754<T> =>
        $"{x.ToString(null, Ci)} canonical={T.IsCanonical(x)} zero={T.IsZero(x)} nan={T.IsNaN(x)} inf={T.IsInfinity(x)} ==0 {x == T.Zero} hash==0 {x.GetHashCode() == T.Zero.GetHashCode()} +1 {(x + T.One).ToString(null, Ci)}";

    static void Encodings()
    {
        Console.WriteLine("== encodings");
        foreach (var bits in new uint[] { 0x32800000, 0x32800001, 0xB2800000, 0x6CBFFFFF, 0x6CB89680, 0x6CB8967F, 0x7C000000, 0x7C0FFFFF, 0x7C0F4240, 0x7E000001, 0x78000000, 0x78000001, 0xF9FFFFFF })
            R("Decimal32 " + bits.ToString("X8"), () => Describe(Unsafe.BitCast<uint, Decimal32>(bits)));
        foreach (var bits in new ulong[] { 0x31C0000000000000, 0x31C0000000000001, 0x6C7FFFFFFFFFFFFF, 0x6C7386F26FC0FFFF, 0x6C7386F26FC10000, 0x7C00000000000000, 0x7C03FFFFFFFFFFFF, 0x7800000000000001 })
            R("Decimal64 " + bits.ToString("X16"), () => Describe(Unsafe.BitCast<ulong, Decimal64>(bits)));
        foreach (var (hi, lo) in new (ulong, ulong)[] { (0x3040000000000000, 0), (0x3040000000000000, 1), (0x3041ED09BEAD87C0, 0x378D8E63FFFFFFFF), (0x3041ED09BEAD87C0, 0x378D8E6400000000), (0x6C00000000000000, 1), (0x7C00000000000000, 0), (0x7C003FFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF), (0x7800000000000000, 5) })
            R($"Decimal128 {hi:X16}{lo:X16}", () => Describe(Unsafe.BitCast<UInt128, Decimal128>(new UInt128(hi, lo))));
    }

    static void Main()
    {
        string[] texts = ["0", "-0", "1.5", "  42  ", "1,234.5", "1e10", "-1E-5", "nan", "NaN", "-nan", "infinity", "-Infinity", "INFINITY", "∞", "0x10", "", "abc", "1.0000000000000000000000000000000001"];

        Generic("BFloat16", [BFloat16.Zero, BFloat16.NegativeZero, BFloat16.One, (BFloat16)1.5f, (BFloat16)(-2.75f), (BFloat16)3.3e38f, BFloat16.Epsilon, BFloat16.MaxValue, BFloat16.MinValue, BFloat16.NaN, BFloat16.PositiveInfinity, BFloat16.NegativeInfinity, (BFloat16)1e-39f]);
        Text<BFloat16>("BFloat16", texts);
        Generic("Decimal32", [Decimal32.Zero, Decimal32.NegativeZero, Decimal32.One, Decimal32.Parse("2.50", Ci), Decimal32.Parse("-1234567", Ci), Decimal32.Epsilon, Decimal32.MaxValue, Decimal32.NaN, Decimal32.PositiveInfinity]);
        Text<Decimal32>("Decimal32", texts);
        Generic("Decimal64", [Decimal64.Zero, Decimal64.One, Decimal64.Parse("0.1", Ci), Decimal64.Parse("-9999999999999999", Ci), Decimal64.Epsilon, Decimal64.MaxValue, Decimal64.NegativeInfinity, Decimal64.NaN]);
        Text<Decimal64>("Decimal64", texts);
        Generic("Decimal128", [Decimal128.Zero, Decimal128.One, Decimal128.Parse("1.000", Ci), Decimal128.Parse("-0.000001", Ci), Decimal128.Epsilon, Decimal128.MaxValue, Decimal128.MinValue, Decimal128.NaN]);
        Text<Decimal128>("Decimal128", texts);

        Console.WriteLine("== decimal to BFloat16");
        decimal[] decimals = [0m, -0.0m, 1m, -1m, 0.1m, 1.5m, 255.5m, 256.5m, 257.5m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m,
            1237940039285380274899124224m + 4835703278458516698824704m + 1m,   // 2^90 + 2^82 + 1: rounds up, not to the tie
            1237940039285380274899124224m + 4835703278458516698824704m,        // 2^90 + 2^82: the tie, to even (2^90)
            1237940039285380274899124224m + 4835703278458516698824704m - 1m];  // just below the tie: down
        foreach (var d in decimals) R("(BFloat16)" + d, () => ((BFloat16)d).ToString(null, Ci) + " bits " + BitConverter.SingleToInt32Bits((float)(BFloat16)d).ToString("X8"));
        R("(decimal)BFloat16", () => ((decimal)(BFloat16)1.5f, (decimal)BFloat16.MaxValue));
        R("(decimal)BFloat16.NaN", () => (decimal)BFloat16.NaN);
        R("(Decimal128)decimal", () => ((Decimal128)1.25m, (Decimal128)decimal.MaxValue, (Decimal128)(-0.0000000000000000000000000001m)));
        R("(decimal)Decimal128", () => ((decimal)Decimal128.Parse("123.456", Ci), (decimal)Decimal128.Parse("1E30", Ci)));
        R("(decimal)Decimal64", () => (decimal)Decimal64.Parse("-0.000000000000000000000000000000001", Ci));
        R("Int128/UInt128 round trip", () => ((Int128)(Decimal128)Int128.MaxValue, (UInt128)(Decimal128)UInt128.MaxValue, (Decimal64)Int128.MinValue));
        Encodings();
        R("BFloat16 bits", () => (BitConverter.SingleToInt32Bits((float)BFloat16.Epsilon).ToString("X8"), BitConverter.SingleToInt32Bits((float)BFloat16.MaxValue).ToString("X8")));
    }
}
