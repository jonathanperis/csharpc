// DateTime conversions in a fixed time zone: TZ is set before anything reads the local zone, so
// .NET (TimeZoneInfo.Local) and csharpc (libc) both resolve America/New_York whatever the machine's
// zone is. Covers DST gaps and overlaps (the ambiguous-hour Kind flag), local mean time before 1883
// and the clamping at the ends of the DateTime range.
Environment.SetEnvironmentVariable("TZ", "America/New_York");

DateTime[] wallTimes =
[
    new(2024, 1, 15, 12, 0, 0), new(2024, 7, 15, 12, 0, 0),
    new(2024, 3, 10, 1, 59, 59), new(2024, 3, 10, 2, 0, 0), new(2024, 3, 10, 2, 30, 0), new(2024, 3, 10, 3, 0, 0),
    new(2024, 11, 3, 0, 59, 0), new(2024, 11, 3, 1, 0, 0), new(2024, 11, 3, 1, 30, 0), new(2024, 11, 3, 2, 0, 0),
    new(1850, 6, 1, 12, 0, 0), new(1883, 11, 18, 12, 0, 0), new(1970, 1, 1), new(2100, 6, 1),
    new(1, 1, 1, 1, 0, 0), new(9999, 12, 31, 22, 0, 0), DateTime.MinValue, DateTime.MaxValue,
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

// UTC instants across the November overlap: the first 1:xx is flagged as the daylight-saving one.
for (var u = new DateTime(2024, 11, 3, 4, 30, 0, DateTimeKind.Utc); u <= new DateTime(2024, 11, 3, 7, 0, 0, DateTimeKind.Utc); u = u.AddMinutes(30))
{
    var l = u.ToLocalTime();
    Console.WriteLine($"{u:HH:mm}Z -> {l:o} dst={l.IsDaylightSavingTime()} back={l.ToUniversalTime():HH:mm}Z date={l.Date:o} plus={l.AddMinutes(1):o}/{l.AddMinutes(1).ToUniversalTime():HH:mm}Z bin={DateTime.FromBinary(l.ToBinary()).ToUniversalTime():HH:mm}Z");
}
Console.WriteLine($"{DateTime.FromFileTime(0):o} {DateTime.FromFileTime(133500000000000000):o} {DateTime.FromBinary(-1):o}");
var now = DateTime.Now;
Console.WriteLine($"{Math.Abs((now.ToUniversalTime() - DateTime.UtcNow).TotalSeconds) < 60} {now.ToString("zzz") == now.ToString("%K")} {now.ToString("zzz") is "-05:00" or "-04:00"}");

static void Show(string label, DateTime d) =>
    Console.WriteLine($"{label} {d:yyyy-MM-dd HH:mm:ss.fffffff} {d.Kind,-11} {d:yyyy-MM-dd z zz zzz} K={d:%K} o={d:o} r={d:r} dst={d.IsDaylightSavingTime()}");
