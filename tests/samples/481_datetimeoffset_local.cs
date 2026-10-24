// DateTimeOffset and the local time zone: ToLocalTime/LocalDateTime across New York's DST transitions,
// construction from Local/Unspecified DateTimes, parsing without an offset (assumed local), FromFileTime.
// TZ is set before anything reads the local zone (see 188_datetime_zone_new_york).
using System;
using System.Globalization;

Environment.SetEnvironmentVariable("TZ", "America/New_York");
var ci = CultureInfo.InvariantCulture;
var instants = new[]
{
    new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2024, 7, 15, 12, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2024, 3, 10, 6, 59, 59, TimeSpan.Zero),
    new DateTimeOffset(2024, 3, 10, 7, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2024, 11, 3, 5, 30, 0, TimeSpan.Zero),
    new DateTimeOffset(2024, 11, 3, 6, 30, 0, TimeSpan.Zero),
};
foreach (var i in instants)
{
    var local = i.ToLocalTime();
    Console.WriteLine($"{i:o} -> {local:o} {local.LocalDateTime:o} {i.LocalDateTime.Kind} {local.UtcDateTime:o}");
}
var unspecified = new DateTime(2024, 6, 1, 8, 0, 0);
var a = new DateTimeOffset(unspecified);
Console.WriteLine(a.ToString("o", ci) + " " + a.Offset);
DateTimeOffset b = new DateTime(2024, 12, 1, 8, 0, 0, DateTimeKind.Local);
Console.WriteLine(b.ToString("o", ci));
var c = new DateTimeOffset(new DateTime(2024, 12, 1, 8, 0, 0, DateTimeKind.Local), TimeSpan.FromHours(-5));
Console.WriteLine(c.ToString("o", ci));
try { new DateTimeOffset(new DateTime(2024, 12, 1, 8, 0, 0, DateTimeKind.Local), TimeSpan.FromHours(-4)); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { new DateTimeOffset(new DateTime(2024, 12, 1, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(-4)); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
// Parsing without an offset assumes the local zone (or UTC with AssumeUniversal).
Console.WriteLine(DateTimeOffset.Parse("2024-07-04 09:15", ci).ToString("o", ci));
Console.WriteLine(DateTimeOffset.Parse("2024-01-04 09:15", ci, DateTimeStyles.AssumeUniversal).ToString("o", ci));
Console.WriteLine(DateTimeOffset.Parse("2024-01-04T09:15:00Z", ci).ToString("o", ci));
Console.WriteLine(DateTimeOffset.Parse("2024-01-04T09:15:00Z", ci, DateTimeStyles.AdjustToUniversal).ToString("o", ci));
Console.WriteLine(DateTimeOffset.ParseExact("2024-07-04 09:15", "yyyy-MM-dd HH:mm", ci).ToString("o", ci));
try { DateTimeOffset.Parse("2024-01-04", ci, DateTimeStyles.NoCurrentDateDefault); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { DateTimeOffset.Parse("2024-01-04", ci, DateTimeStyles.AssumeLocal | DateTimeStyles.AssumeUniversal); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
Console.WriteLine(DateTimeOffset.FromFileTime(133500000000000000).ToString("o", ci));
Console.WriteLine(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.FromHours(2)).ToFileTime());
Console.WriteLine(new DateTimeOffset(new DateTime(2024, 1, 1)).ToString("zzz K", ci));
var dt = new DateTime(2024, 8, 1, 10, 0, 0, DateTimeKind.Local);
Console.WriteLine(dt.ToString("o", ci) + " " + new DateTimeOffset(dt).ToString("o", ci));
// Equality compares UTC instants; EqualsExact also compares offsets.
var x = new DateTimeOffset(2024, 5, 5, 10, 0, 0, TimeSpan.FromHours(2));
var y = new DateTimeOffset(2024, 5, 5, 4, 0, 0, TimeSpan.FromHours(-4));
Console.WriteLine($"{x == y} {x.Equals(y)} {x.EqualsExact(y)} {x.CompareTo(y)} {DateTimeOffset.Equals(x, y)} {x.GetHashCode() == y.GetHashCode()}");
var set = new System.Collections.Generic.HashSet<DateTimeOffset> { x, y, x.ToLocalTime() };
Console.WriteLine(set.Count);
