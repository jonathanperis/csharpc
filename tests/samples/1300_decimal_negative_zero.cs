// decimal has negative zeros: -z, decimal.Negate, 0.00m * -1m, Round/Truncate of a small negative value and an exactly cancelling sum or
// difference keep (or take) the sign bit exactly as DecCalc does, visible through GetBits. The compiler itself depends on it:
// Roslyn folds -0.00m with the System.Decimal it runs on, so a csharpc built by csharpc lost the sign of such a constant.
using System;
using System.Globalization;

var ci = CultureInfo.InvariantCulture;
decimal P(string s) => decimal.Parse(s, NumberStyles.Float, ci);
string B(decimal d) => d + "[" + string.Join(",", decimal.GetBits(d)) + "]";
var zs = new[] { "0", "0.00", "-0", "-0.00", "-0.000" };
var os = new[] { "1", "-1", "0.5", "-2.50" };
foreach (var a in zs)
{
    Console.WriteLine($"{a}: neg={B(-P(a))} negate={B(decimal.Negate(P(a)))} abs={B(decimal.Abs(P(a)))} plus={B(+P(a))}");
    Console.WriteLine($"  round={B(decimal.Round(P(a), 1))} trunc={B(decimal.Truncate(P(a)))} floor={B(decimal.Floor(P(a)))} ceil={B(decimal.Ceiling(P(a)))} mround={B(Math.Round(P(a)))}");
    Console.WriteLine($"  rem={B(P(a) % P("3"))} remneg={B(P(a) % P("-3"))} div={B(P(a) / P("3"))} divneg={B(P(a) / P("-3"))}");
    foreach (var b in zs)
        Console.WriteLine($"  {b}: add={B(P(a) + P(b))} sub={B(P(a) - P(b))} mul={B(P(a) * P(b))}");
    foreach (var b in os)
        Console.WriteLine($"  {b}: add={B(P(a) + P(b))} sub={B(P(a) - P(b))} mul={B(P(a) * P(b))} div={B(P(a) / P(b))} rem={B(P(a) % P(b))}");
}
foreach (var s in new[] { "-0.001", "-0.4", "-0.5", "-0.0004", "-0.5000", "-1E-28", "-0.00000000000000000000000000001" })
{
    var d = P(s);
    Console.WriteLine($"{s}: r0={B(decimal.Round(d))} r2={B(decimal.Round(d, 2))} away={B(decimal.Round(d, MidpointRounding.AwayFromZero))} ceil={B(decimal.Ceiling(d))} trunc={B(decimal.Truncate(d))} int={B(d * 0)} mulsmall={B(d * P("0.0001"))} add={B(d + P("0.001"))} sub={B(d - d)}");
}
Console.WriteLine(B((decimal)(-0.0)) + " " + B((decimal)(-0.0f)) + " " + B(Convert.ToDecimal(-0.0)) + " " + B((decimal)(-1e-30)) + " " + B(decimal.Parse("-0E-10", NumberStyles.Float, ci)) + " " + B(decimal.Parse("-0.0", ci)));
Console.WriteLine(B(new decimal(0, 0, 0, true, 3)) + " " + B(new decimal(new[] { 0, 0, 0, int.MinValue })) + " " + B(decimal.Divide(P("-0.00"), 7)) + " " + B(decimal.Multiply(P("-0.0"), 3)));
foreach (var (x, y) in new[] { ("1.0", "1.00"), ("1.00", "1.0"), ("-1.0", "1.00"), ("1.0", "-1.00"), ("-1.00", "1.0"), ("1", "1"), ("-1", "-1"), ("-1", "1"), ("-2.50", "-2.5") })
    Console.WriteLine($"{x} {y}: add={B(P(x) + P(y))} sub={B(P(x) - P(y))} rem={B(P(x) % P(y))} div={B(P(x) / P(y))}");
Console.WriteLine(B(decimal.Truncate(P("-0.5"))) + " " + B(decimal.Floor(P("0.5"))) + " " + B(Math.Abs(P("-0.00"))) + " " + Math.Sign(P("-0.00")) + " " + ((double)P("-0.00")) + " " + ((int)P("-0.00")) + " " + (P("-0.00") == P("0")) + " " + P("-0.00").GetHashCode() + " " + P("0").GetHashCode() + " " + P("-0.00").CompareTo(P("0")) + " " + P("-0.00").ToString("F1", ci) + " " + P("-0.00").ToString("N0", ci) + " " + P("-0.00").ToString("0.00;(0.00)", ci));
