// System.Math members of double that were missing (Acosh/Asinh/Atanh, BitIncrement/BitDecrement, FusedMultiplyAdd, ILogB,
// MaxMagnitude/MinMagnitude, SinCos, Sign's NaN error) and the NaN bits Log, Log2 and double.IsNegative report.
using System;

static string F(double v) => v.ToString("R");
static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

double[] values = { 0, -0d, 0.5, 1, -1, 1.5, 2.5, -2.5, 10, 1e-300, 4.9e-324, 2.2250738585072014E-308, 1e308, double.MaxValue, double.MinValue, double.PositiveInfinity, double.NegativeInfinity, double.NaN };

foreach (var x in values)
{
    Console.WriteLine($"{F(x),-24} next={F(Math.BitIncrement(x))} prev={F(Math.BitDecrement(x))} ilogb={Math.ILogB(x)} " +
        $"acosh={Math.Acosh(x):G6} asinh={Math.Asinh(x):G6} atanh={Math.Atanh(x):G6}");
    Console.WriteLine($"{"",-24} fma={F(Math.FusedMultiplyAdd(x, 3, 0.5))} fma2={F(Math.FusedMultiplyAdd(x, x, -x * x))} sincos={Math.SinCos(x).Sin:G6},{Math.SinCos(x).Cos:G6}");
    foreach (var y in new[] { 0, -2.5, 2.5, 1, double.NaN })
        Console.WriteLine($"{"",-24} y={F(y),-5} maxmag={F(Math.MaxMagnitude(x, y))} minmag={F(Math.MinMagnitude(x, y))}");
    try { Console.WriteLine($"{"",-24} sign={Math.Sign(x)}"); }
    catch (Exception e) { Console.WriteLine($"{"",-24} {e.GetType().Name}: {e.Message}"); }
}

// FusedMultiplyAdd rounds once: the product's rounding error survives into the sum
double a = 1 + Math.Pow(2, -30), b = 1 - Math.Pow(2, -30);
Console.WriteLine(F(Math.FusedMultiplyAdd(a, b, -1)) + " " + F(a * b - 1));

// Log and Log2 of a negative number: .NET answers the positive quiet NaN, whatever the C library does
double negative = -1;
Console.WriteLine(Bits(Math.Log(negative)) + " " + Bits(Math.Log2(negative)));
Console.WriteLine(Bits(Math.Log(-0.0)) + " " + Bits(Math.Log(0.0)) + " " + Bits(Math.Log(double.NegativeInfinity)) + " " + Bits(Math.Log2(double.NegativeInfinity)));
double negativeNaN = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000000UL)), positiveNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000000L);
Console.WriteLine(double.IsNegative(negativeNaN) + " " + double.IsNegative(positiveNaN) + " " + double.IsNegative(-0.0) + " " + double.IsNegative(0.0));
Console.WriteLine(Bits(Math.Log(negativeNaN)) + " " + Bits(Math.Log(positiveNaN)) + " " + Bits(Math.Log2(negativeNaN)));
