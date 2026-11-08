// System.Numerics.Decimal32, Decimal64 and Decimal128 (IEEE 754 decimal floating point, BID encoding): constants, the
// cohort a parse keeps ("1.50" prints "1.50"), rounding of long digit strings (half to even, subnormals, overflow to
// infinity), the standard and custom format strings, numeric comparison across cohorts, truncation and rounding to a power
// of ten, and the conversions. The JSON serializer's converter table names the three types.
using System;
using System.Globalization;
using System.Numerics;

var ci = CultureInfo.InvariantCulture;

Console.WriteLine("constants");
Console.WriteLine(Decimal32.MaxValue.ToString(ci) + " " + Decimal32.MinValue.ToString(ci) + " " + Decimal32.Epsilon.ToString(ci));
Console.WriteLine(Decimal64.MaxValue.ToString(ci) + " " + Decimal64.Epsilon.ToString(ci));
Console.WriteLine(Decimal128.MaxValue.ToString(ci) + " " + Decimal128.Epsilon.ToString(ci));
Console.WriteLine(Decimal32.E.ToString(ci) + " " + Decimal32.Pi + " " + Decimal32.Tau + " " + Decimal64.Pi + " " + Decimal128.Pi + " " + Decimal128.E + " " + Decimal128.Tau);
Console.WriteLine(Decimal32.One + " " + Decimal32.Zero + " " + Decimal32.NegativeOne + " " + Decimal32.NegativeZero + " " + Decimal32.NaN + " " + Decimal32.PositiveInfinity + " " + Decimal32.NegativeInfinity);
foreach (var n in new[] { Decimal32.Zero, Decimal32.One, Decimal32.NegativeOne, Decimal32.NegativeZero, Decimal32.NaN, Decimal32.PositiveInfinity, Decimal32.NegativeInfinity, Decimal32.MaxValue, Decimal32.MinValue, Decimal32.Epsilon })
    Console.WriteLine($"{Decimal32.EncodeBinary(n):X8}");
Console.WriteLine($"{Decimal64.EncodeBinary(Decimal64.One):X16} {Decimal64.EncodeBinary(Decimal64.MaxValue):X16} {Decimal64.EncodeBinary(Decimal64.Epsilon):X16} {Decimal64.EncodeBinary(Decimal64.NaN):X16} {Decimal64.EncodeBinary(Decimal64.PositiveInfinity):X16}");
static string Hex128(UInt128 v) => ((ulong)(v >> 64)).ToString("X16") + ((ulong)v).ToString("X16");
Console.WriteLine(Hex128(Decimal128.EncodeBinary(Decimal128.One)) + " " + Hex128(Decimal128.EncodeBinary(Decimal128.MaxValue)) + " " + Hex128(Decimal128.EncodeBinary(Decimal128.Epsilon)) + " " + Hex128(Decimal128.EncodeBinary(Decimal128.NaN)));
Console.WriteLine(Decimal32.EncodeBinary(Decimal32.DecodeBinary(0x32800001)).ToString("X8") + " " + Decimal32.DecodeBinary(0x22400015).ToString(ci) + " " + Decimal64.DecodeBinary(0x31C0000000000001UL));

Console.WriteLine("Decimal32 parse");
string[] inputs = { "0", "-0", "1", "1.5", "1.50", "100", "1E3", "1e-3", "123456789", "1234567", "12345678", "0.0000001", "9999999E90", "9999999E91", "1E-101", "1E-102", "1E-110", "NaN", "Infinity", "-Infinity", "  7  ", "1,000", "0.1234567890123", "1.00000000000000000000000000000000000001", "5E-101", "0.5E-100", "0.000", "0E3", "-0.00" };
foreach (var s in inputs)
{
    bool ok = Decimal32.TryParse(s, ci, out var d);
    Console.WriteLine($"{s,-45} ok={ok} str='{(ok ? d.ToString(ci) : "")}' bin={(ok ? Decimal32.EncodeBinary(d).ToString("X8") : "")}");
}
Console.WriteLine("ties go to even");
foreach (var s in new[] { "0.12345675", "0.12345665", "0.12345685", "0.123456750000001", "-0.12345675", "1234567.5", "1234568.5", "12345675", "12345665" })
    Console.WriteLine($"{s} -> {Decimal32.Parse(s, ci)}");

