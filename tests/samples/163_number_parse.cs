// Number parsing (correct rounding, styles, errors) and Math.Round on exact decimal values.
using System;
using System.Globalization;

string[] doubles = { "0", "-0", "1.5", "  3.25  ", "1e308", "1.7976931348623157e308", "1.7976931348623159e308", "2e308", "4.9e-324", "2.4703282292062327e-324",
    "2.4703282292062328e-324", "1e-400", "0.1", "0.30000000000000004", "123456789012345678901234567890", "9007199254740993", "9007199254740995",
    "2.2250738585072011e-308", "2.2250738585072012e-308", ".5", "5.", "+7", "1,234.5", "1E+3", "NaN", "-Infinity", "infinity", "∞", "-nan",
    "0.000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000049406564584124654",
    "3.141592653589793238462643383279502884197169399375105820974944592307816406286", "abc", "", "1e", "1.2.3", "--1", "0x10" };
foreach (var s in doubles)
    Console.WriteLine($"[{s}] -> {(double.TryParse(s, out var d) ? d.ToString("R") : "fail")}");
string[] ints = { "0", "-2147483648", "2147483647", "2147483648", " 42 ", "+5", "-0", "12a", "", " ", "99999999999999999999", "99999999999999999999x", "1,000", "\t7\n" };
foreach (var s in ints)
{
    Console.Write($"[{s}] {(int.TryParse(s, out var i) ? i.ToString() : "fail")} {(long.TryParse(s, out var l) ? l.ToString() : "fail")} ");
    try { Console.WriteLine(int.Parse(s)); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}
Console.WriteLine(int.Parse("FF", NumberStyles.HexNumber) + " " + int.Parse("FFFFFFFF", NumberStyles.HexNumber) + " " + long.Parse("1,234,567", NumberStyles.AllowThousands));
Console.WriteLine(byte.Parse("255", NumberStyles.Integer) + " " + int.Parse("12.000", NumberStyles.Number) + " " + double.Parse("1,234.5e2", NumberStyles.Float | NumberStyles.AllowThousands));
try { byte.Parse("256", NumberStyles.Integer); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { sbyte.Parse("-129", NumberStyles.Integer, CultureInfo.InvariantCulture); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { double.Parse("x1"); } catch (FormatException e) { Console.WriteLine(e.Message); }
try { double.Parse(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
foreach (var x in new[] { 1.115, 2.675, 1.005, 0.285, 2.355, 0.125, 0.135, 4.35, 10.555, 1.4999999999999998, -0.004, 123.456, 0.0004, 1e15 + 0.5, -2.5 })
    Console.WriteLine($"{x:R} {Math.Round(x, 2):R} {Math.Round(x, 2, MidpointRounding.AwayFromZero):R} {Math.Round(x, 1, MidpointRounding.ToZero):R} {Math.Round(x, 3, MidpointRounding.ToPositiveInfinity):R} {Math.Round(x, 3, MidpointRounding.ToNegativeInfinity):R} {Math.Round(x, MidpointRounding.AwayFromZero):R}");
Console.WriteLine(Math.Round(1.23456789012345678, 16) + " " + Math.Round(0.1, 300));
try { Math.Round(1.0, -1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { Math.Round(1.0, 1, (MidpointRounding)9); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine($"{Int128.MaxValue} {Int128.MinValue} {UInt128.MaxValue} {(Int128)(-5)} {(Int128)long.MaxValue * 1000}");
ulong state = 88172645463325252UL;
int bad = 0;
for (int k = 0; k < 20000; k++)
{
    state ^= state << 13; state ^= state >> 7; state ^= state << 17;
    double v = BitConverter.Int64BitsToDouble((long)state);
    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
    if (double.Parse(v.ToString("R")) != v || double.Parse(v.ToString("E16")) != v) bad++;
}
Console.WriteLine("roundtrip failures: " + bad);
