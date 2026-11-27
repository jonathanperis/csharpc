// The floating-point members of float, double, Math and MathF served from CoreLib's IL
// (#423): generic-math statics, rounding modes and saturating conversions.
using System;
using System.Globalization;
using System.Numerics;

static class Program
{
    static T Hyp<T>(T x, T y) where T : IRootFunctions<T> => T.Hypot(x, y);
    static T Mid<T>(T a, T b) where T : IFloatingPointIeee754<T> => T.Lerp(a, b, T.CreateChecked(0.5));

    // ConvertToIntegerNative of NaN or of a value out of range is the platform instruction's result (x64, Arm64 and wasm32 differ): in range only.
    // The transcendental members end in the C library's libm (glibc, Apple's, the UCRT, wasi-libc), whose last bit differs between platforms
    // for .NET too: printed to 13 and 6 significant digits.
    static string L(double v) => v.ToString("G13");
    static string L(float v) => v.ToString("G6");

    static double InRange(double x) => double.IsFinite(x) && Math.Abs(x) < 1e18 ? x : 0;

    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        double[] xs = { 0.0, -0.0, 0.5, -2.5, 1e-310, 3.75, double.NaN, double.PositiveInfinity, 1e300 };
        foreach (double x in xs)
            Console.WriteLine($"{x}: {L(double.SinPi(x))} {L(double.CosPi(x))} {L(double.RootN(x, 3))} {double.ILogB(x)} {double.BitIncrement(x)} {double.ScaleB(x, -3)} {double.ConvertToInteger<int>(x)} {double.ConvertToIntegerNative<long>(InRange(x))}");
        foreach (float x in new[] { 0.1f, -7.25f, float.Epsilon, float.MaxValue, float.NegativeInfinity })
            Console.WriteLine($"{x}: {L(float.Exp10M1(x))} {L(float.Log2P1(x))} {L(float.AtanPi(x))} {float.MaxMagnitudeNumber(x, -1f)} {float.ReciprocalSqrtEstimate(x) > 0} {MathF.BitDecrement(x)} {float.ConvertToInteger<byte>(x)}");
        var (sinPi, cosPi) = double.SinCosPi(0.25);
        Console.WriteLine(L(sinPi) + " " + L(cosPi) + " " + L(double.Atan2Pi(1, -1)) + " " + double.Ieee754Remainder(10, 3) + " " + Math.IEEERemainder(-7.5, 2));
        foreach (var mode in new[] { MidpointRounding.ToEven, MidpointRounding.AwayFromZero, MidpointRounding.ToZero, MidpointRounding.ToNegativeInfinity, MidpointRounding.ToPositiveInfinity })
            Console.WriteLine($"{mode}: {Math.Round(2.345, 2, mode)} {Math.Round(-2.5, mode)} {MathF.Round(1.15f, 1, mode)} {double.Round(-0.125, 2, mode)}");
        Console.WriteLine(Hyp(3.0, 4.0) + " " + Hyp(5f, 12f) + " " + Mid(1.0, 4.0) + " " + Mid(-1f, 2f));
        Console.WriteLine(L(Math.Log(1024, 2)) + " " + L(MathF.Log(81f, 3f)) + " " + Math.Sign(-0.0) + " " + MathF.ILogB(0.0f) + " " + double.MinNative(double.NaN, 1) + " " + double.MaxNumber(double.NaN, 1));
        try { Math.Sign(double.NaN); } catch (ArithmeticException e) { Console.WriteLine(e.Message); }
        try { Math.Round(1.5, 16); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
        try { Math.Round(1.5, (MidpointRounding)9); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
    }
}
