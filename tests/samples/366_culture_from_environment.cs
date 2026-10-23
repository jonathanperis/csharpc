// The user culture comes from the environment (366_culture_from_environment.env: LC_ALL=pt_BR.UTF-8):
// the program never sets a culture, yet formats, parses and compares like Brazilian Portuguese.
using System;
using System.Globalization;
using System.Linq;

Console.WriteLine(CultureInfo.CurrentCulture.Name + " " + CultureInfo.CurrentUICulture.Name);
double d = 1234567.891;
Console.WriteLine(d);
Console.WriteLine($"{d:N2} {d:C} {0.25:P} {-5} {double.NaN} {double.NegativeInfinity}");
var dt = new DateTime(2026, 10, 23, 8, 5, 3);
Console.WriteLine(dt + " | " + dt.ToString("D") + " | " + dt.ToShortDateString() + " | " + dt.ToString("MMMM"));
Console.WriteLine(double.Parse("3,5") + " " + decimal.Parse("1.234,5") + " " + DateTime.Parse("23/10/2026").ToString("o"));
Console.WriteLine(string.Join(" ", new[] { "maçã", "Maca", "ábaco", "zebra", "Ábaco" }.OrderBy(s => s)));
Console.WriteLine("ação".ToUpper() + " " + string.Compare("ação", "acao", CultureInfo.CurrentCulture, CompareOptions.IgnoreNonSpace));
