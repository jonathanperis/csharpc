// Boxed DateTime / TimeSpan values in composite formatting (string.Format, object holes, ToString of
// object), with the current culture and an explicit provider. csharpc formats them through the
// runtime so that programs which never box one don't compile the date formatter.
using System;
using System.Globalization;

var date = new DateTime(2026, 10, 23, 8, 5, 3, 250);
var span = new TimeSpan(1, 2, 3, 4, 567);
object boxedDate = date, boxedSpan = span;
Console.WriteLine(string.Format("{0} | {0:d} | {0:yyyy-MM-dd HH:mm:ss.fff} | {1} | {1:g} | {1:c}", boxedDate, boxedSpan));
Console.WriteLine(string.Format(CultureInfo.GetCultureInfo("de-DE"), "{0:D} | {0:t} | {1:g} | {2,12:N2}", date, span, 1234.5));
Console.WriteLine(string.Format(CultureInfo.GetCultureInfo("ja-JP"), "{0:F} | {1:G}", date, span));
Console.WriteLine(boxedDate.ToString() + " | " + boxedSpan + " | " + $"{boxedDate:MMMM}");
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
Console.WriteLine(string.Format("{0} | {0:f} | {1:g}", boxedDate, boxedSpan));
Console.WriteLine(string.Join(" / ", new object[] { date, span, 2.5 }));
