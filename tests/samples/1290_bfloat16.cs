// System.Numerics.BFloat16: constants, classification, conversions (rounding from double and float, saturating to
// integers), comparison, the shortest round-trip text of every value, parsing (including the UTF-8 overloads) and
// the arithmetic done in float. The JSON serializer's converter table names the type, so it must exist as a type.
using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

var ci = CultureInfo.InvariantCulture;

static ushort Bits(BFloat16 v) => Unsafe.As<BFloat16, ushort>(ref v);
static BFloat16 FromBits(ushort bits) => Unsafe.As<ushort, BFloat16>(ref bits);

Console.WriteLine("constants");
foreach (var (name, v) in new (string, BFloat16)[] { ("Zero", BFloat16.Zero), ("NegativeZero", BFloat16.NegativeZero), ("One", BFloat16.One), ("NegativeOne", BFloat16.NegativeOne),
    ("Epsilon", BFloat16.Epsilon), ("MaxValue", BFloat16.MaxValue), ("MinValue", BFloat16.MinValue), ("NaN", BFloat16.NaN), ("PositiveInfinity", BFloat16.PositiveInfinity),
    ("NegativeInfinity", BFloat16.NegativeInfinity), ("Pi", BFloat16.Pi), ("E", BFloat16.E), ("Tau", BFloat16.Tau) })
    Console.WriteLine($"{name,-17} {Bits(v):X4} {v.ToString(ci)}");

Console.WriteLine("conversions from float");
foreach (var f in new[] { 0f, -0f, 1f, 1.5f, 0.1f, 3.14159f, 1e10f, 1e-10f, 65504f, 3.4e38f, float.MaxValue, float.NaN, float.PositiveInfinity, 256f, 257f, 258f, 1.00390625f, 100.4f, 0.0001f, 123456f, 1234f, 1e-40f })
{
    var b = (BFloat16)f;
    Console.WriteLine($"{f.ToString("R", ci),-12} -> {Bits(b):X4} {b.ToString(ci),-10} {b.ToString("E3", ci)} {b.ToString("F2", ci)} {((float)b).ToString("R", ci)}");
}

Console.WriteLine("rounding from double is direct, not through float");
Console.WriteLine(Bits((BFloat16)1.00390625000001) + " " + Bits((BFloat16)(float)1.00390625000001) + " " + Bits((BFloat16)1.00390625) + " " + Bits((BFloat16)1.01171875));

Console.WriteLine("integers and other conversions");
Console.WriteLine((int)(BFloat16)3.9f + " " + (int)(BFloat16)(-3.9f) + " " + (long)(BFloat16)1e10f + " " + (byte)(BFloat16)300f + " " + (sbyte)(BFloat16)(-300f) + " " + (uint)(BFloat16)(-5f) + " " + (int)BFloat16.NaN
    + " " + (decimal)(BFloat16)1.5f + " " + (double)(BFloat16)0.1f + " " + (BFloat16)5 + " " + (BFloat16)123456789L + " " + (BFloat16)1.5m + " " + (BFloat16)(byte)7 + " " + (BFloat16)(sbyte)(-7));
try { Console.WriteLine(checked((int)(BFloat16)1e10f)); } catch (OverflowException) { Console.WriteLine("checked overflow"); }

Console.WriteLine("classification");
foreach (var v in new[] { BFloat16.Zero, BFloat16.NegativeZero, BFloat16.One, BFloat16.Epsilon, BFloat16.MaxValue, BFloat16.NaN, BFloat16.PositiveInfinity, BFloat16.NegativeInfinity, (BFloat16)4f, (BFloat16)3f, (BFloat16)1.5f })
    Console.WriteLine($"{v.ToString(ci),-9} nan={BFloat16.IsNaN(v)} inf={BFloat16.IsInfinity(v)} finite={BFloat16.IsFinite(v)} neg={BFloat16.IsNegative(v)} pos={BFloat16.IsPositive(v)} normal={BFloat16.IsNormal(v)} sub={BFloat16.IsSubnormal(v)} int={BFloat16.IsInteger(v)} even={BFloat16.IsEvenInteger(v)} odd={BFloat16.IsOddInteger(v)} pow2={BFloat16.IsPow2(v)} real={BFloat16.IsRealNumber(v)}");

Console.WriteLine("comparison");
Console.WriteLine(BFloat16.NaN.Equals(BFloat16.NaN) + " " + (BFloat16.NaN == BFloat16.NaN) + " " + (BFloat16.NaN != BFloat16.NaN) + " " + BFloat16.NaN.CompareTo(BFloat16.One) + " " + BFloat16.One.CompareTo(BFloat16.NaN)
    + " " + BFloat16.Zero.Equals(BFloat16.NegativeZero) + " " + (BFloat16.One < BFloat16.NaN) + " " + (BFloat16.One <= (BFloat16)1f) + " " + BFloat16.One.CompareTo((BFloat16)2f) + " " + ((object)BFloat16.One).Equals(BFloat16.One));
Console.WriteLine(BFloat16.NaN.GetHashCode() + " " + BFloat16.NegativeZero.GetHashCode() + " " + BFloat16.One.GetHashCode() + " " + ((BFloat16)(-2f)).GetHashCode());

