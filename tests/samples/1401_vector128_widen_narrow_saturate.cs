// Vector128 widening, narrowing, saturating arithmetic, the *All/*Any comparisons, IEquatable equality (NaN equal to NaN) and the
// Vector64 loads and widenings the framework's ASCII/Latin-1 transcoding uses (#423): lane values, as .NET computes them.
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

static class Program
{
    static string Lanes<T>(Vector128<T> v) where T : struct
    {
        var parts = new string[Vector128<T>.Count];
        for (int i = 0; i < parts.Length; i++) parts[i] = Convert.ToString(v.GetElement(i), System.Globalization.CultureInfo.InvariantCulture)!;
        return string.Join(",", parts);
    }

    static string Lanes64<T>(Vector64<T> v) where T : struct
    {
        var parts = new string[Vector64<T>.Count];
        for (int i = 0; i < parts.Length; i++) parts[i] = Convert.ToString(v.GetElement(i), System.Globalization.CultureInfo.InvariantCulture)!;
        return string.Join(",", parts);
    }

    static bool SameVector<TVector>(TVector a, TVector b) where TVector : IEquatable<TVector> => a.Equals(b);

    static void Main()
    {
        var bytes = Vector128.Create((byte)0, 1, 2, 127, 128, 200, 254, 255, 10, 20, 30, 40, 50, 60, 70, 250);
        var sbytes = Vector128.Create((sbyte)-128, -1, 0, 1, 127, -50, 50, -2, 3, -3, 4, -4, 5, -5, 6, -6);
        Console.WriteLine(Lanes(Vector128.WidenLower(bytes)) + " | " + Lanes(Vector128.WidenUpper(bytes)));
        Console.WriteLine(Lanes(Vector128.WidenLower(sbytes)) + " | " + Lanes(Vector128.WidenUpper(sbytes)));
        var (lower, upper) = Vector128.Widen(bytes);
        Console.WriteLine(Lanes(lower) + " | " + Lanes(upper));
        var floats = Vector128.Create(1.5f, -2.25f, float.NaN, 3e38f);
        Console.WriteLine(Lanes(Vector128.WidenLower(floats)) + " | " + Lanes(Vector128.WidenUpper(floats)));
        var shorts = Vector128.Create((ushort)0, 255, 256, 511, 65535, 1000, 128, 7);
        Console.WriteLine(Lanes(Vector128.Narrow(shorts, Vector128.Create((ushort)300))));
        Console.WriteLine(Lanes(Vector128.Narrow(Vector128.Create(1.5, -1e300), Vector128.Create(double.NaN, 2.0))));
        Console.WriteLine(Lanes(Vector128.Narrow(Vector128.Create(-1, 70000, int.MinValue, 5), Vector128.Create(32767, 32768, -32769, 0))));
        Console.WriteLine(Lanes(Vector128.AddSaturate(bytes, Vector128.Create((byte)100))) + " | " + Lanes(Vector128.SubtractSaturate(bytes, Vector128.Create((byte)100))));
        Console.WriteLine(Lanes(Vector128.AddSaturate(sbytes, Vector128.Create((sbyte)100))) + " | " + Lanes(Vector128.SubtractSaturate(sbytes, Vector128.Create((sbyte)100))));
        Console.WriteLine(Lanes(Vector128.AddSaturate(Vector128.Create(1.5f), Vector128.Create(2f))));
        var small = Vector128.Create((ushort)5);
        Console.WriteLine($"{Vector128.LessThanOrEqualAll(small, Vector128.Create((ushort)5))} {Vector128.LessThanOrEqualAny(shorts, small)} {Vector128.GreaterThanOrEqualAll(shorts, small)} {Vector128.GreaterThanOrEqualAny(shorts, small)}");
        var nums = new Vector<ushort>(7);
        Console.WriteLine($"{Vector.LessThanOrEqualAll(nums, new Vector<ushort>(7))} {Vector.Narrow(nums, new Vector<ushort>(0x1FF))[0]}");
        Console.WriteLine($"{SameVector(bytes, bytes)} {SameVector(bytes, sbytes.AsByte())} {SameVector(floats, floats)} {floats.Equals(Vector128.Create(1.5f, -2.25f, float.NaN, 3e38f))} {floats == floats}");
        Console.WriteLine($"{SameVector(Vector128.Create(0.0), Vector128.Create(-0.0))} {Vector128.Create(double.NaN).Equals(Vector128.Create(double.NaN))}");

        // Vector64: load, widen and combine into a Vector128, and the halves of a Vector128.
        byte[] data = { 65, 66, 67, 200, 1, 2, 3, 255, 9 };
        var v64 = Vector64.LoadUnsafe(ref data[0]);
        var v64b = Vector64.LoadUnsafe(ref data[0], 1);
        Console.WriteLine(Lanes64(v64) + " | " + Lanes64(v64b));
        var (lo, hi) = Vector64.Widen(v64);
        Console.WriteLine(Lanes64(lo) + " | " + Lanes64(hi) + " | " + Lanes64(Vector64.WidenLower(v64)) + " | " + Lanes64(Vector64.WidenUpper(v64)));
        var combined = Vector128.Create(lo, hi);
        Console.WriteLine(Lanes(combined) + " | " + Lanes64(combined.GetLower()) + " | " + Lanes64(combined.GetUpper()));
        Console.WriteLine(Lanes(Vector128.Create(Vector64.Create((sbyte)-1), Vector64.Create((sbyte)2))));
    }
}
