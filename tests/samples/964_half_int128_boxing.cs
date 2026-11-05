// System.Half (conversions, arithmetic, comparisons, text over every bit pattern) and boxing / type tests
// of Int128 and UInt128.
using System;
using System.Collections.Generic;
using System.Globalization;

class Program
{
    static ulong Fnv(ulong h, string s)
    {
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        h ^= 10; h *= 1099511628211UL;
        return h;
    }

    static void Show(string name, Func<object> f)
    {
        try { Console.WriteLine(name + ": " + f()); }
        catch (Exception e) { Console.WriteLine(name + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static T Unbox<T>(object o) => (T)o;

    static void Main()
    {
        // ---- Int128 / UInt128 as objects ----
        object i = (Int128)42;
        Console.WriteLine(i is Int128 v ? v.ToString() : "no");
        object u = (UInt128)7;
        Console.WriteLine(u is UInt128 w ? w.ToString() : "no");
        Console.WriteLine(Unbox<Int128>((Int128)9));
        Console.WriteLine(Unbox<UInt128>((UInt128)10));
        Console.WriteLine(i + " " + u + " " + (Int128)(-5) + " " + UInt128.MaxValue);
        Console.WriteLine(i.Equals((Int128)42) + " " + i.Equals((Int128)43) + " " + i.Equals(u) + " " + i.Equals(null));
        Console.WriteLine(i.GetType().Name + " " + u.GetType().FullName);
        Console.WriteLine((i is UInt128) + " " + (u is Int128) + " " + (i is ValueType) + " " + (i is IComparable));
        object big = Int128.MaxValue;
        Console.WriteLine($"{big} {(object)Int128.MinValue}");
        Console.WriteLine(i.GetHashCode() == ((object)(Int128)42).GetHashCode());
        var list = new List<object> { (Int128)1, (UInt128)2, 3, "four" };
        foreach (var item in list) Console.WriteLine(item.GetType().Name + "=" + item);
        Show("IComparable", () => ((IComparable)i).CompareTo((Int128)41) + " " + ((IComparable)i).CompareTo((Int128)42) + " " + ((IComparable)i).CompareTo((Int128)43) + " " + ((IComparable)i).CompareTo(null)
            + " " + ((IComparable)u).CompareTo((UInt128)8) + " " + ((IFormattable)i).ToString("G", CultureInfo.InvariantCulture) + " " + ((IFormattable)u).ToString(null, null));
        Show("IComparable mismatch", () => ((IComparable)i).CompareTo(u));
        Show("IComparable<Int128>", () => ((IComparable<Int128>)i).CompareTo((Int128)(-1)) + " " + ((IEquatable<Int128>)i).Equals((Int128)42));
        Show("unbox wrong", () => (Int128)u);
        Show("unbox null", () => (Int128)(object)null);

        // ---- Half ----
        ReadOnlySpan<Half> hs = [(Half)1.5f, (Half)2];
        Console.WriteLine(hs[1].ToString());
        object h = (Half)0.5f;
        Console.WriteLine(h is Half hv ? hv.ToString() : "no");
        Console.WriteLine(h.GetType().Name + " " + (h is IComparable) + " " + h.Equals((Half)0.5f) + " " + h.Equals(0.5f));

        foreach (float f in new[] { 0f, -0f, 1f, 0.1f, 1.5f, 65504f, 65519f, 65520f, 100000f, 0.0001f, 0.00001f, 1e-7f, 6e-8f, 2.9802322e-8f, 3e-8f, 3.14159f, 12345f, 1234.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 2049f, 2051f, 4097f })
            Console.WriteLine(f.ToString(CultureInfo.InvariantCulture) + " -> " + ((Half)f).ToString(CultureInfo.InvariantCulture) + " | " + ((Half)f).ToString("R", CultureInfo.InvariantCulture) + " | "
                + ((Half)f).ToString("F2", CultureInfo.InvariantCulture) + " | " + ((Half)f).ToString("E3", CultureInfo.InvariantCulture) + " | " + ((Half)f).ToString("N1", CultureInfo.InvariantCulture)
                + " | " + ((Half)f).ToString("G3", CultureInfo.InvariantCulture) + " | " + ((Half)f).ToString("G", CultureInfo.InvariantCulture));

        // every bit pattern: text and the value as double
        ulong hash = 14695981039346656037UL, bitsHash = 14695981039346656037UL;
        int nan = 0, inf = 0, subnormal = 0;
        for (int bits = 0; bits < 65536; bits++)
        {
            var value = BitConverter.UInt16BitsToHalf((ushort)bits);
            hash = Fnv(hash, value.ToString(CultureInfo.InvariantCulture));
            double d = (double)value;
            bitsHash = Fnv(bitsHash, BitConverter.DoubleToInt64Bits(d).ToString(CultureInfo.InvariantCulture));
            // double -> Half -> bits is the identity for every non-NaN value
            if (!Half.IsNaN(value) && BitConverter.HalfToUInt16Bits((Half)d) != bits) Console.WriteLine("roundtrip mismatch " + bits);
            if (Half.IsNaN(value)) nan++;
            if (Half.IsInfinity(value)) inf++;
            if (Half.IsSubnormal(value)) subnormal++;
        }
        Console.WriteLine(hash + " " + bitsHash + " " + nan + " " + inf + " " + subnormal);

        // rounding of doubles and floats: ties, subnormals, overflow
        ulong roundHash = 14695981039346656037UL;
        var rng = new Random(12345);
        for (int n = 0; n < 20000; n++)
        {
            double d = Math.ScaleB(rng.NextDouble() + 0.5, rng.Next(-30, 18)) * (rng.Next(2) == 0 ? 1 : -1);
            roundHash = Fnv(roundHash, BitConverter.HalfToUInt16Bits((Half)d).ToString(CultureInfo.InvariantCulture));
            roundHash = Fnv(roundHash, BitConverter.HalfToUInt16Bits((Half)(float)d).ToString(CultureInfo.InvariantCulture));
        }
        Console.WriteLine(roundHash);
        foreach (double d in new[] { 65519.99, 65520.0, 65519.999999, 5.960464477539063E-08, 2.98023223876953125E-08, 2.980232238769532E-08, 2.9802322387695316E-08, 1.0004882812, 1.00048828125, 1.000732421875 })
            Console.WriteLine(d.ToString("R", CultureInfo.InvariantCulture) + " " + BitConverter.HalfToUInt16Bits((Half)d));

        // arithmetic, comparisons and predicates
        Half a = (Half)1.5f, b = (Half)0.1f, c = (Half)(-2);
        Console.WriteLine((a + b) + " " + (a - b) + " " + (a * b) + " " + (a / b) + " " + (a % (Half)0.4f) + " " + (-a) + " " + (+c));
        Console.WriteLine((a == b) + " " + (a != b) + " " + (a < b) + " " + (a > b) + " " + (a <= a) + " " + (a >= b));
        Console.WriteLine((Half.NaN == Half.NaN) + " " + (Half.NaN != Half.NaN) + " " + Half.NaN.Equals(Half.NaN) + " " + ((Half)0 == Half.NegativeZero) + " " + (Half.NaN < a));
        Console.WriteLine(a.CompareTo(b) + " " + b.CompareTo(a) + " " + a.CompareTo(a) + " " + Half.NaN.CompareTo(a) + " " + a.CompareTo(Half.NaN) + " " + Half.NaN.CompareTo(Half.NaN) + " " + a.CompareTo(null));
        Console.WriteLine(Half.MaxValue + " " + Half.MinValue + " " + Half.Epsilon + " " + Half.PositiveInfinity + " " + Half.NegativeInfinity + " " + Half.NaN + " " + Half.NegativeZero);
        Console.WriteLine(Half.IsNaN(Half.NaN) + " " + Half.IsInfinity(Half.PositiveInfinity) + " " + Half.IsFinite(a) + " " + Half.IsNegative(c) + " " + Half.IsNormal(a) + " " + Half.IsSubnormal(Half.Epsilon) + " " + Half.IsPositiveInfinity(a) + " " + Half.IsInteger((Half)3) + " " + Half.IsEvenInteger((Half)4) + " " + Half.IsOddInteger((Half)4));
        Console.WriteLine(Half.Abs(c) + " " + Half.Max(a, b) + " " + Half.Min(a, b) + " " + Half.Clamp((Half)5, a, (Half)3) + " " + Half.Sign(c) + " " + Half.Floor((Half)2.5f) + " " + Half.Ceiling((Half)2.5f) + " " + Half.Truncate((Half)(-2.5f)) + " " + Half.Round((Half)2.5f) + " " + Half.Sqrt((Half)2) + " " + Half.CopySign(a, c));
        Console.WriteLine((int)a + " " + (long)c + " " + (byte)(Half)200 + " " + (double)b + " " + (float)b + " " + (decimal)a + " " + (uint)(Half)3.9f + " " + (short)(Half)(-3.9f));
        Show("checked int", () => checked((int)(Half)3.7f));
        Show("checked byte overflow", () => checked((byte)(Half)300));
        Show("checked neg", () => checked((uint)(Half)(-1)));
        Show("sign NaN", () => Half.Sign(Half.NaN));
        Show("clamp bad", () => Half.Clamp((Half)1, (Half)3, (Half)2));
        Half inc = a; inc++; Half dec = a; dec--;
        Console.WriteLine(inc + " " + dec);
        Half fromInt = (Half)3; Half fromByte = (byte)7; Half fromSByte = (sbyte)(-7);
        Console.WriteLine(fromInt + " " + fromByte + " " + fromSByte + " " + (Half)(short)(-300) + " " + (Half)12345678L + " " + (Half)65504UL + " " + (Half)(decimal)1.25);
        Console.WriteLine(((Half)1.5f).GetHashCode() + " " + ((Half)0).GetHashCode() + " " + Half.NegativeZero.GetHashCode() + " " + Half.NaN.GetHashCode() + " " + ((Half)(-1.5f)).GetHashCode());
        Console.WriteLine(a.ToString("F1") + " " + b.ToString(CultureInfo.GetCultureInfo("de-DE")) + " " + b.ToString("G", CultureInfo.GetCultureInfo("fr-FR")) + " " + $"{a,8}|{b:F3}");
        Span<char> buffer = stackalloc char[16];
        Console.WriteLine(b.TryFormat(buffer, out int written, "G", CultureInfo.InvariantCulture) + " " + buffer.Slice(0, written).ToString() + " " + b.TryFormat(buffer.Slice(0, 2), out written));
        var sorted = new List<Half> { (Half)3, Half.NaN, (Half)(-1), (Half)2, Half.NegativeInfinity, (Half)0.5f };
        sorted.Sort();
        Console.WriteLine(string.Join(" ", sorted));
        var set = new HashSet<Half> { (Half)1, (Half)1, (Half)2, Half.NaN, Half.NaN, (Half)0, Half.NegativeZero };
        Console.WriteLine(set.Count);
    }
}