Console.WriteLine("arithmetic and math");
var a = (BFloat16)1.5f; var c = (BFloat16)2.25f;
Console.WriteLine((a + c) + " " + (a - c) + " " + (a * c) + " " + (a / c) + " " + (c % a) + " " + (-a) + " " + (+a) + " " + (BFloat16)1f / (BFloat16)3f);
var inc = a; inc++; var dec = a; dec--;
Console.WriteLine(inc + " " + dec);
Console.WriteLine(BFloat16.Round((BFloat16)2.5f) + " " + BFloat16.Round((BFloat16)3.5f) + " " + BFloat16.Floor((BFloat16)(-2.5f)) + " " + BFloat16.Ceiling((BFloat16)2.1f) + " " + BFloat16.Truncate((BFloat16)(-2.7f)) + " " + BFloat16.Sqrt((BFloat16)2f)
    + " " + BFloat16.Pow((BFloat16)2f, (BFloat16)10f) + " " + BFloat16.Abs((BFloat16)(-2f)) + " " + BFloat16.Max((BFloat16)1f, (BFloat16)2f) + " " + BFloat16.Min((BFloat16)1f, (BFloat16)2f) + " " + BFloat16.Sign((BFloat16)(-2f))
    + " " + BFloat16.CopySign((BFloat16)3f, (BFloat16)(-1f)) + " " + BFloat16.Clamp((BFloat16)9f, (BFloat16)1f, (BFloat16)5f));
try { BFloat16.Sign(BFloat16.NaN); } catch (ArithmeticException e) { Console.WriteLine(e.Message); }

Console.WriteLine("text of every value");
// 65536 bit patterns: the text of each, its parse back, and an order-dependent checksum of all of them.
uint hash = 2166136261;
int notRoundTripping = 0, nanCount = 0;
for (int i = 0; i < 65536; i++)
{
    var v = FromBits((ushort)i);
    string s = v.ToString(ci);
    foreach (char ch in s) hash = (hash ^ ch) * 16777619;
    if (BFloat16.IsNaN(v)) { nanCount++; continue; }
    if (!BFloat16.TryParse(s, ci, out var back) || Bits(back) != Bits(v)) notRoundTripping++;
}
Console.WriteLine($"checksum {hash:X8} nan {nanCount} notRoundTripping {notRoundTripping}");
foreach (ushort bits in new ushort[] { 0x0001, 0x007F, 0x0080, 0x3F80, 0x3F81, 0x4049, 0x449A, 0x4700, 0x4780, 0x7F7F, 0x5A0E, 0x2F80, 0x3A83 })
    Console.WriteLine($"{bits:X4} {FromBits(bits).ToString(ci)} {FromBits(bits).ToString("R", ci)} {FromBits(bits).ToString("G3", ci)} {FromBits(bits).ToString("e2", ci)} {FromBits(bits).ToString("N1", ci)}");
Console.WriteLine(((BFloat16)1234.5f).ToString(new CultureInfo("de-DE")) + " " + ((BFloat16)(-0.5f)).ToString("F3", new CultureInfo("fr-FR")));

Console.WriteLine("parsing");
foreach (var s in new[] { "1", "-1.5", "3.14159", "1e38", "1e39", "3.4e38", "3.39e38", "1.00390625", "1.0039062501", "0.1", "NaN", "Infinity", "-Infinity", "  2 ", "abc", "", "1e-45", "1e-41", "9.18e-41", "4.59e-41", "4.5918e-41", "1,000" })
{
    bool ok = BFloat16.TryParse(s, ci, out var r);
    Console.WriteLine($"[{s}] {ok} {(ok ? Bits(r).ToString("X4") : "")} {(ok ? r.ToString(ci) : "")}");
}
Console.WriteLine(BFloat16.TryParse("1.5"u8, ci, out var u8) + " " + u8 + " " + BFloat16.TryParse("x"u8, ci, out _) + " " + BFloat16.Parse("2.5"u8, ci) + " " + BFloat16.Parse("7"u8, NumberStyles.Integer, ci));
try { BFloat16.Parse("abc", ci); } catch (FormatException e) { Console.WriteLine(e.Message); }
try { BFloat16.Parse((string)null!, ci); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

Console.WriteLine("formatting into spans");
Span<char> chars = stackalloc char[16];
Console.WriteLine(((BFloat16)3.14159f).TryFormat(chars, out int written, default, ci) + " " + chars[..written].ToString());
Span<byte> utf8 = stackalloc byte[3];
Console.WriteLine(((BFloat16)3.14159f).TryFormat(utf8, out int bytes, default, ci) + " " + bytes);
Span<byte> utf8Big = stackalloc byte[16];
Console.WriteLine(((BFloat16)3.14159f).TryFormat(utf8Big, out bytes, "E2", ci) + " " + System.Text.Encoding.UTF8.GetString(utf8Big[..bytes]));
Console.WriteLine($"{(BFloat16)1.5f} {(BFloat16)1.5f:F2} {string.Format(ci, "{0,8:G}", (BFloat16)2.75f)}|");
