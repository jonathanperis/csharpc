// DateTime/TimeSpan arithmetic and formatting (corelib DateTimeOps), including boxed values.
using System;

var d = new DateTime(2024, 2, 29, 13, 5, 9, 45);
string[] formats = { "d", "D", "f", "F", "g", "G", "M", "Y", "t", "T", "s", "u", "o", "R", "yyyy-MM-dd HH:mm:ss.fff", "ddd dd MMM yy h:m:s tt", "%d", "'day' d", "HH\\h", "FFFFFFF|fffffff" };
foreach (var f in formats) Console.WriteLine($"{f,-28} {d.ToString(f)}");
Console.WriteLine(d.AddMonths(12) + " | " + d.AddYears(-1) + " | " + d.AddDays(1.5) + " | " + d.AddHours(-13.25) + " | " + d.DayOfWeek + " " + d.DayOfYear);
Console.WriteLine(DateTime.IsLeapYear(1900) + " " + DateTime.IsLeapYear(2000) + " " + DateTime.DaysInMonth(2023, 2) + " " + new DateTime(1, 1, 1).Ticks + " " + DateTime.MaxValue);
var t = new TimeSpan(1, 2, 3, 4, 567);
TimeSpan[] spans = { t, -t, TimeSpan.Zero, TimeSpan.FromMilliseconds(1.5), TimeSpan.FromDays(-0.5), TimeSpan.MaxValue, TimeSpan.MinValue, t * 2.5, t / 3 };
foreach (var s in spans) Console.WriteLine($"{s} | {s:c} | {s:g} | {s:G} | {s.ToString(@"hh\:mm\:ss\.fff")} | {s.ToString("d' days 'h' h'")}");
object[] boxes = { t, d, (object)TimeSpan.FromSeconds(90) };
foreach (var b in boxes) Console.WriteLine(b.ToString() + " " + b);
Console.WriteLine(string.Format("{0} {1:yyyy}", d, d));
try { d.AddYears(9000); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { Console.WriteLine(TimeSpan.MaxValue + t); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { new DateTime(2023, 2, 29); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { t.ToString("x"); } catch (FormatException e) { Console.WriteLine(e.Message); }
Console.WriteLine(string.Format("{0:F2} {1:X}", new Money(3.14159m), new Money(255m)));

record Money(decimal Amount) : IFormattable
{
    public string ToString(string? format, IFormatProvider? provider) => format == "X" ? "hex!" : "$" + Amount.ToString(format);
}
