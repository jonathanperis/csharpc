// Min/Max of arrays of int, long, double and float (C# 14 binds `array.Min()` to MemoryExtensions' span
// overloads) and of the same values as List<T> (Enumerable): which NaN comes back (by its bits), signed
// zeros, infinities, extremes, long and short spans, and the empty-span message.
using System;
using System.Collections.Generic;
using System.Linq;

static class Program
{
    static string Bits(double d) => BitConverter.DoubleToInt64Bits(d).ToString("X16");
    static string Bits(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

    static void T(string label, Func<string> f)
    {
        try { Console.WriteLine(label + ": " + f()); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void Main()
    {
        double nanA = BitConverter.Int64BitsToDouble(0x7FF8000000000001);
        double nanB = BitConverter.Int64BitsToDouble(0x7FF8000000000002);
        float fnanA = BitConverter.Int32BitsToSingle(0x7FC00001);
        float fnanB = BitConverter.Int32BitsToSingle(0x7FC00002);

        double[][] doubles =
        {
            new[] { 3.0, nanA, -1.0, nanB },
            new[] { nanA, nanB },
            new[] { nanA, 5.0, nanB, 7.0 },
            new[] { 0.0, -0.0, 1.0 },
            new[] { -0.0, 0.0, -1.0 },
            new[] { 1.0, -0.0, 0.0, 2.0 },
            new[] { double.NegativeInfinity, double.PositiveInfinity },
            Enumerable.Range(0, 100).Select(i => (i * 37 % 101) - 50.0).ToArray(),
            Enumerable.Range(0, 100).Select(i => i == 77 ? nanB : (i * 37 % 101) - 50.0).ToArray(),
            Enumerable.Range(0, 99).Select(i => i == 98 ? -0.0 : i == 50 ? 0.0 : i + 1.0).ToArray(),
        };
        for (int i = 0; i < doubles.Length; i++)
        {
            double[] a = doubles[i];
            T("double[" + i + "] Min", () => Bits(a.Min()));
            T("double[" + i + "] Max", () => Bits(a.Max()));
            T("List<double>[" + i + "] Min", () => Bits(new List<double>(a).Min()));
            T("List<double>[" + i + "] Max", () => Bits(new List<double>(a).Max()));
            T("enumerated double[" + i + "] Min", () => Bits(a.Where(_ => true).Min()));
            T("enumerated double[" + i + "] Max", () => Bits(a.Where(_ => true).Max()));
        }

        float[][] floats =
        {
            new[] { 2f, fnanA, -3f, fnanB },
            new[] { fnanA, fnanB },
            new[] { fnanA, 1f, fnanB },
            new[] { 0f, -0f },
            new[] { -0f, 0f },
            Enumerable.Range(0, 37).Select(i => (i * 11 % 37) - 18f).ToArray(),
        };
        for (int i = 0; i < floats.Length; i++)
        {
            float[] a = floats[i];
            T("float[" + i + "] Min", () => Bits(a.Min()));
            T("float[" + i + "] Max", () => Bits(a.Max()));
            T("List<float>[" + i + "] Min", () => Bits(new List<float>(a).Min()));
            T("List<float>[" + i + "] Max", () => Bits(new List<float>(a).Max()));
            T("enumerated float[" + i + "] Min", () => Bits(a.Where(_ => true).Min()));
            T("enumerated float[" + i + "] Max", () => Bits(a.Where(_ => true).Max()));
        }

        int[] ints = Enumerable.Range(0, 1000).Select(i => (i * 7919 % 2003) - 1001).ToArray();
        ints[500] = int.MinValue;
        ints[501] = int.MaxValue;
        T("int[] Min", () => ints.Min().ToString());
        T("int[] Max", () => ints.Max().ToString());
        T("int[3] Min", () => new[] { 5, -2, 9 }.Min().ToString());
        long[] longs = ints.Select(i => (long)i << 20).ToArray();
        T("long[] Min", () => longs.Min().ToString());
        T("long[] Max", () => longs.Max().ToString());
        T("long[1] Max", () => new[] { long.MinValue }.Max().ToString());
        T("empty int[] Min", () => new int[0].Min().ToString());
        T("empty double[] Max", () => new double[0].Max().ToString());
        T("empty float[] Min", () => new float[0].Min().ToString());
    }
}