Console.WriteLine("standard formats");
var v = Decimal32.Parse("1234.5600", ci);
foreach (var f in new[] { "G", "G3", "F", "F2", "F0", "E", "E3", "N", "N1", "P", "R", "e", "g", "f4", "n", "0.00", "#,##0.0", "0.###E+0" })
{
    try { Console.WriteLine($"{f}: {v.ToString(f, ci)}"); } catch (Exception e) { Console.WriteLine($"{f}: {e.GetType().Name}"); }
}
foreach (var f in new[] { "D", "X" })
{
    try { Console.WriteLine($"{f}: {v.ToString(f, ci)}"); } catch (Exception e) { Console.WriteLine($"{f}: {e.GetType().Name}"); }
}
foreach (var f in new[] { "F2", "E2", "N2", "G", "G5", "F0", "0.00", "#,##0.0", "0.###E+0" })
    foreach (var s in new[] { "-0", "0.000", "0E3", "-0.00" })
        Console.WriteLine($"{s} {f} -> '{Decimal32.Parse(s, ci).ToString(f, ci)}'");

Console.WriteLine("rounding in formats");
foreach (var f in new[] { "F1", "N1", "E1", "G2", "0.0", "#.#", "0.0E+0" })
    Console.WriteLine(f + ": " + string.Join(" ", new[] { "0.25", "0.35", "0.15", "2.5", "0.05", "1.25", "-1.25", "1.251", "9.95", "99.95" }.Select(s => Decimal32.Parse(s, ci).ToString(f, ci))));

Console.WriteLine("cohorts and comparison");
var a = Decimal32.Parse("1.0", ci); var b = Decimal32.Parse("1.00", ci);
Console.WriteLine($"{a == b} {a.Equals(b)} {a.GetHashCode() == b.GetHashCode()} {a.CompareTo(b)} {Decimal32.EncodeBinary(a):X8} {Decimal32.EncodeBinary(b):X8} {a} {b}");
Console.WriteLine($"{Decimal32.NaN == Decimal32.NaN} {Decimal32.NaN.Equals(Decimal32.NaN)} {Decimal32.NaN.CompareTo(Decimal32.One)} {Decimal32.One.CompareTo(Decimal32.NaN)} {Decimal32.Zero == Decimal32.NegativeZero} {Decimal32.Zero.Equals(Decimal32.NegativeZero)} {Decimal32.Zero.GetHashCode() == Decimal32.NegativeZero.GetHashCode()}");
Console.WriteLine(Decimal32.Parse("1.5", ci).CompareTo(Decimal32.Parse("1.50", ci)) + " " + (Decimal32.Parse("-0", ci) < Decimal32.Zero) + " " + Decimal32.NegativeInfinity.CompareTo(Decimal32.MinValue) + " " + (Decimal32.NaN < Decimal32.One) + " " + (Decimal32.NaN != Decimal32.NaN) + " " + Decimal32.Parse("1E3", ci).Equals(Decimal32.Parse("1000", ci)) + " " + (Decimal32.Parse("2", ci) > Decimal32.Parse("1.99", ci)) + " " + (Decimal32.Parse("-2", ci) < Decimal32.Parse("-1.99", ci)));
Console.WriteLine(Decimal32.Max(Decimal32.One, Decimal32.NaN) + " " + Decimal32.Min(Decimal32.Zero, Decimal32.NegativeZero) + " " + Decimal32.Max(Decimal32.Zero, Decimal32.NegativeZero) + " " + Decimal32.Abs(Decimal32.Parse("-1.50", ci)) + " " + (-Decimal32.Parse("1.50", ci)) + " " + Decimal32.Sign(Decimal32.Parse("-3", ci)) + " " + Decimal32.CopySign(Decimal32.One, Decimal32.NegativeOne) + " " + Decimal32.Clamp(Decimal32.Parse("5", ci), Decimal32.One, Decimal32.Parse("3", ci)));
foreach (var t in new Action[] { () => Console.WriteLine(Decimal32.Sign(Decimal32.NaN)), () => Console.WriteLine(Decimal32.Clamp(Decimal32.One, Decimal32.Parse("3", ci), Decimal32.One)), () => Console.WriteLine(Decimal32.Parse("abc", ci)), () => Console.WriteLine(Decimal32.Parse((string)null!, ci)), () => Console.WriteLine(Decimal32.Parse("1e5", NumberStyles.Integer, ci)), () => Console.WriteLine(Decimal32.Parse("(1.5)", NumberStyles.Number | NumberStyles.AllowParentheses, ci)), () => Console.WriteLine(Decimal32.Parse("0x10", NumberStyles.HexNumber, ci)) })
{ try { t(); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); } }

