// char is IFormattable (format and provider ignored). A boxed char reaching the composite formatter
// through IFormattable used to recurse forever; the DateTime "unmatched quote" message formats one.
using System;
using System.Globalization;

object boxed = 'x';
IFormattable f = 'y';
Console.WriteLine(string.Format("[{0}] [{0,3}] [{1:X4}]", boxed, 'z'));
Console.WriteLine(f.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " " + $"{'q',-3}|" + string.Format(CultureInfo.GetCultureInfo("fr-FR"), "{0}", 'w'));
Console.WriteLine(boxed is IFormattable);
try { Console.WriteLine(new DateTime(2026, 1, 2).ToString("yyyy 'abc", CultureInfo.InvariantCulture)); }
catch (FormatException e) { Console.WriteLine(e.Message); }
