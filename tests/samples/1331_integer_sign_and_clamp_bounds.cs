// Two silent differences of csharpc's integer intrinsics (#391, #392): byte.Sign and short.Sign returned the value itself (CompareTo's
// rule for small types: the difference), and Math.Clamp / T.Clamp with min > max returned a value where .NET throws ArgumentException
// ("'9' cannot be greater than 1."), for every numeric type. A NaN bound compares false, so it never throws.
using System;
using System.Numerics;

static class Program
{
    static void R(string label, Func<object> f)
    {
        try { Console.WriteLine(label + ": " + f()); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + " " + e.Message); }
    }

    static T ClampOf<T>(T value, T min, T max) where T : INumber<T> => T.Clamp(value, min, max);

    static void Main()
    {
        R("byte.Sign(5)", () => byte.Sign(5));
        R("byte.Sign(200)", () => byte.Sign(200));
        R("short.Sign(5)", () => short.Sign(5));
        R("short.Sign(-300)", () => short.Sign(-300));
        R("ushort.Sign(40000)", () => ushort.Sign(40000));
        R("sbyte.Sign(-7)", () => sbyte.Sign(-7));
        R("int.Sign(-9)", () => int.Sign(-9));
        R("Math.Sign((short)-300)", () => Math.Sign((short)-300));

        R("Math.Clamp(int)", () => Math.Clamp(5, 9, 1));
        R("Math.Clamp(long)", () => Math.Clamp(5L, 9L, 1L));
        R("Math.Clamp(short)", () => Math.Clamp((short)5, (short)9, (short)1));
        R("Math.Clamp(sbyte)", () => Math.Clamp((sbyte)5, (sbyte)9, (sbyte)-1));
        R("Math.Clamp(uint)", () => Math.Clamp(5u, 9u, 1u));
        R("Math.Clamp(ulong)", () => Math.Clamp(5ul, 9ul, 1ul));
        R("Math.Clamp(byte)", () => Math.Clamp((byte)5, (byte)9, (byte)1));
        R("Math.Clamp(ushort)", () => Math.Clamp((ushort)5, (ushort)9, (ushort)1));
        R("Math.Clamp(nint)", () => Math.Clamp((nint)5, (nint)9, (nint)1));
        R("Math.Clamp(nuint)", () => Math.Clamp((nuint)5, (nuint)9, (nuint)1));
        R("Math.Clamp(double)", () => Math.Clamp(5.0, 9.5, 1.25));
        R("Math.Clamp(float)", () => Math.Clamp(5f, 9.5f, 1.25f));
        R("Math.Clamp(decimal)", () => Math.Clamp(5m, 9m, 1m));
        R("int.Clamp", () => int.Clamp(5, 9, 1));
        R("byte.Clamp", () => byte.Clamp(5, 9, 1));
        R("long.Clamp", () => long.Clamp(5, 9, 1));
        R("T.Clamp<int>", () => ClampOf(5, 9, 1));
        R("T.Clamp<ushort>", () => ClampOf((ushort)5, (ushort)9, (ushort)1));
        R("T.Clamp<double>", () => ClampOf(5.0, 9.0, 1.0));

        // Not thrown: equal bounds, ordered bounds, NaN bounds.
        R("Math.Clamp(int, equal bounds)", () => Math.Clamp(5, 3, 3));
        R("Math.Clamp(int, in range)", () => (Math.Clamp(5, 1, 9), Math.Clamp(-5, 1, 9), Math.Clamp(50, 1, 9)));
        R("Math.Clamp(uint, in range)", () => (Math.Clamp(5u, 1u, 9u), Math.Clamp(0u, 1u, 9u), Math.Clamp(50u, 1u, 9u)));
        R("Math.Clamp(double, NaN bounds)", () => (Math.Clamp(5.0, double.NaN, 1.0), Math.Clamp(5.0, 9.0, double.NaN)));
        R("Math.Clamp(double, NaN value)", () => Math.Clamp(double.NaN, 1.0, 9.0));
    }
}
