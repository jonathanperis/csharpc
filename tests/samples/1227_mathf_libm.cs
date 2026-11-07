// System.MathF's libm functions. The C library's float functions can differ in the last bit between platforms, so the
// results are printed with 5 significant digits (what the test pins down is the function, the special values and the
// domain errors; csharpc and .NET call the same libm on one machine, which tests/ also compares bit for bit by hand).
using System;

static string F(float v) => v.ToString("G5");

float[] values = { 0f, -0f, 0.25f, 0.5f, 0.75f, 1f, -1f, 1.5f, 2f, -2f, 3f, 10f, 100f, -0.3f, 1e-5f, 1e5f, float.PositiveInfinity, float.NegativeInfinity, float.NaN };

foreach (var x in values)
{
    Console.WriteLine($"{F(x),-9} sin={F(MathF.Sin(x))} cos={F(MathF.Cos(x))} tan={F(MathF.Tan(x))} asin={F(MathF.Asin(x))} acos={F(MathF.Acos(x))} atan={F(MathF.Atan(x))}");
    Console.WriteLine($"{"",-9} sinh={F(MathF.Sinh(x))} cosh={F(MathF.Cosh(x))} tanh={F(MathF.Tanh(x))} asinh={F(MathF.Asinh(x))} acosh={F(MathF.Acosh(x))} atanh={F(MathF.Atanh(x))}");
    Console.WriteLine($"{"",-9} exp={F(MathF.Exp(x))} log={F(MathF.Log(x))} log2={F(MathF.Log2(x))} log10={F(MathF.Log10(x))} cbrt={F(MathF.Cbrt(x))} sqrt={F(MathF.Sqrt(x))}");
    var (s, c) = MathF.SinCos(x);
    Console.WriteLine($"{"",-9} sincos={F(s)},{F(c)}");
}

float[] bases = { 0f, 1f, 2f, 10f, 0.5f, -2f, float.PositiveInfinity, float.NaN };
foreach (var x in new[] { 0f, 1f, 2f, 8f, 0.25f, -8f, 100f, float.PositiveInfinity, float.NaN })
    foreach (var y in bases)
        Console.WriteLine($"{F(x),-9},{F(y),-9} pow={F(MathF.Pow(x, y))} atan2={F(MathF.Atan2(x, y))} logb={F(MathF.Log(x, y))}");

// results that are exact in every libm
Console.WriteLine(MathF.Pow(2f, 10f) + " " + MathF.Pow(10f, 3f) + " " + MathF.Pow(-8f, 1f / 3f) + " " + MathF.Pow(4f, 0.5f) + " " + MathF.Pow(7f, 0f));
Console.WriteLine(MathF.Exp(0f) + " " + MathF.Log(1f) + " " + MathF.Log2(8f) + " " + MathF.Log10(1000f) + " " + MathF.Cbrt(27f) + " " + MathF.Cbrt(-8f));
Console.WriteLine(MathF.Sin(0f) + " " + MathF.Cos(0f) + " " + MathF.Atan2(0f, -1f) + " " + MathF.Atan2(1f, 0f) + " " + MathF.Acos(1f));

// the generic-math statics reach the same functions
Console.WriteLine(F(float.Sin(1f)) + " " + F(float.Cos(1f)) + " " + F(float.Pow(2f, 0.5f)) + " " + F(float.Log(10f, 10f)) + " " + F(float.Exp2(3.5f)) + " " + F(float.Exp10(2.5f)));
Console.WriteLine(F(Apply(0.75f)) + " " + F(Apply(2f)));

static T Apply<T>(T x) where T : System.Numerics.IFloatingPointIeee754<T> => T.Sin(x) + T.Cos(x) + T.Atan2(x, T.One) + T.Hypot(x, x) + T.Cbrt(x);