Console.WriteLine("truncation and rounding to a power of ten");
foreach (var s in new[] { "2.5", "3.5", "-2.5", "2.4", "2.6", "1E3", "12.345", "0.4", "-0.4" })
    Console.WriteLine($"{s}: round={Decimal32.Round(Decimal32.Parse(s, ci))} trunc={Decimal32.Truncate(Decimal32.Parse(s, ci))} floor={Decimal32.Floor(Decimal32.Parse(s, ci))} ceil={Decimal32.Ceiling(Decimal32.Parse(s, ci))} r1={Decimal32.Round(Decimal32.Parse(s, ci), 1)} away={Decimal32.Round(Decimal32.Parse(s, ci), MidpointRounding.AwayFromZero)}");

Console.WriteLine("conversions from");
Console.WriteLine((Decimal32)(-5) + " " + (Decimal32)(uint.MaxValue) + " " + (Decimal32)long.MinValue + " " + (Decimal64)long.MinValue + " " + (Decimal64)ulong.MaxValue + " " + (Decimal128)ulong.MaxValue + " " + (Decimal128)Int128.MinValue + " " + (Decimal128)UInt128.MaxValue + " " + (Decimal32)(byte)200 + " " + (Decimal32)'a');
Console.WriteLine((Decimal32)0.1 + " " + (Decimal64)0.1 + " " + (Decimal128)0.1 + " " + (Decimal128)0.1f + " " + (Decimal32)1e20 + " " + (Decimal32)1e-20 + " " + (Decimal32)double.NaN + " " + (Decimal32)double.PositiveInfinity + " " + (Decimal32)1e100 + " " + (Decimal32)0.5 + " " + (Decimal32)0.0 + " " + (Decimal32)(-0.0));
Console.WriteLine((Decimal32)12345678.5m + " " + (Decimal32)0.0000001234567891m + " " + (Decimal64)79228162514264337593543950335m + " " + (Decimal128)79228162514264337593543950335m + " " + (Decimal128)0.0000000000000000000000000001m + " " + (Decimal32)1.50m + " " + (Decimal32)(-0.00m));
Console.WriteLine("conversions to");
Console.WriteLine((int)Decimal32.Parse("12.9", ci) + " " + (int)Decimal32.Parse("-12.9", ci) + " " + (byte)Decimal32.Parse("300", ci) + " " + (long)Decimal32.Parse("1E10", ci) + " " + (double)Decimal32.Parse("0.1", ci) + " " + (float)Decimal32.Parse("0.1", ci) + " " + (decimal)Decimal32.Parse("1.50", ci) + " " + (decimal)Decimal64.Parse("123456789.123456789", ci) + " " + (double)Decimal32.NaN + " " + (double)Decimal32.NegativeInfinity + " " + (int)Decimal32.NaN + " " + (int)Decimal32.PositiveInfinity + " " + (uint)Decimal32.Parse("-5", ci) + " " + (sbyte)Decimal32.NegativeInfinity);
Console.WriteLine((double)Decimal128.Parse("3.141592653589793238462643383279503", ci) + " " + (double)Decimal64.MaxValue + " " + (float)Decimal64.MaxValue + " " + (double)Decimal32.Epsilon + " " + (Int128)Decimal128.Parse("1E30", ci) + " " + (UInt128)Decimal128.Parse("1E38", ci) + " " + (Int128)Decimal128.Parse("-1E39", ci));
foreach (var t in new Action[] { () => Console.WriteLine(checked((int)Decimal32.Parse("1E10", ci))), () => Console.WriteLine(checked((int)Decimal32.NaN)), () => Console.WriteLine(checked((uint)Decimal32.Parse("-1", ci))), () => Console.WriteLine((decimal)Decimal32.Parse("1E30", ci)), () => Console.WriteLine((decimal)Decimal32.PositiveInfinity), () => Console.WriteLine((decimal)Decimal32.NaN) })
{ try { t(); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); } }

