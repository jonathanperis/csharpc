// DateTime formatting and parsing with explicit cultures: standard and custom patterns, month and day
// names, calendars' first day of week, round trips through the culture's own patterns.
using System;
using System.Globalization;

var d = new DateTime(2026, 3, 7, 14, 5, 9, 123);
var midnight = new DateTime(2024, 12, 31, 0, 0, 0);
string[] cultures = { "", "en-US", "pt-BR", "de-DE", "fr-FR", "ja-JP", "tr-TR", "es-ES", "it-IT", "ru-RU", "zh-CN", "ko-KR", "nl-NL", "sv-SE", "pl-PL", "en-GB", "pt-PT", "hi-IN" };
string[] formats = { "d", "D", "f", "F", "g", "G", "m", "y", "t", "T", "M", "o", "r", "s", "u", "dddd dd MMMM yyyy", "ddd MMM", "hh:mm tt", "%h", "yy-M-d H:m:s.fff", "MMMM", "dddd" };
foreach (string name in cultures)
{
    var c = new CultureInfo(name);
    var f = c.DateTimeFormat;
    Console.WriteLine($"== [{name}] {f.ShortDatePattern} | {f.LongDatePattern} | {f.ShortTimePattern} | {f.LongTimePattern} | '{f.DateSeparator}' '{f.TimeSeparator}' '{f.AMDesignator}' '{f.PMDesignator}' | {f.FirstDayOfWeek} {f.CalendarWeekRule} {c.Calendar.GetType().Name}");
    Console.WriteLine("   " + string.Join(",", f.MonthNames) + " / " + string.Join(",", f.AbbreviatedDayNames));
    foreach (DateTime t in new[] { d, midnight })
    {
        Console.Write("   " + t.ToString(c));
        foreach (string fmt in formats) Console.Write(" | " + t.ToString(fmt, c));
        Console.WriteLine();
    }
    Console.WriteLine("   parse: " + P(() => DateTime.Parse(d.ToString("d", c), c)) + " " + P(() => DateTime.Parse(d.ToString(c), c))
        + " " + P(() => DateTime.Parse(d.ToString("D", c), c)) + " " + P(() => DateTime.ParseExact(d.ToString("F", c), "F", c)));
}
Console.WriteLine(DateTime.ParseExact("sábado, 7 de março de 2026", "dddd, d 'de' MMMM 'de' yyyy", new CultureInfo("pt-BR")).ToString("o"));
Console.WriteLine(DateTime.TryParse("31/02/2026", new CultureInfo("pt-BR"), DateTimeStyles.None, out var x) + " " + x.ToString("o"));
Console.WriteLine(DateTime.TryParse("3/7/2026 2:05 PM", new CultureInfo("en-US"), DateTimeStyles.None, out x) + " " + x.ToString("o"));
Console.WriteLine(DateTime.Parse("7. März 2026", new CultureInfo("de-DE")).ToString("o"));
Console.WriteLine(new TimeSpan(1, 2, 3, 4, 567).ToString("g", new CultureInfo("pt-BR")) + " " + new TimeSpan(1, 2, 3, 4, 567).ToString("G", new CultureInfo("fr-FR")) + " " + new TimeSpan(-5, 0, 0).ToString("c", new CultureInfo("de-DE")));

static string P(Func<DateTime> parse)
{
    try { return parse().ToString("o"); }
    catch (FormatException e) { return "FormatException: " + e.Message; }
}
