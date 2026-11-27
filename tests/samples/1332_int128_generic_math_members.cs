// Int128's and UInt128's static generic-math members (#393): Log2, PopCount, LeadingZeroCount, TrailingZeroCount, Sign, IsPow2,
// Abs, CopySign, Clamp, Min/Max, the magnitude members, DivRem, rotates, the Is* predicates and CreateChecked/Saturating/Truncating
// from and to the other primitives, called directly and through generic code (T.Log2(x) with T = Int128), over edge values that set
// only the lower half, only the upper half, both, and the extremes. csharpc serves them from the types' CoreLib IL
// (src/Csharpc/upstream-types/System.IntegerMembers.txt), whose code reads the two 64-bit halves (_lower, _upper) of a value csharpc
// keeps as one 128-bit integer.
using System;
using System.Numerics;

static class Program
{
    static void R(string label, Func<object> f)
    {
        try { Console.WriteLine(label + ": " + f()); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + " " + e.Message); }
    }

    static readonly Int128[] Signed =
    [
        0, 1, -1, 5, -5, 64, long.MaxValue, long.MinValue, (Int128)ulong.MaxValue, (Int128)1 << 64, ((Int128)1 << 100) + 3,
        -((Int128)1 << 100), Int128.MaxValue, Int128.MinValue, Int128.MinValue + 1,
    ];

    static readonly UInt128[] Unsigned =
    [
        0, 1, 5, 64, ulong.MaxValue, (UInt128)1 << 64, ((UInt128)1 << 100) + 3, (UInt128)1 << 127, UInt128.MaxValue, UInt128.MaxValue - 1,
    ];

    static void Direct()
    {
        foreach (var x in Signed)
        {
            var n = "Int128(" + x + ")";
            R("Int128.Log2 " + n, () => Int128.Log2(x));
            R("Int128.PopCount " + n, () => Int128.PopCount(x));
            R("Int128.LeadingZeroCount " + n, () => Int128.LeadingZeroCount(x));
            R("Int128.TrailingZeroCount " + n, () => Int128.TrailingZeroCount(x));
            R("Int128.Sign " + n, () => Int128.Sign(x));
            R("Int128.IsPow2 " + n, () => Int128.IsPow2(x));
            R("Int128.Abs " + n, () => Int128.Abs(x));
            R("Int128.IsNegative/IsPositive " + n, () => (Int128.IsNegative(x), Int128.IsPositive(x)));
            R("Int128.IsEvenInteger/IsOddInteger " + n, () => (Int128.IsEvenInteger(x), Int128.IsOddInteger(x)));
            R("Int128.RotateLeft/RotateRight " + n, () => (Int128.RotateLeft(x, 3), Int128.RotateRight(x, 3), Int128.RotateLeft(x, 64)));
            R("Int128.CopySign " + n, () => (Int128.CopySign(x, -1), Int128.CopySign(x, 1)));
            R("Int128.Clamp " + n, () => Int128.Clamp(x, -1000, 1000));
            R("Int128.Min/Max " + n, () => (Int128.Min(x, 7), Int128.Max(x, 7)));
            R("Int128.MinMagnitude/MaxMagnitude " + n, () => (Int128.MinMagnitude(x, -7), Int128.MaxMagnitude(x, -7)));
            R("Int128.DivRem " + n, () => Int128.DivRem(x, 7));
            R("Int128 to int saturating/truncating " + n, () => (Sat<int, Int128>(x), int.CreateTruncating(x)));
            R("Int128 to long checked " + n, () => long.CreateChecked(x));
            R("Int128 to ulong saturating/truncating " + n, () => (ulong.CreateSaturating(x), ulong.CreateTruncating(x)));
            R("Int128 to UInt128 saturating/truncating " + n, () => (UInt128.CreateSaturating(x), UInt128.CreateTruncating(x)));
            R("Int128 to double " + n, () => double.CreateTruncating(x));
        }
        foreach (var x in Unsigned)
        {
            var n = "UInt128(" + x + ")";
            R("UInt128.Log2 " + n, () => UInt128.Log2(x));
            R("UInt128.PopCount " + n, () => UInt128.PopCount(x));
            R("UInt128.LeadingZeroCount " + n, () => UInt128.LeadingZeroCount(x));
            R("UInt128.TrailingZeroCount " + n, () => UInt128.TrailingZeroCount(x));
            R("UInt128.Sign " + n, () => UInt128.Sign(x));
            R("UInt128.IsPow2 " + n, () => UInt128.IsPow2(x));
            R("UInt128.IsEvenInteger/IsOddInteger " + n, () => (UInt128.IsEvenInteger(x), UInt128.IsOddInteger(x)));
            R("UInt128.RotateLeft/RotateRight " + n, () => (UInt128.RotateLeft(x, 3), UInt128.RotateRight(x, 3), UInt128.RotateLeft(x, 64)));
            R("UInt128.Clamp " + n, () => UInt128.Clamp(x, 10, 1000));
            R("UInt128.Min/Max " + n, () => (UInt128.Min(x, 7), UInt128.Max(x, 7)));
            R("UInt128.DivRem " + n, () => UInt128.DivRem(x, 7));
            R("UInt128 to int saturating/truncating " + n, () => (int.CreateSaturating(x), int.CreateTruncating(x)));
            R("UInt128 to ulong checked " + n, () => ulong.CreateChecked(x));
            R("UInt128 to Int128 checked " + n, () => Int128.CreateChecked(x));
            R("UInt128 to Int128 saturating/truncating " + n, () => (Int128.CreateSaturating(x), Int128.CreateTruncating(x)));
            R("UInt128 to double " + n, () => double.CreateTruncating(x));
        }

        // From the other primitives.
        R("Int128.CreateChecked(int/long/uint/ulong/byte)", () => (Int128.CreateChecked(-5), Int128.CreateChecked(long.MinValue), Int128.CreateChecked(uint.MaxValue), Int128.CreateChecked(ulong.MaxValue), Int128.CreateChecked((byte)200)));
        R("Int128.CreateSaturating(double)", () => (Int128.CreateSaturating(1e40), Int128.CreateSaturating(-1e40), Int128.CreateSaturating(double.NaN), Int128.CreateSaturating(-2.5)));
        R("Int128.CreateTruncating(double)", () => (Int128.CreateTruncating(1e20), Int128.CreateTruncating(-2.5)));
        R("Int128.CreateChecked(1e40)", () => Int128.CreateChecked(1e40));
        R("UInt128.CreateChecked(int/long/uint/ulong/byte)", () => (UInt128.CreateChecked(5), UInt128.CreateChecked(long.MaxValue), UInt128.CreateChecked(uint.MaxValue), UInt128.CreateChecked(ulong.MaxValue), UInt128.CreateChecked((byte)200)));
        R("UInt128.CreateChecked(-1)", () => UInt128.CreateChecked(-1));
        R("UInt128.CreateSaturating(int/long/double)", () => (UInt128.CreateSaturating(-1), UInt128.CreateSaturating(long.MinValue), UInt128.CreateSaturating(1e40), UInt128.CreateSaturating(double.NaN)));
        R("UInt128.CreateTruncating(int/long/sbyte)", () => (UInt128.CreateTruncating(-1), UInt128.CreateTruncating(long.MinValue), UInt128.CreateTruncating((sbyte)-2)));
        R("Int128.Abs(MinValue)", () => Int128.Abs(Int128.MinValue));
        R("Int128.Clamp(min > max)", () => Int128.Clamp(5, 9, 1));
        R("UInt128.Clamp(min > max)", () => UInt128.Clamp(5, 9, 1));
        R("Int128.DivRem(x, 0)", () => Int128.DivRem(5, 0));
    }

    static T Sat<T, TFrom>(TFrom x) where T : INumberBase<T> where TFrom : INumberBase<TFrom> => T.CreateSaturating(x);

    static void Generic<T>(T x) where T : IBinaryInteger<T>
    {
        var n = typeof(T).Name + "(" + x + ")";
        R("T.Log2 " + n, () => T.Log2(x));
        R("T.PopCount " + n, () => T.PopCount(x));
        R("T.LeadingZeroCount " + n, () => T.LeadingZeroCount(x));
        R("T.TrailingZeroCount " + n, () => T.TrailingZeroCount(x));
        R("T.Sign " + n, () => T.Sign(x));
        R("T.IsPow2 " + n, () => T.IsPow2(x));
        R("T.Abs " + n, () => T.Abs(x));
        R("T.IsNegative/IsPositive/IsZero " + n, () => (T.IsNegative(x), T.IsPositive(x), T.IsZero(x)));
        R("T.IsEvenInteger/IsOddInteger " + n, () => (T.IsEvenInteger(x), T.IsOddInteger(x)));
        R("T.IsCanonical/IsComplexNumber/IsFinite/IsImaginaryNumber " + n, () => (T.IsCanonical(x), T.IsComplexNumber(x), T.IsFinite(x), T.IsImaginaryNumber(x)));
        R("T.IsInfinity/IsInteger/IsNaN/IsNegativeInfinity " + n, () => (T.IsInfinity(x), T.IsInteger(x), T.IsNaN(x), T.IsNegativeInfinity(x)));
        R("T.IsNormal/IsPositiveInfinity/IsRealNumber/IsSubnormal " + n, () => (T.IsNormal(x), T.IsPositiveInfinity(x), T.IsRealNumber(x), T.IsSubnormal(x)));
        R("T.Clamp/Min/Max/CopySign " + n, () => (T.Clamp(x, T.Zero, T.One), T.Min(x, T.One), T.Max(x, T.One), T.CopySign(x, T.One)));
        R("T.MaxMagnitude/MinMagnitude " + n, () => (T.MaxMagnitude(x, T.One), T.MinMagnitude(x, T.One)));
        R("T.MaxMagnitudeNumber/MinMagnitudeNumber/MaxNumber/MinNumber " + n, () => (T.MaxMagnitudeNumber(x, T.One), T.MinMagnitudeNumber(x, T.One), T.MaxNumber(x, T.One), T.MinNumber(x, T.One)));
        R("T.DivRem " + n, () => T.DivRem(x, T.CreateTruncating(3)));
        R("T.RotateLeft/RotateRight " + n, () => (T.RotateLeft(x, 1), T.RotateRight(x, 1)));
        R("T.CreateSaturating(300)/(-1) " + n, () => (T.CreateSaturating(300), T.CreateSaturating(-1)));
        R("T.CreateChecked(5UL) " + n, () => T.CreateChecked(5UL));
        R("T.CreateTruncating(-1L) " + n, () => T.CreateTruncating(-1L));
        R("long.CreateSaturating(x) " + n, () => long.CreateSaturating(x));
    }

    static void Main()
    {
        Direct();
        foreach (var x in Signed) Generic(x);
        foreach (var x in Unsigned) Generic(x);
    }
}
