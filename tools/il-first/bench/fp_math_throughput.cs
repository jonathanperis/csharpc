// Floating-point math throughput, the program behind the NativeAOT column of docs/il-first/System.Math.md (tools/il-first/measure-aot.sh):
// float/double generic-math statics, Math.Round modes and clamping. Checksums only.
using System;

double acc = 0;
float facc = 0;
for (int i = 0; i < 2_000_000; i++)
{
    double x = (i % 1000) * 0.0137 - 6.0;
    acc += double.SinPi(x) + double.Hypot(x, 1.5) + double.RootN(Math.Abs(x) + 1, 3) + Math.Round(x, 2, MidpointRounding.AwayFromZero);
    acc += double.Lerp(x, 1, 0.25) + double.ScaleB(x, 2) + double.MaxMagnitude(x, -2) + double.ILogB(x + 7);
    float f = (float)x;
    facc += float.CosPi(f) + float.Exp2M1(f * 0.01f) + MathF.Round(f, 1) + float.Clamp(f, -1, 1);
}
Console.WriteLine(acc.ToString("R") + " " + facc.ToString("R"));
