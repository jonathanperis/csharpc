// System.Convert (a port of .NET's Convert.cs and ParseNumbers.cs): conversions between primitives with
// rounding to even and range checks, strings with providers and bases (2, 8, 10, 16), DateTime, and the
// exception types and messages of the failing conversions (overflow, format, invalid base, invalid cast).
#nullable disable
using System;
using System.Globalization;

Console.WriteLine(Convert.ToByte(200) + " " + Convert.ToSByte(-5L) + " " + Convert.ToUInt16('A') + " " + Convert.ToInt16(12.5) + " " + Convert.ToInt16(13.5));
Console.WriteLine(Convert.ToInt32(2.5) + " " + Convert.ToInt32(3.5f) + " " + Convert.ToInt64(-2.5m) + " " + Convert.ToUInt32(4294967295.4));
Console.WriteLine(Convert.ToDecimal(1.1) + " " + Convert.ToDouble(1.1m) + " " + Convert.ToSingle(16777217) + " " + Convert.ToBoolean(0.0) + " " + Convert.ToBoolean(-3));
Console.WriteLine(Convert.ToChar(65) + " " + Convert.ToChar("Z") + " " + Convert.ToString(255, 16) + " " + Convert.ToString((byte)5, 2) + " " + Convert.ToString((short)-1, 8));
Console.WriteLine(Convert.ToByte("ff", 16) + " " + Convert.ToUInt64("777", 8) + " " + Convert.ToInt16("-1", 10) + " " + Convert.ToSByte("80", 16) + " " + Convert.ToInt32("0x1F", 16));
Console.WriteLine(Convert.ToByte("42") + " " + Convert.ToUInt64(" 12 ") + " " + Convert.ToDecimal("1.25", CultureInfo.InvariantCulture) + " " + Convert.ToSingle("1.5") + " " + Convert.ToByte((string)null));
Console.WriteLine(Convert.ToDateTime("2024-01-02 03:04:05", CultureInfo.InvariantCulture).ToString("o") + " " + Convert.ToDateTime((string)null).Ticks);
Console.WriteLine(Convert.ToString(1.5) + " " + Convert.ToString(true) + " " + Convert.ToString('c') + " " + Convert.ToString(12.5m, CultureInfo.InvariantCulture) + " " + Convert.ToString(new DateTime(2020, 5, 6)));
Console.WriteLine(Convert.ToInt32((string)null) + " " + Convert.ToInt64(" -77 ") + " " + Convert.ToDouble("1e3") + " " + Convert.ToBoolean(" TRUE ") + " " + Convert.ToInt32(true) + " " + Convert.ToInt32('7') + " " + Convert.ToDouble(3) + " " + Convert.ToChar(97));
Console.WriteLine(Convert.ToInt32("1010", 2) + " " + Convert.ToInt32("80000000", 16) + " " + Convert.ToInt64("-123", 10) + " " + Convert.ToString(-1, 2) + " " + Convert.ToString(-1L, 16) + " " + Convert.ToString(-8, 10));
foreach (Func<object> f in new Func<object>[] {
    () => Convert.ToByte(300), () => Convert.ToSByte(200), () => Convert.ToInt32(1e10), () => Convert.ToUInt32(-1), () => Convert.ToChar(-1),
    () => Convert.ToChar("ab"), () => Convert.ToBoolean('x'), () => Convert.ToDateTime(5), () => Convert.ToInt32("12", 7),
    () => Convert.ToByte("1ff", 16), () => Convert.ToInt32("zz", 16), () => Convert.ToInt32("", 16), () => Convert.ToUInt16("-1", 10), () => Convert.ToByte("x"), () => Convert.ToInt32(5000000000L), () => Convert.ToChar(70000), () => Convert.ToBoolean("yes"),
    () => Convert.ToInt32("12a"), () => Convert.ToInt64("1", 3), () => Convert.ToInt32("100000000", 16), () => Convert.ToInt32(double.NaN) })
{
    try { Console.WriteLine(f()); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}
// Convert.ToString(object): IFormattable with the provider, else ToString(); null is the empty string.
Func<object, string> toStr = Convert.ToString;
Console.WriteLine(toStr(42) + "|" + toStr(null) + "|" + toStr("s") + "|" + toStr(true) + "|" + toStr('c') + "|" + toStr(1.5) + "|" + toStr(new DateTime(2020, 1, 2)) + "|" + toStr(DayOfWeek.Monday) + "|" + toStr(new P()));
Console.WriteLine(Convert.ToString((object)12.5m, CultureInfo.GetCultureInfo("de-DE")) + "|" + Convert.ToString((object)1.5, CultureInfo.GetCultureInfo("de-DE")) + "|" + Convert.ToString((object)new[] { 1 }));

class P { public override string ToString() => "P!"; }
