// Math.Round / MathF.Round / double.Round with digits and MidpointRounding come from CoreLib's IL (#453): the results, including
// the cases where the decimal midpoint is not exact in binary, every mode, large and tiny values, and the argument checks.
using System;
using System.Globalization;

static class Program
{
    static string S(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    static string S(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    static void Main()
    {
        double[] values = { 2.675, 2.665, 1.005, 0.125, 0.375, -2.5, 2.5, 3.5, -0.5, 0.5, 1.4999999999999998, 123456.785,
                            9.995, -9.995, 1e15 + 0.5, 1e16, 4503599627370495.5, 1e-10, 5e-324, double.MaxValue, -0.0,
                            double.NaN, double.PositiveInfinity, double.NegativeInfinity, Math.PI, Math.E, 0.1 + 0.2 };
        var modes = (MidpointRounding[])Enum.GetValues(typeof(MidpointRounding));
        foreach (var v in values)
        {
            var line = S(v) + ":";
            foreach (var digits in new[] { 0, 1, 2, 3, 6, 15 })
            {
                line += " " + digits + "=" + S(Math.Round(v, digits));
                foreach (var m in modes) line += "," + S(Math.Round(v, digits, m));
            }
            line += " | " + S(Math.Round(v, MidpointRounding.AwayFromZero)) + " " + S(double.Round(v, 2)) + " " + S(double.Round(v, MidpointRounding.ToZero))
                  + " " + S(double.Round(v, 1, MidpointRounding.ToPositiveInfinity));
            Console.WriteLine(line);
        }
        float[] floats = { 2.675f, 1.005f, -2.5f, 0.125f, 123.4565f, 1e7f + 0.5f, float.NaN, -0.0f, float.MaxValue };
        foreach (var f in floats)
        {
            var line = S(f) + ":";
            foreach (var digits in new[] { 0, 1, 2, 4, 6 })
            {
                line += " " + digits + "=" + S(MathF.Round(f, digits));
                foreach (var m in modes) line += "," + S(MathF.Round(f, digits, m));
            }
            Console.WriteLine(line + " | " + S(float.Round(f, 2)) + " " + S(float.Round(f, 1, MidpointRounding.ToNegativeInfinity)));
        }
        foreach (var (digits, mode) in new[] { (-1, MidpointRounding.ToEven), (16, MidpointRounding.ToEven), (2, (MidpointRounding)7) })
        {
            try { Console.WriteLine(S(Math.Round(1.25, digits, mode))); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
            try { Console.WriteLine(S(MathF.Round(1.25f, digits, mode))); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        }
        double acc = 0;
        for (int i = 0; i < 1000; i++) acc += Math.Round(i * 0.0137 - 6.0, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine(S(acc));
    }
}
