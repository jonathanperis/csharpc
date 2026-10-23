using System;
using System.Globalization;
double d = 1.5;
FormattableString fs = $"{d} {d:N2}";
Console.WriteLine(fs.Format + " " + fs.ArgumentCount);
Console.WriteLine(FormattableString.Invariant($"{d}"));
Console.WriteLine(fs.ToString(new CultureInfo("pt-BR")));
Console.WriteLine(string.Create(new CultureInfo("de-DE"), $"{d} x {d:F3}"));
IFormattable f = fs; Console.WriteLine(f.ToString(null, new CultureInfo("fr-FR")));
