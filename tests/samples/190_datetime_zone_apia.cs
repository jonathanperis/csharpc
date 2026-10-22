// DateTime conversions in Pacific/Apia (TZ set before the local zone is first read, see 188): the
// skipped 30 December 2011 when Samoa crossed the date line, and a local mean time that .NET's
// adjustment rules wrap by a day (-11:26:04 against the base offset +13 becomes +12:34).
Environment.SetEnvironmentVariable("TZ", "Pacific/Apia");

DateTime[] wallTimes =
[
    new(2010, 6, 1, 12, 0, 0), new(2011, 12, 29, 23, 30, 0), new(2011, 12, 30, 0, 0, 0), new(2011, 12, 30, 12, 0, 0),
    new(2011, 12, 31, 0, 30, 0), new(2012, 6, 1), new(2021, 4, 4, 3, 30, 0),
    new(1850, 1, 1), new(1900, 1, 1), new(1911, 6, 1), new(1960, 1, 1),
    new(1, 1, 1, 1, 0, 0), DateTime.MinValue, DateTime.MaxValue,
];
foreach (var wall in wallTimes)
    foreach (var kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Utc, DateTimeKind.Local })
    {
        var d = DateTime.SpecifyKind(wall, kind);
        Show("in", d);
        Show(" u", d.ToUniversalTime());
        Show(" l", d.ToLocalTime());
        Show("lu", d.ToLocalTime().ToUniversalTime());
        Console.WriteLine($"   bin={d.ToBinary()} back={DateTime.FromBinary(d.ToBinary()):o} U={d:U}");
        try { Console.WriteLine($"   ft={d.ToFileTime()} ftu={d.ToFileTimeUtc()}"); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine("   " + e.Message); }
    }

// UTC instants across the skipped day.
for (var u = new DateTime(2011, 12, 30, 8, 0, 0, DateTimeKind.Utc); u <= new DateTime(2011, 12, 30, 11, 0, 0, DateTimeKind.Utc); u = u.AddMinutes(30))
{
    var l = u.ToLocalTime();
    Console.WriteLine($"{u:HH:mm}Z -> {l:o} dst={l.IsDaylightSavingTime()} back={l.ToUniversalTime():HH:mm}Z date={l.Date:o} plus={l.AddMinutes(1):o}/{l.AddMinutes(1).ToUniversalTime():HH:mm}Z bin={DateTime.FromBinary(l.ToBinary()).ToUniversalTime():HH:mm}Z");
}
Console.WriteLine($"{DateTime.FromFileTime(0):o} {DateTime.FromFileTime(133500000000000000):o} {DateTime.FromBinary(-1):o}");
var now = DateTime.Now;
Console.WriteLine($"{Math.Abs((now.ToUniversalTime() - DateTime.UtcNow).TotalSeconds) < 60} {now.ToString("zzz") == now.ToString("%K")} {now.ToString("zzz") is "+13:00" or "+14:00"}");

static void Show(string label, DateTime d) =>
    Console.WriteLine($"{label} {d:yyyy-MM-dd HH:mm:ss.fffffff} {d.Kind,-11} {d:yyyy-MM-dd z zz zzz} K={d:%K} o={d:o} r={d:r} dst={d.IsDaylightSavingTime()}");