Console.WriteLine("widths");
Console.WriteLine((Decimal64)Decimal32.Parse("1.5", ci) + " " + (Decimal128)Decimal64.Parse("1.50", ci) + " " + (Decimal32)Decimal64.Parse("1.23456789", ci) + " " + (Decimal64)Decimal128.Parse("0.1234567890123456789", ci) + " " + (Decimal32)Decimal128.Parse("1E100", ci) + " " + (Decimal32)Decimal128.NaN);
Console.WriteLine("Decimal64 and Decimal128 text");
Console.WriteLine(Decimal64.Parse("1E400", ci) + " " + Decimal64.Parse("-1E-400", ci) + " " + Decimal32.Parse("0E200", ci) + " " + Decimal32.Parse("0E-200", ci) + " " + Decimal128.Parse("0.1", ci) + " " + Decimal128.Parse("1" + new string('0', 40), ci) + " " + Decimal128.Parse("1234567890123456789012345678901234567890", ci) + " " + Decimal64.Parse("12345678901234567890", ci));
Console.WriteLine(string.Join(",", new[] { "1.5", "1.50", "1.500" }.Select(x => Decimal64.Parse(x, ci).ToString("G", ci))));
Console.WriteLine(Decimal128.Parse("123456789012345678901234567890.123456", ci).ToString("N2", ci) + " " + Decimal128.Parse("-123456789012345678901234567890.123456", ci).ToString("E5", ci) + " " + Decimal64.Parse("1234.5678", ci).ToString("C2", ci));
Console.WriteLine(Decimal32.Parse("12345678901234567890E-5", ci));

Console.WriteLine("classification");
foreach (var s in new[] { "1E3", "1.5", "4", "3", "0", "-0.0", "10.0", "7.0", "1E-95", "1000000E-101", "999999E-101", "1E-101" })
{
    var x = Decimal32.Parse(s, ci);
    Console.WriteLine($"{s,-13} int={Decimal32.IsInteger(x)} even={Decimal32.IsEvenInteger(x)} odd={Decimal32.IsOddInteger(x)} normal={Decimal32.IsNormal(x)} sub={Decimal32.IsSubnormal(x)} finite={Decimal32.IsFinite(x)} neg={Decimal32.IsNegative(x)}");
}
Console.WriteLine(Decimal32.IsRealNumber(Decimal32.NaN) + " " + Decimal32.IsFinite(Decimal32.PositiveInfinity) + " " + Decimal32.IsNegative(Decimal32.NaN) + " " + Decimal32.IsPositive(Decimal32.NaN) + " " + Decimal32.IsInfinity(Decimal32.NegativeInfinity) + " " + Decimal32.IsPositiveInfinity(Decimal32.NegativeInfinity) + " " + Decimal32.IsNegativeInfinity(Decimal32.NegativeInfinity) + " " + Decimal32.IsNaN(Decimal32.NaN));

Console.WriteLine("spans and UTF-8");
Span<char> buf = stackalloc char[4];
Console.WriteLine(Decimal32.Parse("123.45", ci).TryFormat(buf, out int w, default, ci) + " " + w);
Span<byte> ub = stackalloc byte[16];
Console.WriteLine(Decimal32.Parse("123.45", ci).TryFormat(ub, out int w2, "F1", ci) + " " + w2 + " " + System.Text.Encoding.UTF8.GetString(ub[..w2]));
Console.WriteLine(Decimal32.TryParse("1,5"u8, new CultureInfo("de-DE"), out var de) + " " + de + " " + de.ToString(new CultureInfo("de-DE")));
Console.WriteLine(Decimal32.TryParse("1.5"u8, ci, out var u8) + " " + u8 + " " + Decimal32.TryParse(""u8, ci, out _) + " " + Decimal32.TryParse(new byte[] { 0xFF }, ci, out _) + " " + Decimal64.Parse("2.50"u8, ci) + " " + Decimal128.Parse("7"u8, NumberStyles.Integer, ci));
Console.WriteLine($"{Decimal32.Parse("1.50", ci)} {Decimal32.Parse("1.5", ci):F3} {string.Format(ci, "{0,10:G}|", Decimal64.Parse("2.75", ci))}");
