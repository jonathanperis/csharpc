// Number formatting with explicit cultures (ICU data through the culture's NumberFormatInfo):
// separators, group sizes, currency/percent/number patterns, signs, NaN and infinity symbols.
using System;
using System.Globalization;

string[] cultures = { "", "en-US", "pt-BR", "de-DE", "fr-FR", "de-CH", "es-ES", "it-IT", "ja-JP", "hi-IN", "sv-SE", "nl-NL", "ru-RU", "pl-PL", "en-GB", "pt-PT", "zh-CN", "ko-KR" };
double[] doubles = { 0, 1234567.891, -1234567.891, 0.5, -0.125, 1e21, 1.5e-7, double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0 };
string[] formats = { "G", "N", "N0", "N3", "C", "C0", "P", "P1", "F2", "E3", "R", "#,##0.00", "0.###%", "#,#0;(#,#0);zero", "000.0e+0" };

foreach (string name in cultures)
{
    var c = new CultureInfo(name);
    var nf = c.NumberFormat;
    Console.WriteLine($"== [{name}] dec '{nf.NumberDecimalSeparator}' grp '{nf.NumberGroupSeparator}' sizes {string.Join(",", nf.NumberGroupSizes)} cur '{nf.CurrencySymbol}' {nf.CurrencyPositivePattern}/{nf.CurrencyNegativePattern} pct {nf.PercentPositivePattern}/{nf.PercentNegativePattern} neg {nf.NumberNegativePattern} '{nf.NegativeSign}' '{nf.PositiveSign}' nan '{nf.NaNSymbol}' inf '{nf.PositiveInfinitySymbol}' '{nf.NegativeInfinitySymbol}' digits {nf.NumberDecimalDigits}/{nf.CurrencyDecimalDigits}/{nf.PercentDecimalDigits}");
    foreach (double d in doubles)
    {
        Console.Write(d.ToString(c));
        foreach (string f in formats) Console.Write(" | " + d.ToString(f, c));
        Console.WriteLine();
    }
    foreach (long n in new long[] { 0, 7, -42, 1234567890, long.MinValue })
        Console.WriteLine($"{n.ToString(c)} | {n.ToString("N", c)} | {n.ToString("C", c)} | {n.ToString("D8", c)} | {n.ToString("X", c)} | {n.ToString("#,##0", c)} | {n.ToString("P0", c)}");
    foreach (decimal m in new decimal[] { 0m, -1234.5600m, 79228162514264337593543950335m, 0.001m })
        Console.WriteLine($"{m.ToString(c)} | {m.ToString("N", c)} | {m.ToString("C", c)} | {m.ToString("0.00", c)} | {m.ToString("E2", c)}");
    Console.WriteLine($"{1.5f.ToString(c)} | {(-3.25f).ToString("N1", c)} | {((short)-12).ToString(c)} | {((byte)200).ToString("N", c)} | {12345u.ToString("C", c)}");
    Console.WriteLine(string.Format(c, "{0:N2} {1:C} {2:P} {3} {4:F1}", 1234.5, -9.99, 0.075, -1.5, 2.25));
}
