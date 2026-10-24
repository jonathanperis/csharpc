// DateTimeOffset (a port of .NET's): construction and validation, the clock and UTC views, arithmetic,
// equality and ordering on the UTC instant, Unix times, standard and custom formats (o, r, s, u, zzz, K),
// parsing with offsets, boxing, sorting, Deconstruct and Guid.CreateVersion7(DateTimeOffset). Only explicit
// offsets are used, so the local time zone doesn't matter.
using System;
using System.Globalization;

var d = new DateTimeOffset(2024, 3, 15, 10, 30, 45, 123, TimeSpan.FromHours(-5));
Console.WriteLine(d);
Console.WriteLine(d.ToString("o"));
Console.WriteLine(d.ToString("O"));
Console.WriteLine(d.ToString("r"));
Console.WriteLine(d.ToString("s"));
Console.WriteLine(d.ToString("u"));
Console.WriteLine(d.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
Console.WriteLine(d.ToString("K z zz"));
Console.WriteLine($"{d:yyyy/MM/dd}|{d}");
Console.WriteLine(d.UtcDateTime.ToString("o"));
Console.WriteLine(d.Offset);
Console.WriteLine(d.UtcTicks + " " + d.Ticks);
var u = d.ToUniversalTime();
Console.WriteLine(u + " " + (u == d) + " " + u.EqualsExact(d) + " " + d.Equals(u));
Console.WriteLine(d.ToUnixTimeSeconds() + " " + d.ToUnixTimeMilliseconds());
Console.WriteLine(DateTimeOffset.FromUnixTimeSeconds(1700000000));
Console.WriteLine(DateTimeOffset.FromUnixTimeMilliseconds(-1500).ToString("o"));
Console.WriteLine(DateTimeOffset.UnixEpoch.ToString("o") + " " + DateTimeOffset.MinValue + " " + DateTimeOffset.MaxValue);
Console.WriteLine(d.ToOffset(TimeSpan.FromHours(9)));
Console.WriteLine(d.AddDays(1.5).AddMonths(11).AddYears(-1));
Console.WriteLine((d - u) + " " + (d.AddHours(3) - d) + " " + (d + TimeSpan.FromMinutes(90)));
Console.WriteLine(DateTimeOffset.Compare(d, d.AddTicks(1)) + " " + d.CompareTo(d.AddTicks(-1)));
var p = DateTimeOffset.Parse("2024-01-02T03:04:05.678+02:00", CultureInfo.InvariantCulture);
Console.WriteLine(p.ToString("o") + " " + p.Offset);
var pe = DateTimeOffset.ParseExact("15/03/2024 10:30 +05:30", "dd/MM/yyyy HH:mm zzz", CultureInfo.InvariantCulture);
Console.WriteLine(pe.ToString("O"));
Console.WriteLine(DateTimeOffset.TryParse("garbage", out var g) + " " + g);
Console.WriteLine(DateTimeOffset.TryParseExact("2024-02-30", "yyyy-MM-dd", null, DateTimeStyles.AssumeUniversal, out g) + " " + g);
DateTimeOffset fromDt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
Console.WriteLine(fromDt.ToString("o"));
Console.WriteLine(d.GetHashCode() == u.GetHashCode());
object o = d;
Console.WriteLine(o.Equals(u) + " " + ((IComparable)d).CompareTo(u) + " " + o);
Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:dd MMM yyyy HH:mm zzz}", d));
Span<char> buf = stackalloc char[64];
Console.WriteLine(d.TryFormat(buf, out int n, "s") + " " + buf.Slice(0, n).ToString());
try { new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromSeconds(30)); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(15)); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { DateTimeOffset.FromUnixTimeSeconds(long.MaxValue); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { DateTimeOffset.MinValue.ToOffset(TimeSpan.FromHours(1)); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { DateTimeOffset.Parse("nope"); } catch (FormatException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
var (date, time, off) = d;
Console.WriteLine(date + " " + time + " " + off);
var list = new System.Collections.Generic.List<DateTimeOffset> { d, u.AddHours(-1), d.AddMinutes(5) };
list.Sort();
foreach (var x in list) Console.WriteLine(x.ToString("u"));
DateTimeOffset? nd = null;
Console.WriteLine(nd.HasValue + " " + (nd ?? d).Year + " " + d.DayOfWeek + " " + d.DayOfYear + " " + d.Millisecond + " " + d.TimeOfDay + " " + d.Date);
var g7 = Guid.CreateVersion7(DateTimeOffset.FromUnixTimeMilliseconds(0x0123456789AB));
Console.WriteLine(g7.ToString().Substring(0, 15) + " " + g7.Version);
