// Number parsing with explicit cultures: decimal and group separators, currency symbols, signs,
// parentheses, NBSP group separators, NaN/infinity symbols, NumberStyles, and overflow vs format errors.
using System;
using System.Globalization;

var inv = CultureInfo.InvariantCulture;
void T(string s, IFormatProvider p)
{
    Console.Write(s.Replace(' ', '_').Replace(' ', '~') + " => ");
    Console.Write(double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, p, out var d) ? d.ToString("R", inv) : "fail");
    Console.Write(" | ");
    Console.Write(decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, p, out var m) ? m.ToString(inv) : "fail");
    Console.Write(" | ");
    Console.Write(int.TryParse(s, NumberStyles.Any, p, out var i) ? i.ToString(inv) : "fail");
    Console.Write(" | ");
    Console.WriteLine(long.TryParse(s, NumberStyles.Integer, p, out var l) ? l.ToString(inv) : "fail");
}
foreach (string name in new[] { "pt-BR", "de-DE", "fr-FR", "de-CH", "en-US", "sv-SE", "" })
{
    var c = new CultureInfo(name);
    Console.WriteLine("[" + name + "]");
    foreach (string s in new[] { "1,5", "1.5", "1.234,56", "1,234.56", "-12", "(12)", "12-", "R$ 1.234,56", "1 234,5", "1 234,5", "1 234,5", "NaN",
                 "∞", "-∞", "Infinity", "-Infinity", "1e5", "  42  ", "€12", "12 €", "+7", "CHF 3.50", "1'234.5", "−5", "0x10", "", " ", "1,2,3", "9999999999" })
        T(s, c);
}
try { int.Parse("abc"); } catch (FormatException e) { Console.WriteLine(e.Message); }
try { int.Parse("99999999999"); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { byte.Parse("-1"); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { decimal.Parse("1e30", NumberStyles.Float); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { int.Parse("1", NumberStyles.HexNumber | NumberStyles.AllowDecimalPoint); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
Console.WriteLine(uint.Parse("-0") + " " + int.Parse("FF", NumberStyles.HexNumber) + " " + long.Parse("1,234,567", NumberStyles.AllowThousands, inv)
    + " " + int.Parse("1.000", NumberStyles.AllowDecimalPoint, inv) + " " + short.Parse("-32768") + " " + ulong.Parse("18446744073709551615") + " " + sbyte.Parse("  -128\0\0"));
try { Console.WriteLine(int.Parse("1.5", NumberStyles.AllowDecimalPoint, inv)); } catch (OverflowException e) { Console.WriteLine(e.Message); }
Console.WriteLine(double.Parse("1.7976931348623157E+308", inv) + " " + double.Parse("4.9e-324", inv) + " " + float.Parse("3.4028235E+38", inv) + " " + decimal.Parse("0.00000000000000000000000000001", inv));
CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
Console.WriteLine(double.Parse("3,25") + " " + decimal.Parse("1.000,5") + " " + float.Parse("0,1") + " " + Convert.ToDouble("2,5") + " " + int.Parse("-7") + " " + double.TryParse("1.5", out var r) + " " + r);
