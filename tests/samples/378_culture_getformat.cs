// CultureInfo.GetFormat for NumberFormatInfo, DateTimeFormatInfo and other types, directly and through
// IFormatProvider (csharpc reaches the DateTimeFormatInfo case through the runtime; see CultureInfo.cs).
using System;
using System.Globalization;
var c = CultureInfo.GetCultureInfo("de-DE");
var d = (DateTimeFormatInfo)c.GetFormat(typeof(DateTimeFormatInfo))!;
Console.WriteLine(d.MonthNames[2] + " " + (c.GetFormat(typeof(NumberFormatInfo)) as NumberFormatInfo)!.NumberDecimalSeparator + " " + (c.GetFormat(typeof(string)) == null));
IFormatProvider p = c;
Console.WriteLine(((DateTimeFormatInfo)p.GetFormat(typeof(DateTimeFormatInfo))!).ShortDatePattern);
