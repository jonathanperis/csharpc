// System.MathF, the members whose results are exact (no libm): rounding, magnitudes, bit steps, scaling, remainders.
// Every result is printed with the round-trip format, so a wrong last bit shows. Each row is one input pair.
using System;

float[] values =
{
    0f, -0f, 1f, -1f, 0.5f, -0.5f, 1.5f, 2.5f, -2.5f, 3.5f, 0.1f, 0.3f, -0.7f, 1.005f, 2.675f, 123456.789f,
    8388607.5f, 8388608f, 16777216f, 1e10f, float.MaxValue, float.MinValue, float.Epsilon, -float.Epsilon, 1.17549435E-38f, 1e-40f,
    float.PositiveInfinity, float.NegativeInfinity, float.NaN,
};

static string F(float v) => v.ToString("R");

Console.WriteLine("E=" + F(MathF.E) + " PI=" + F(MathF.PI) + " Tau=" + F(MathF.Tau));

Console.WriteLine("-- unary");
foreach (var x in values)
{
    Console.WriteLine($"{F(x),-12} abs={F(MathF.Abs(x))} ceil={F(MathF.Ceiling(x))} floor={F(MathF.Floor(x))} trunc={F(MathF.Truncate(x))} " +
        $"round={F(MathF.Round(x))} sqrt={F(MathF.Sqrt(x))} next={F(MathF.BitIncrement(x))} prev={F(MathF.BitDecrement(x))} ilogb={MathF.ILogB(x)}");
}

Console.WriteLine("-- rounding modes");
foreach (var x in values)
{
    Console.WriteLine($"{F(x),-12} even={F(MathF.Round(x, MidpointRounding.ToEven))} away={F(MathF.Round(x, MidpointRounding.AwayFromZero))} " +
        $"zero={F(MathF.Round(x, MidpointRounding.ToZero))} down={F(MathF.Round(x, MidpointRounding.ToNegativeInfinity))} up={F(MathF.Round(x, MidpointRounding.ToPositiveInfinity))}");
}

Console.WriteLine("-- rounding digits");
foreach (var x in values)
{
    Console.WriteLine($"{F(x),-12} d1={F(MathF.Round(x, 1))} d2={F(MathF.Round(x, 2))} d3={F(MathF.Round(x, 3))} d7={F(MathF.Round(x, 7))} d12={F(MathF.Round(x, 12))} " +
        $"d2away={F(MathF.Round(x, 2, MidpointRounding.AwayFromZero))} d2zero={F(MathF.Round(x, 2, MidpointRounding.ToZero))} " +
        $"d2down={F(MathF.Round(x, 2, MidpointRounding.ToNegativeInfinity))} d2up={F(MathF.Round(x, 2, MidpointRounding.ToPositiveInfinity))} d0={F(MathF.Round(x, 0))}");
}

Console.WriteLine("-- binary");
float[] second = { 0f, -0f, 1f, -1.5f, 2.5f, -2.5f, 0.3f, 7f, float.PositiveInfinity, float.NaN };
foreach (var x in values)
{
    foreach (var y in second)
    {
        Console.WriteLine($"{F(x)},{F(y)} max={F(MathF.Max(x, y))} min={F(MathF.Min(x, y))} maxmag={F(MathF.MaxMagnitude(x, y))} minmag={F(MathF.MinMagnitude(x, y))} " +
            $"copysign={F(MathF.CopySign(x, y))} rem={F(MathF.IEEERemainder(x, y))} fma={F(MathF.FusedMultiplyAdd(x, y, 1.5f))}");
    }
}

Console.WriteLine("-- scale");
foreach (var n in new[] { -2000, -300, -150, -149, -127, -126, -1, 0, 1, 127, 128, 254, 300, 2000 })
    Console.WriteLine($"{n,5} {F(MathF.ScaleB(1.5f, n))} {F(MathF.ScaleB(-0.75f, n))} {F(MathF.ScaleB(float.Epsilon, n))} {F(MathF.ScaleB(float.MaxValue, n))}");

Console.WriteLine("-- sign");
foreach (var x in values)
{
    try { Console.WriteLine($"{F(x),-12} {MathF.Sign(x)}"); }
    catch (Exception e) { Console.WriteLine($"{F(x),-12} {e.GetType().Name}: {e.Message}"); }
}

Console.WriteLine("-- errors");
foreach (var action in new Action[]
{
    () => MathF.Round(1.5f, -1),
    () => MathF.Round(1.5f, 2, (MidpointRounding)9),
    () => MathF.Round(1.5f, (MidpointRounding)9),
    () => MathF.Round(float.NaN, -3),
})
{
    try { action(); Console.WriteLine("no exception"); }
    catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
}

Console.WriteLine("-- estimates stay close");
foreach (var x in new[] { 0.5f, 3f, 10f, 12345.5f })
{
    Console.WriteLine($"{F(x)} recip {Math.Abs(MathF.ReciprocalEstimate(x) * x - 1) < 0.001f} rsqrt {Math.Abs(MathF.ReciprocalSqrtEstimate(x) * MathF.Sqrt(x) - 1) < 0.001f}");
}
