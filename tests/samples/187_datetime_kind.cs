// DateTime.Kind: stored in the top bits of the value, preserved by arithmetic, ignored by equality,
// ordering and hashing, and written by the K / o / z format specifiers. Nothing here depends on the
// machine's time zone (see 188-190 for zone-dependent conversions).
using System.Collections.Generic;
using System.Linq;

var utc = new DateTime(2024, 2, 29, 13, 45, 30, 123, DateTimeKind.Utc);
var unspecified = new DateTime(2024, 2, 29, 13, 45, 30, 123);
var local = DateTime.SpecifyKind(unspecified, DateTimeKind.Local);
Console.WriteLine($"{utc.Kind} {unspecified.Kind} {local.Kind} {default(DateTime).Kind} {DateTime.MinValue.Kind} {DateTime.MaxValue.Kind} {DateTime.UnixEpoch.Kind}");
Console.WriteLine($"{new DateTime(5, DateTimeKind.Utc).Kind} {new DateTime(5L).Kind} {new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local).Kind} {new DateTime(2024, 1, 2).Kind}");
Console.WriteLine($"{DateTime.UtcNow.Kind} {DateTime.Now.Kind} {DateTime.Today.Kind} {DateTime.Today.TimeOfDay} {DateTime.Today == DateTime.Now.Date}");
Console.WriteLine($"{utc.Ticks} {local.Ticks} {unspecified.Ticks} {utc.Ticks == local.Ticks} {DateTime.SpecifyKind(utc, DateTimeKind.Unspecified).Kind}");

// Arithmetic keeps the kind.
Console.WriteLine($"{utc.AddDays(1.5).Kind} {utc.AddMonths(13).Kind} {utc.AddYears(-3).Kind} {utc.AddTicks(7).Kind} {utc.AddHours(-2).Kind} {utc.AddMilliseconds(1).Kind}");
Console.WriteLine($"{(utc + TimeSpan.FromDays(3)).Kind} {(local - TimeSpan.FromMinutes(5)).Kind} {utc.Add(TimeSpan.FromSeconds(1)).Kind} {utc.Subtract(TimeSpan.FromSeconds(1)).Kind} {local.Date.Kind} {utc.Date:o}");
Console.WriteLine($"{(utc - local)} {(local - utc)} {utc.Subtract(unspecified)} {utc.TimeOfDay} {local.Year} {local.Month} {local.Day} {local.Hour} {local.Minute} {local.Second} {local.Millisecond} {local.DayOfWeek} {local.DayOfYear}");

// Equality, ordering and hashing ignore the kind.
Console.WriteLine($"{utc == local} {utc != unspecified} {utc.Equals(local)} {DateTime.Equals(utc, unspecified)} {utc.CompareTo(local)} {DateTime.Compare(local, utc)} {utc < local} {utc >= local}");
Console.WriteLine($"{utc.GetHashCode() == local.GetHashCode()} {utc.GetHashCode() == unspecified.GetHashCode()} {utc.GetHashCode()}");
object boxedUtc = utc, boxedLocal = local;
Console.WriteLine($"{boxedUtc.Equals(boxedLocal)} {boxedUtc.GetHashCode() == boxedLocal.GetHashCode()} {utc.Equals((object)unspecified)} {utc.CompareTo((object)local)} {utc.CompareTo(null)} {boxedUtc}");
var byDate = new Dictionary<DateTime, string> { [utc] = "utc" };
byDate[local] = "local";
Console.WriteLine($"{byDate.Count} {byDate[unspecified]} {new HashSet<DateTime> { utc, local, unspecified }.Count} {new List<DateTime> { utc }.Contains(local)} {Array.IndexOf(new[] { unspecified, utc }, local)}");
Console.WriteLine($"{new Stamp(utc, 1) == new Stamp(local, 1)} {new Stamp(utc, 1).GetHashCode() == new Stamp(unspecified, 1).GetHashCode()} {new Pair(utc, 2).Equals(new Pair(local, 2))} {(utc, 3) == (local, 3)} {EqualityComparer<DateTime>.Default.Equals(utc, local)} {Comparer<DateTime>.Default.Compare(utc, local)}");
var outerUtc = new Outer { P = new Pair(utc, 4), N = utc, T = (utc, 5) };
var outerLocal = new Outer { P = new Pair(local, 4), N = local, T = (local, 5) };
Console.WriteLine($"{outerUtc.Equals(outerLocal)} {outerUtc.GetHashCode() == outerLocal.GetHashCode()} {new Pair(utc, 2).GetHashCode() == new Pair(local, 2).GetHashCode()} {new Dictionary<Pair, int> { [new Pair(utc, 9)] = 1 }.ContainsKey(new Pair(unspecified, 9))} {outerUtc.Equals(new Outer { P = new Pair(utc.AddTicks(1), 4), N = utc, T = (utc, 5) })}");
DateTime? maybe = local;
DateTime? none = null;
TimeSpan? noSpan = null, span = TimeSpan.FromHours(1);
Console.WriteLine($"{maybe == utc} {maybe != utc} {none == utc} {none != utc} {none == null} {maybe < utc.AddTicks(1)} {none < utc} {maybe.Value.Kind} {maybe.GetValueOrDefault().Kind}");
Console.WriteLine($"{(utc + span)?.Kind} {(utc + noSpan).HasValue} {(none + span).HasValue} {(none - utc).HasValue} {(maybe - utc)} {span + noSpan == null} {span > TimeSpan.Zero}");
var dates = new[] { utc.AddDays(2), DateTime.SpecifyKind(utc.AddDays(-1), DateTimeKind.Local), unspecified.AddDays(1) };
Array.Sort(dates);
Console.WriteLine(string.Join(" ", dates.Select(d => d.ToString("MM-dd") + d.Kind.ToString()[0])) + " " + dates.Max().Kind + " " + dates.OrderBy(d => d).First().Kind);

