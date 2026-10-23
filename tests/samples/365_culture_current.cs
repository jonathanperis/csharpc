// The current culture drives every implicit format and parse: ToString(), interpolation, concatenation,
// Console.WriteLine(double), string.Format, StringBuilder, composite format with a provider, and culture
// properties (names, parents, RegionInfo).
using System;
using System.Globalization;
using System.Text;

double d = 1234.5;
decimal m = -0.75m;
float f = 2.5f;
int n = -42;
var dt = new DateTime(2026, 10, 23, 8, 5, 3);
foreach (string name in new[] { "", "pt-BR", "de-DE", "en-US", "fr-FR", "ar-EG", "fa-IR" })
{
    CultureInfo.CurrentCulture = new CultureInfo(name);
    var c = CultureInfo.CurrentCulture;
    Console.WriteLine($"[{c.Name}] {d} {m} {f} {n} {dt} {d:N2} {m:C} {0.5:P0} {dt:d} {dt:t}");
    Console.WriteLine(d);
    Console.WriteLine("concat " + d + " " + m + " " + n + " " + -0.0 + " " + double.NaN + " " + (1.0 / 0));
    Console.WriteLine(string.Format("{0} {1:F3} {2,10:N1}|", d, d, -d));
    var sb = new StringBuilder();
    sb.Append(d).Append(' ').Append(m).Append(' ').Append(n).Append(' ').AppendFormat("{0:E2}", d).Append(' ').AppendFormat(CultureInfo.InvariantCulture, "{0}", d);
    Console.WriteLine(sb.ToString());
    Console.WriteLine(d.ToString(CultureInfo.InvariantCulture) + " " + string.Format(CultureInfo.InvariantCulture, "{0} {1}", d, dt) + " " + FormattableString.Invariant($"{d} {dt}"));
    Console.WriteLine($"{c.Name}|{c.TwoLetterISOLanguageName}|{c.ThreeLetterISOLanguageName}|{c.IsNeutralCulture}|{c.Parent.Name}|{c.TextInfo.ListSeparator}|{c.NumberFormat.NativeDigits[1]}|{c.Calendar.GetType().Name}");
    Console.WriteLine(double.Parse(d.ToString()) + " " + decimal.Parse(m.ToString()) + " " + DateTime.Parse(dt.ToString()).ToString("o"));
}
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
foreach (string name in new[] { "pt-BR", "en-US", "ja-JP", "de-CH" })
{
    var r = new RegionInfo(name);
    Console.WriteLine($"{r.Name} {r.TwoLetterISORegionName} {r.ThreeLetterISORegionName} {r.IsMetric} {r.CurrencySymbol} {r.ISOCurrencySymbol} {r.EnglishName}");
}
Console.WriteLine(CultureInfo.GetCultureInfo("pt-BR").Name + " " + ReferenceEquals(CultureInfo.GetCultureInfo("pt-BR"), CultureInfo.GetCultureInfo("pt-BR")) + " " + CultureInfo.GetCultureInfo("pt-BR").IsReadOnly + " " + new CultureInfo("pt-BR").IsReadOnly);
var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
custom.NumberFormat.NumberDecimalSeparator = "_";
custom.NumberFormat.NegativeSign = "~";
Console.WriteLine(1.5.ToString(custom) + " " + (-3).ToString(custom) + " " + custom.IsReadOnly);
try { CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator = ","; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
