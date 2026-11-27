// System.Numerics.BigInteger and Complex served from System.Runtime.Numerics' IL (#423): arithmetic, generic math,
// bit operations, conversions, parsing and formatting, with .NET's exceptions.
using System;
using System.Globalization;
using System.Numerics;

static class Program
{
    static T SumOf<T>(T[] values) where T : INumber<T>
    {
        T sum = T.Zero;
        foreach (var v in values) sum += v;
        return sum;
    }

    static void Try(string label, Func<object> f)
    {
        try { Console.WriteLine($"{label}: {f()}"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        BigInteger a = BigInteger.Pow(3, 200) - 17, b = -BigInteger.Pow(2, 130) + 5;
        Console.WriteLine(a);
        Console.WriteLine(a * b);
        Console.WriteLine(BigInteger.DivRem(a, b, out var rem) + " " + rem);
        Console.WriteLine(BigInteger.ModPow(a, 65537, BigInteger.Pow(10, 30) + 7));
        Console.WriteLine(BigInteger.GreatestCommonDivisor(a * 6, b * 9));
        Console.WriteLine(BigInteger.Log(a).ToString("F6") + " " + BigInteger.Log10(a).ToString("F6") + " " + BigInteger.Log(a, 7).ToString("F6"));
        Console.WriteLine((a >> 100) + " " + (b << 3) + " " + (b >>> 5));
        Console.WriteLine((a & b) + " " + (a | b) + " " + (a ^ b) + " " + ~b);

        // Generic math over BigInteger
        Console.WriteLine(SumOf(new BigInteger[] { a, b, 1 }));
        Console.WriteLine(BigInteger.PopCount(a) + " " + BigInteger.LeadingZeroCount(b) + " " + BigInteger.TrailingZeroCount(b - 5));
        Console.WriteLine(BigInteger.IsPow2(BigInteger.Pow(2, 77)) + " " + BigInteger.Log2(a) + " " + BigInteger.RotateLeft(new BigInteger(0x80000001u), 4));
        Console.WriteLine(BigInteger.CreateChecked(UInt128.MaxValue) + " " + BigInteger.CreateTruncating(-1.5e30) + " " + Int128.CreateSaturating(a));
        Console.WriteLine(BigInteger.Clamp(a, -10, 10) + " " + BigInteger.CopySign(a, b).Sign + " " + BigInteger.MaxMagnitude(a, b).Sign);
        Console.WriteLine(BigInteger.IsEvenInteger(a) + " " + BigInteger.IsNegative(b) + " " + BigInteger.Abs(b));

        // Conversions
        Console.WriteLine((double)a + " " + (float)b + " " + (decimal)(a % BigInteger.Pow(10, 25)));
        Console.WriteLine((long)(b >> 70) + " " + (ulong)(a & ulong.MaxValue) + " " + (UInt128)(a >> 200));
        Console.WriteLine(new BigInteger(1e300 / 7) + " " + new BigInteger(-123.99m));
        byte[] bytes = b.ToByteArray();
        Console.WriteLine(Convert.ToHexString(bytes) + " " + new BigInteger(bytes) + " " + b.GetByteCount(isUnsigned: false) + " " + b.GetBitLength());
        Console.WriteLine(Convert.ToHexString(a.ToByteArray(isUnsigned: true, isBigEndian: true)));

        // Parsing and formatting
        Console.WriteLine(BigInteger.Parse("-0001234567890123456789012345678901234567890"));
        Console.WriteLine(BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber));
        Console.WriteLine(BigInteger.Parse("(1,234,567)", NumberStyles.Number | NumberStyles.AllowParentheses));
        Console.WriteLine(BigInteger.TryParse("12e3", NumberStyles.Float, null, out var parsed) + " " + parsed);
        Console.WriteLine(a.ToString("X") + " " + b.ToString("X") + " " + b.ToString("x40"));
        Console.WriteLine(a.ToString("N0") + " " + b.ToString("E10") + " " + a.ToString("D80") + " " + b.ToString("C"));
        Console.WriteLine(new BigInteger(255).ToString("B") + " " + BigInteger.MinusOne.ToString("b12"));
        Span<char> buffer = stackalloc char[8];
        Console.WriteLine(a.TryFormat(buffer, out int written) + " " + written + " " + new BigInteger(-42).TryFormat(buffer, out written) + " " + buffer[..written].ToString());
        Console.WriteLine($"{a,70:N0}|{b,-10}|");

        // Boxed
        object boxed = a;
        Console.WriteLine(boxed.Equals(a) + " " + (boxed is IComparable<BigInteger> c && c.CompareTo(b) > 0) + " " + ((IFormattable)boxed).ToString("E3", null));
        Console.WriteLine(a.GetHashCode() == (BigInteger.Pow(3, 200) - 17).GetHashCode());

        // Complex
        var z = new Complex(3, -4);
        Console.WriteLine(Complex.Abs(z) + " " + (z * Complex.Conjugate(z)).ToString(CultureInfo.InvariantCulture) + " " + Complex.Sqrt(z).ToString("F4", CultureInfo.InvariantCulture));
        Console.WriteLine(Complex.Exp(Complex.ImaginaryOne * Math.PI).ToString("F3", CultureInfo.InvariantCulture) + " " + Complex.Pow(z, 2).ToString(CultureInfo.InvariantCulture));

        Try("Divide by zero", () => a / BigInteger.Zero);
        Try("Parse bad", () => BigInteger.Parse("12x"));
        Try("Parse null", () => BigInteger.Parse(null));
        Try("Explicit to int", () => (int)a);
        Try("From NaN", () => new BigInteger(double.NaN));
        Try("Negative exponent", () => BigInteger.Pow(a, -1));
        Try("Bad format", () => a.ToString("Q"));
        Try("Log negative", () => BigInteger.Log(b));
    }
}
