// Array initializers of 16+ numeric constants are emitted as one constant table copied into the new
// array: every element type, negative values, extremes, NaN/-0.0, and a fresh copy per evaluation.
using System;
using System.Linq;

byte[] bytes = { 0, 1, 2, 127, 128, 200, 255, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
sbyte[] sbytes = { -128, -1, 0, 1, 127, -2, -3, -4, -5, -6, -7, -8, -9, -10, -11, -12 };
char[] chars = { 'a', 'b', 'c', '\0', '￿', 'é', 'z', 'y', 'x', 'w', 'v', 'u', 't', 's', 'r', 'q' };
short[] shorts = { short.MinValue, -1, 0, 1, short.MaxValue, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
ushort[] ushorts = { 0, 1, ushort.MaxValue, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
int[] ints = { int.MinValue, -1, 0, 1, int.MaxValue, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 'A' };
uint[] uints = { 0, 1, uint.MaxValue, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
long[] longs = { long.MinValue, -1, 0, 1, long.MaxValue, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, int.MinValue };
ulong[] ulongs = { 0, 1, ulong.MaxValue, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
float[] floats = { 0f, -0f, 1.5f, float.NaN, float.PositiveInfinity, float.MinValue, float.Epsilon, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
double[] doubles = { 0, -0.0, 1.5, double.NaN, double.NegativeInfinity, double.MaxValue, double.Epsilon, 1e-300, 4, 5, 6, 7, 8, 9, 10, 11 };

Console.WriteLine(string.Join(",", bytes));
Console.WriteLine(string.Join(",", sbytes));
Console.WriteLine(string.Join(",", chars.Select(c => (int)c)));
Console.WriteLine(string.Join(",", shorts) + " | " + string.Join(",", ushorts));
Console.WriteLine(string.Join(",", ints) + " | " + string.Join(",", uints));
Console.WriteLine(string.Join(",", longs) + " | " + string.Join(",", ulongs));
Console.WriteLine(string.Join(",", floats.Select(f => BitConverter.SingleToInt32Bits(f))));
Console.WriteLine(string.Join(",", doubles.Select(d => BitConverter.DoubleToInt64Bits(d))));

for (int round = 0; round < 2; round++)
{
    int[] fresh = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
    Console.WriteLine(fresh.Sum() + " " + fresh[0]);
    fresh[0] = 100; // must not leak into the next evaluation
}
