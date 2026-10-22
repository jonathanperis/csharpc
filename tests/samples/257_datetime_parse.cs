// DateTime.Parse/TryParse (the common shapes of .NET's lenient parser), ParseExact/TryParseExact
// (custom and standard formats, styles, offsets and Kind), DateOnly and TimeOnly. The zone is fixed
// so offset conversions are deterministic; inputs whose result depends on today's date are avoided.
using System;
using System.Collections.Generic;
using System.Globalization;

Environment.SetEnvironmentVariable("TZ", "America/New_York");
Part1();
Part2();
Part3();

static void Part1()
{
string[] inputs = { "13/1/2025", "2015-13-05", "1/13/2025", "2015-02-30", "Jan 32 2025", "garbage", "", "  ", "12:61", "25:00",
  "2015-11-05", "1/15/2025 3:30 PM", "January 15, 2025", "15 January 2025", "Jan 15 2025", "2025-01-15T14:30:00", "2025-01-15T14:30:00Z",
  "2025-01-15T14:30:00.1234567+02:00", "2025-01-15 14:30", "Wednesday, January 15, 2025", "Wed, 15 Jan 2025 14:30:00 GMT", "1/15/25", "01/15/2025 23:59:59",
  "2025/01/15", "1-15-2025", "January 2025", "2025-01-15T14:30", "  2015-11-05  ", "2015-11-05T10:00:00.5", "12/31/1999 12:00:00 AM", "12/31/1999 12:00 PM",
  "2025-1-5", "2025-01-15 2:30:00 PM", "Jan 15, 2025 3:45:10 AM", "15-Jan-2025", "2025-01-15T14:30:00-05:00", "2025-01-15 14:30:00Z", "20250115" };
foreach (var s in inputs)
{
    if (DateTime.TryParse(s, out var d)) Console.WriteLine($"[{s}] -> {d:yyyy-MM-dd HH:mm:ss.fffffff} {d.Kind}");
    else Console.WriteLine($"[{s}] -> fail");
}
try { DateTime.Parse("garbage"); } catch (FormatException e) { Console.WriteLine(e.Message); }
try { DateTime.Parse(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
string[] exact = { "2025-01-15T14:30:00|yyyy-MM-ddTHH:mm:ss", "15/01/2025|dd/MM/yyyy", "1/5/2025|M/d/yyyy", "Jan 05 2025|MMM dd yyyy", "20250115|yyyyMMdd",
  "2025-01-15T14:30:00.12|yyyy-MM-ddTHH:mm:ss.FFF", "Wednesday 15 January 2025|dddd dd MMMM yyyy",
  "Tuesday 15 January 2025|dddd dd MMMM yyyy", "2025-01-15 +02:00|yyyy-MM-dd zzz", "2025-01-15T10:00:00Z|yyyy-MM-ddTHH:mm:ssK", "25|yy", "75-03|yy-MM",
  "2025-01-15|d", "01/15/2025|d", "2025-01-15T14:30:00.0000000|o", "2025-01-15T14:30:00.0000000Z|o", "Wed, 15 Jan 2025 14:30:00 GMT|R", "2025-01-15T14:30:00|s",
  "2025-1-15|yyyy-MM-dd", " 2025-01-15|yyyy-MM-dd", "2025-01-15 |yyyy-MM-dd", "2025.01.15|yyyy'.'MM'.'dd", "2025-01-15|yyyy\\-MM\\-dd" };
foreach (var e in exact)
{
    var parts = e.Split('|');
    try { var d = DateTime.ParseExact(parts[0], parts[1], CultureInfo.InvariantCulture); Console.WriteLine($"[{parts[0]}|{parts[1]}] -> {d:yyyy-MM-dd HH:mm:ss.fffffff} {d.Kind}"); }
    catch (FormatException ex) { Console.WriteLine($"[{parts[0]}|{parts[1]}] -> {ex.Message}"); }
}
Console.WriteLine(DateTime.TryParseExact("2025-01-15", "yyyy-MM-dd", null, DateTimeStyles.None, out var r) + " " + r.ToString("o"));
Console.WriteLine(DateTime.TryParseExact("x", "yyyy-MM-dd", null, DateTimeStyles.None, out r) + " " + r.ToString("o"));
Console.WriteLine(DateTime.ParseExact("2025-01-15 10:00", new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm" }, null, DateTimeStyles.None).ToString("o"));
Console.WriteLine(DateTime.Parse("2025-01-15T10:00:00Z", null, DateTimeStyles.AdjustToUniversal).ToString("o"));
Console.WriteLine(DateTime.Parse("2025-01-15T10:00:00Z", null, DateTimeStyles.RoundtripKind).ToString("o"));
Console.WriteLine(DateTime.Parse("2025-01-15T10:00:00", null, DateTimeStyles.AssumeUniversal).ToString("o"));
Console.WriteLine(DateTime.ParseExact("2025-01-15", "yyyy-MM-dd", null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToString("o"));
}
static void Part2()
{
string[] inputs = { "13/1/2025", "2015-13-05", "2015-02-30", "Jan 32 2025", "garbage", "", "  ", "12:61", "25:00", "20250115",
   "2025-01-15 xyz", "2025-01-15T", "10:00 XM", "2025-01-15T25:00", "Febr 3 2025", "2025-01-15 10:00:00 +25:00", "2025", "5", "2025-01",
   "Monday, January 15, 2025", "12/31/1999 13:00 PM", "1/1/0001", "12/31/9999 23:59:59.9999999", "2025-01-15T14:30:00.12345678", "Jan" };
foreach (var s in inputs)
{
    try { var d = DateTime.Parse(s); Console.WriteLine($"[{s}] -> {d:yyyy-MM-dd HH:mm:ss.fffffff} {d.Kind}"); }
    catch (FormatException e) { Console.WriteLine($"[{s}] -> {e.Message}"); }
}
}
static void Part3()
{
static void T(Func<object> f) { try { Console.WriteLine(f()); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); } }
var d = new DateOnly(2024, 2, 29);
Console.WriteLine($"{d} {d.Year} {d.Month} {d.Day} {d.DayOfWeek} {d.DayOfYear} {d.DayNumber}");
Console.WriteLine($"{d:yyyy-MM-dd} {d:D} {d:o} {d:R} {d:M} {d:Y} {d.ToString("dd MMM yyyy")} {d.ToLongDateString()} {d.ToShortDateString()}");
Console.WriteLine($"{d.AddDays(1)} {d.AddMonths(12)} {d.AddYears(1)} {d.AddDays(-60)}");
Console.WriteLine($"{DateOnly.MinValue} {DateOnly.MaxValue} {DateOnly.FromDayNumber(738000)} {DateOnly.FromDateTime(new DateTime(2020, 1, 2, 3, 4, 5))}");
Console.WriteLine($"{d == new DateOnly(2024, 2, 29)} {d < d.AddDays(1)} {d.CompareTo(d.AddDays(-1))} {d.Equals((object)d)} {d.GetHashCode()}");
Console.WriteLine($"{d.ToDateTime(new TimeOnly(13, 45))} {d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Kind}");
var (y, m, dd) = d; Console.WriteLine($"{y} {m} {dd}");
T(() => new DateOnly(2023, 2, 29));
T(() => DateOnly.FromDayNumber(-1));
T(() => DateOnly.Parse("1/15/2025"));
T(() => DateOnly.Parse("January 15, 2025", CultureInfo.InvariantCulture));
T(() => DateOnly.Parse("2025-01-15"));
T(() => DateOnly.Parse("2025-01-15 10:00"));
T(() => DateOnly.Parse("garbage"));
T(() => DateOnly.ParseExact("21 Oct 2015", "dd MMM yyyy", CultureInfo.InvariantCulture));
T(() => DateOnly.ParseExact("1/15/2025", new[] { "MM/dd/yyyy", "M/d/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None));
T(() => DateOnly.ParseExact("2025-01-15", "yyyy-MM-dd HH"));
T(() => DateOnly.ParseExact("x", "yyyy-MM-dd"));
T(() => DateOnly.TryParse("2025-02-30", out var r) + " " + r);
var dict = new Dictionary<DateOnly, int> { [d] = 1, [d.AddDays(1)] = 2 };
Console.WriteLine(dict[new DateOnly(2024, 3, 1)] + " " + dict.ContainsKey(new DateOnly(2024, 2, 29)));
var t = new TimeOnly(14, 30, 15, 123);
Console.WriteLine($"{t} {t.Hour} {t.Minute} {t.Second} {t.Millisecond} {t.Ticks} {t:HH:mm:ss.fff} {t:t} {t:T} {t:o} {t:r} {t.ToLongTimeString()} {t.ToShortTimeString()}");
Console.WriteLine($"{t.AddHours(10)} {t.AddMinutes(-900)} {t.Add(TimeSpan.FromHours(12))} {t.ToTimeSpan()} {TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(75))} {TimeOnly.FromDateTime(new DateTime(2020, 1, 1, 7, 8, 9))}");
Console.WriteLine($"{t.IsBetween(new TimeOnly(14, 0), new TimeOnly(15, 0))} {t.IsBetween(new TimeOnly(22, 0), new TimeOnly(2, 0))} {new TimeOnly(23, 0) - new TimeOnly(1, 0)} {TimeOnly.MinValue} {TimeOnly.MaxValue}");
Console.WriteLine($"{t == new TimeOnly(14, 30, 15, 123)} {t > TimeOnly.MinValue} {t.CompareTo(TimeOnly.MaxValue)} {t.GetHashCode()}");
T(() => new TimeOnly(24, 0));
T(() => TimeOnly.Parse("14:30:15"));
T(() => TimeOnly.Parse("2:30 PM", CultureInfo.InvariantCulture));
T(() => TimeOnly.Parse("14:30:15.123"));
T(() => TimeOnly.Parse("2025-01-15 10:00"));
T(() => TimeOnly.ParseExact("5:00 pm", "h:mm tt", CultureInfo.InvariantCulture));
T(() => TimeOnly.ParseExact("17:30:25", "HH:mm:ss", CultureInfo.InvariantCulture));
T(() => TimeOnly.ParseExact("2:30 PM", new[] { "HH:mm", "h:mm tt" }, CultureInfo.InvariantCulture, DateTimeStyles.None));
T(() => TimeOnly.TryParse("25:00", out var r) + " " + r);
}
