// Number formatting: shortest round-trip, standard and custom formats, integers, decimals, composite.
using System;

double[] ds = { 0.125, 0.375, 2.5, 0.1 + 0.2, 0.07, 1.005, 123456.789, 1e-7, -0.0, 5e-324, 0.000123456,
    1234567890123456789.0, 1e15, 1e16, 1e17, 1234567890123456.0, 12345678901234567.0, -1.5, 1e21, 1.7976931348623157e308,
    2.2250738585072014e-308, 4.9406564584124654e-324, 0.1, 1.0 / 3, 2.0 / 3, 100, 1e23, 9007199254740993.0, 123.456e-300 };
foreach (var d in ds)
{
    Console.WriteLine($"{d} | {d:R} | {d:F2} | {d:F0} | {d:E3} | {d:e} | {d:N1} | {d:P1} | {d:G3} | {d:G17} | {d:C}");
    Console.WriteLine($"   {d:0.00} | {d:0.0000000000000000000} | {d:#,##0.###} | {d:0%} | {d:0.00E+00} | {d:#.#e0} | {d:0;(0);zero} | {d:'q'0.0\\#} | {d:0.0‰}");
}
float[] fs = { 0.1f, 1.5f, 16777216f, 1e10f, 0.3f, 1f / 3, 3.4028235e38f, 1e-45f, -2.5f, 1e8f, 1e9f, 123456.7f };
foreach (var f in fs)
    Console.WriteLine($"{f} | {f:R} | {f:F10} | {f:0.0000000000} | {f:G9} | {f:E2} | {f:N3}");
long[] ls = { 0, 7, -5, 125, 1000, 255, -1, long.MinValue, long.MaxValue, 12345678901234567, -999 };
foreach (var l in ls)
    Console.WriteLine($"{l} | {l:D8} | {l:X} | {l:x4} | {l:N0} | {l:N2} | {l:G2} | {l:E1} | {l:P0} | {l:F1} | {l:C0} | {l:0.0} | {l:#,#} | {l:00000} | {l:0;(0);zero} | {l:B}");
int[] ints = { 0, -1, 42, int.MinValue, int.MaxValue };
foreach (var i in ints) Console.WriteLine($"{i:X} {i:X8} {i:B} {(short)i:X} {(sbyte)i:X} {(byte)i:X2} {(uint)i:N0} {(ulong)(uint)i:D12}");
decimal[] ms = { 1.25m, 1.35m, -0.001m, 0.125m, 1.50m, 0.00m, -0.00m, 123456789.123456789m, -79228162514264337593543950335m, 0.0000123m, 1e-28m, 100m };
foreach (var m in ms)
    Console.WriteLine($"{m} | {m:F1} | {m:0.0} | {m:0.00} | {m:N2} | {m:E2} | {m:G3} | {m:P1} | {m:C} | {m:G} | {m:#,##0.##}");
Console.WriteLine(string.Format("{0,5}|{1,-5}|{2:F3}|{{x}}|{3:X4}|{4}", 42, "ab", Math.PI, 255, null));
Console.WriteLine(string.Format("{0:0.0} {1:N0} {2:P} {3} {4:E}", 1.25, 1234567L, 0.5f, 1.5m, 12345.6789m));
Console.WriteLine($"{3.14159,10:F2}|{-2.5,-8}|{1e100,12:G3}|{double.NaN}|{double.PositiveInfinity:F2}|{double.NegativeInfinity:0.0}|{float.NaN:E}");
Console.WriteLine(1.0.ToString() + " " + (-0.5).ToString() + " " + 1e-5.ToString() + " " + 0.0001.ToString() + " " + 123456789.0f.ToString());
object[] boxes = { 1, 2L, 3.5, 4.5f, 5.5m, (byte)6, (short)-7, 8u, 9ul, (sbyte)-10, (ushort)11 };
foreach (var b in boxes) Console.Write(string.Format("{0:F1} ", b));
Console.WriteLine();
try { Console.WriteLine(1.5.ToString("X")); } catch (FormatException e) { Console.WriteLine(e.Message); }
try { Console.WriteLine(string.Format("{1}", 1)); } catch (FormatException e) { Console.WriteLine(e.GetType().Name); }
foreach (var d in new[] { 0.0, -0.0, 1.0, -2.0, 0.1, 5e-324, 2.2250738585072014e-308, 1e300, 3.0, 0.75, double.NaN })
    Console.WriteLine($"{d:X} {d:x} {d:X3} {d:X0} {d:x20}");
foreach (var f in new[] { 0.1f, 1.5f, 1e-45f }) Console.WriteLine($"{f:X} {f:x2}");