// Formatting: K writes Z for Utc and nothing for Unspecified; z is +00:00 for Utc.
Console.WriteLine($"{utc:o} | {unspecified:o} | {utc:O} | {utc:r} | {utc:R} | {utc:u} | {utc:s}");
Console.WriteLine($"{utc:yyyy-MM-ddTHH:mm:ss.fffK} | {unspecified:yyyy-MM-ddTHH:mm:ssK}| {utc:yyyy-MM-dd z zz zzz} | {utc:dd \\K K 'K'}");
Console.WriteLine(string.Format("{0:o} {1:o} {0:%K}{1:%K}| {0}", utc, unspecified) + " " + utc.ToString() + " " + boxedUtc.ToString());
Console.WriteLine(utc.ToString("U") + " | " + DateTime.UnixEpoch.ToString("o") + " | " + DateTime.MaxValue.ToString("o") + " | " + DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc).ToString("o"));

// Conversions that need no time zone.
Console.WriteLine($"{utc.ToUniversalTime() == utc} {utc.ToUniversalTime().Kind} {local.ToLocalTime().Kind} {local.ToLocalTime().Ticks == local.Ticks} {utc.IsDaylightSavingTime()}");
Console.WriteLine($"{utc.ToBinary()} {unspecified.ToBinary()} {DateTime.FromBinary(utc.ToBinary()):o} {DateTime.FromBinary(utc.ToBinary()).Kind} {DateTime.FromBinary(unspecified.ToBinary()).Kind}");
Console.WriteLine($"{utc.ToFileTimeUtc()} {DateTime.FromFileTimeUtc(utc.ToFileTimeUtc()):o} {DateTime.FromFileTimeUtc(0):o} {DateTime.FromFileTime(0).Kind} {utc.ToFileTime() == utc.ToFileTimeUtc()}");
var roundTrip = DateTime.Now;
Console.WriteLine($"{DateTime.FromBinary(roundTrip.ToBinary()) == roundTrip} {roundTrip.ToUniversalTime().ToLocalTime() == roundTrip} {Math.Abs((roundTrip.ToUniversalTime() - DateTime.UtcNow).TotalSeconds) < 60}");

// Errors.
Try(() => new DateTime(2024, 1, 1, 0, 0, 0, (DateTimeKind)3));
Try(() => new DateTime(2024, 13, 1, 0, 0, 0, 0, (DateTimeKind)4));
Try(() => new DateTime(-1, (DateTimeKind)3));
Try(() => new DateTime(10, (DateTimeKind)(-1)));
Try(() => DateTime.SpecifyKind(utc, (DateTimeKind)3));
Try(() => DateTime.FromBinary(long.MaxValue));
Try(() => DateTime.FromFileTimeUtc(-1));
Try(() => DateTime.MinValue.ToFileTimeUtc());
Try(() => DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc).AddTicks(1));
Try(() => utc.CompareTo("2024"));
Try(() => DateTime.MinValue - span);

static void Try(Func<object?> f)
{
    try { Console.WriteLine(f()); }
    catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
}

record Stamp(DateTime When, int Id);
struct Pair
{
    public DateTime When;
    public int Id;
    public Pair(DateTime when, int id) { When = when; Id = id; }
}
struct Outer
{
    public Pair P;
    public DateTime? N;
    public (DateTime, int) T;
}
