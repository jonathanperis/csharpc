// The generic-math statics of float and double (IFloatingPointIeee754<T> & co.): float.Hypot, double.Lerp, T.SinPi ...
// Called directly and through a generic method. Algorithmic results (no libm) print with the round-trip format;
// the ones that call the C library print 5 significant digits, as 1227_mathf_libm does.
using System;
using System.Globalization;
using System.Numerics;

Console.WriteLine("== float, directly");
Direct();
Console.WriteLine("== double, directly");
DirectDouble();
Console.WriteLine("== float, generic");
Generic<float>(new[] { 0f, -0f, 0.5f, 1f, -1f, 1.5f, 2.5f, -3f, 0.3f, 100.25f, 1e-40f, float.MaxValue, float.PositiveInfinity, float.NaN });
Console.WriteLine("== double, generic");
Generic<double>(new[] { 0d, -0d, 0.5, 1, -1, 1.5, 2.5, -3, 0.3, 100.25, 1e-310, double.MaxValue, double.PositiveInfinity, double.NaN });


static void Direct()
{
    Console.WriteLine($"{float.Hypot(3f, 4f)} {float.Hypot(1e30f, 1e30f)} {float.Hypot(float.NaN, float.PositiveInfinity)} {float.Hypot(0f, -2f)}");
    Console.WriteLine($"{float.Lerp(1f, 3f, 0.25f)} {float.Lerp(10f, -10f, 0.5f)} {float.Clamp(5f, 1f, 3f)} {float.Clamp(float.NaN, 1f, 3f)}");
    Console.WriteLine($"{Fmt.G(float.RootN(27f, 3))} {Fmt.G(float.RootN(16f, 2))} {Fmt.G(float.RootN(-8f, 3))} {Fmt.G(float.RootN(-4f, 2))} {Fmt.G(float.RootN(0f, -1))} {Fmt.G(float.RootN(81f, 4))}");
    Console.WriteLine($"{float.ILogB(10f)} {float.ILogB(0f)} {float.ScaleB(1f, 10)} {float.Ieee754Remainder(7f, 4f)} {float.BitIncrement(1f)} {float.BitDecrement(1f)}");
    Console.WriteLine($"{float.MaxNumber(float.NaN, 2f)} {float.MinNumber(1f, float.NaN)} {float.MaxMagnitudeNumber(-4f, 3f)} {float.MinMagnitudeNumber(-4f, 3f)} {float.MaxNative(1f, 2f)} {float.MinNative(1f, 2f)}");
    Console.WriteLine($"{float.DegreesToRadians(180f)} {float.RadiansToDegrees(float.Pi)} {float.SinPi(0.5f)} {float.CosPi(1f)} {float.TanPi(0.25f)} {float.SinCosPi(0.75f)}");
    Console.WriteLine($"{Fmt.G(float.Exp2(3.5f))} {Fmt.G(float.Exp10(2.5f))} {Fmt.G(float.ExpM1(0.5f))} {Fmt.G(float.LogP1(0.5f))} {Fmt.G(float.Log2P1(7f))} {Fmt.G(float.Log10P1(99f))} {Fmt.G(float.Atan2Pi(1f, 1f))}");
    Console.WriteLine($"{float.IsNormal(1f)} {float.IsSubnormal(1e-40f)} {float.IsNegative(-0f)} {float.IsPositive(-0f)} {float.IsInteger(2f)} {float.IsEvenInteger(2f)} {float.IsOddInteger(3f)} {float.IsRealNumber(float.NaN)}");
    Console.WriteLine($"{float.Round(2.5f)} {float.Round(2.675f, 2)} {float.Round(2.5f, MidpointRounding.AwayFromZero)} {float.Round(-1.234f, 1, MidpointRounding.ToNegativeInfinity)} {float.Sign(-2f)} {float.Sin(0f)} {float.Pow(2f, 8f)}");
    try { float.Clamp(0f, 2f, 1f); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    try { float.ClampNative(0f, 2f, 1f); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}

static void DirectDouble()
{
    Console.WriteLine($"{double.Hypot(3, 4)} {double.Hypot(1e300, 1e300)} {double.Hypot(double.NaN, double.PositiveInfinity)} {double.Hypot(0, -2)} {double.Hypot(1e-310, 1e-310)}");
    Console.WriteLine($"{double.Lerp(1, 3, 0.25)} {double.Lerp(10, -10, 0.5)} {double.Clamp(5, 1, 3)} {double.Clamp(double.NaN, 1, 3)}");
    Console.WriteLine($"{Fmt.G(double.RootN(27, 3))} {Fmt.G(double.RootN(16, 2))} {Fmt.G(double.RootN(-8, 3))} {Fmt.G(double.RootN(-4, 2))} {Fmt.G(double.RootN(0, -1))} {Fmt.G(double.RootN(81, 4))}");
    Console.WriteLine($"{double.ILogB(10)} {double.ILogB(0)} {double.ScaleB(1, 10)} {double.Ieee754Remainder(7, 4)} {double.BitIncrement(1)} {double.BitDecrement(1)}");
    Console.WriteLine($"{double.MaxNumber(double.NaN, 2)} {double.MinNumber(1, double.NaN)} {double.MaxMagnitudeNumber(-4, 3)} {double.MinMagnitudeNumber(-4, 3)} {double.MaxNative(1, 2)} {double.MinNative(1, 2)}");
    Console.WriteLine($"{double.DegreesToRadians(180)} {double.RadiansToDegrees(double.Pi)} {double.SinPi(0.5)} {double.CosPi(1)} {double.TanPi(0.25)} {double.SinCosPi(0.75)}");
    Console.WriteLine($"{Fmt.G(double.Exp2(3.5))} {Fmt.G(double.Exp10(2.5))} {Fmt.G(double.ExpM1(0.5))} {Fmt.G(double.LogP1(0.5))} {Fmt.G(double.Log2P1(7))} {Fmt.G(double.Log10P1(99))} {Fmt.G(double.Atan2Pi(1, 1))}");
    Console.WriteLine($"{double.IsNormal(1)} {double.IsSubnormal(1e-310)} {double.IsNegative(-0d)} {double.IsPositive(-0d)} {double.IsInteger(2)} {double.IsEvenInteger(2)} {double.IsOddInteger(3)} {double.IsRealNumber(double.NaN)}");
    Console.WriteLine($"{double.Round(2.5)} {double.Round(2.675, 2)} {double.Round(2.5, MidpointRounding.AwayFromZero)} {double.Sign(-2d)} {double.Sin(0)} {double.Pow(2, 8)} {double.Log(8, 2)}");
    try { double.Clamp(0, 2, 1); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    try { double.Sign(double.NaN); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}

static void Generic<T>(T[] values) where T : IFloatingPointIeee754<T>
{
    T half = T.CreateTruncating(0.5);
    T seven = T.CreateTruncating(7);
    foreach (var x in values)
    {
        Console.WriteLine($"{Fmt.R(x),-24} even={T.IsEvenInteger(x)} odd={T.IsOddInteger(x)} int={T.IsInteger(x)} normal={T.IsNormal(x)} sub={T.IsSubnormal(x)} neg={T.IsNegative(x)} pos={T.IsPositive(x)} real={T.IsRealNumber(x)}");
        Console.WriteLine($"{"",-24} hypot={Fmt.R(T.Hypot(x, seven))} lerp={Fmt.R(T.Lerp(x, seven, half))} next={Fmt.R(T.BitIncrement(x))} prev={Fmt.R(T.BitDecrement(x))} scale={Fmt.R(T.ScaleB(x, 3))} ilogb={T.ILogB(x)} rem={Fmt.R(T.Ieee754Remainder(x, seven))}");
        Console.WriteLine($"{"",-24} maxn={Fmt.R(T.MaxNumber(x, half))} minn={Fmt.R(T.MinNumber(x, half))} maxmn={Fmt.R(T.MaxMagnitudeNumber(x, seven))} minmn={Fmt.R(T.MinMagnitudeNumber(x, seven))} copy={Fmt.R(T.CopySign(half, x))} fma={Fmt.R(T.FusedMultiplyAdd(x, seven, half))}");
        Console.WriteLine($"{"",-24} sinpi={Fmt.R(T.SinPi(x))} cospi={Fmt.R(T.CosPi(x))} tanpi={Fmt.R(T.TanPi(x))} deg={Fmt.R(T.DegreesToRadians(x))} rad={Fmt.R(T.RadiansToDegrees(x))}");
        Console.WriteLine($"{"",-24} round={Fmt.R(T.Round(x))} r1={Fmt.R(T.Round(x, 1))} away={Fmt.R(T.Round(x, MidpointRounding.AwayFromZero))} r2z={Fmt.R(T.Round(x, 2, MidpointRounding.ToZero))}");
        Console.WriteLine($"{"",-24} sin={Fmt.G(T.Sin(x))} atan={Fmt.G(T.Atan(x))} exp2={Fmt.G(T.Exp2(x))} exp10={Fmt.G(T.Exp10(x))} expm1={Fmt.G(T.ExpM1(x))} logp1={Fmt.G(T.LogP1(x))} log2p1={Fmt.G(T.Log2P1(x))} cbrt={Fmt.G(T.Cbrt(x))} root5={Fmt.G(T.RootN(x, 5))} rootm2={Fmt.G(T.RootN(x, -2))}");
        try { Console.WriteLine($"{"",-24} sign={T.Sign(x)}"); } catch (Exception e) { Console.WriteLine($"{"",-24} {e.GetType().Name}: {e.Message}"); }
        var (s, c) = T.SinCosPi(x);
        Console.WriteLine($"{"",-24} sincospi={Fmt.R(s)},{Fmt.R(c)} atan2pi={Fmt.G(T.Atan2Pi(x, seven))} asinpi={Fmt.G(T.AsinPi(half))} lg={Fmt.G(T.Log(x, seven))}");
    }
}

static class Fmt
{
    public static string R(IFormattable v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static string G(IFormattable v) => v.ToString("G5", CultureInfo.InvariantCulture);
}
