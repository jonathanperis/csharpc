// DateTime conversions in Australia/Lord_Howe (TZ set before the local zone is first read, see 188):
// a half-hour daylight-saving shift in the southern hemisphere, and .NET ending the daylight-saving
// period that runs into year 10000 at the end of 9999.
Environment.SetEnvironmentVariable("TZ", "Australia/Lord_Howe");

DateTime[] wallTimes =
[
    new(2024, 1, 15, 12, 0, 0), new(2024, 7, 15, 12, 0, 0),
    new(2024, 4, 7, 1, 29, 0), new(2024, 4, 7, 1, 45, 0), new(2024, 4, 7, 2, 0, 0),
    new(2024, 10, 6, 1, 59, 0), new(2024, 10, 6, 2, 15, 0), new(2024, 10, 6, 2, 30, 0),
    new(1890, 6, 1, 12, 0, 0), new(1981, 6, 1), new(2100, 1, 1),
    new(9999, 1, 15, 12, 0, 0), new(9999, 10, 15, 12, 0, 0), new(9999, 12, 31, 12, 0, 0), new(9999, 12, 31, 23, 40, 0),
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

// UTC instants across the April overlap: the first 1:30-2:00 is flagged as the daylight-saving one.
for (var u = new DateTime(2024, 4, 6, 14, 0, 0, DateTimeKind.Utc); u <= new DateTime(2024, 4, 6, 16, 0, 0, DateTimeKind.Utc); u = u.AddMinutes(30))
{
    var l = u.ToLocalTime();
    Console.WriteLine($"{u:HH:mm}Z -> {l:o} dst={l.IsDaylightSavingTime()} back={l.ToUniversalTime():HH:mm}Z date={l.Date:o} plus={l.AddMinutes(1):o}/{l.AddMinutes(1).ToUniversalTime():HH:mm}Z bin={DateTime.FromBinary(l.ToBinary()).ToUniversalTime():HH:mm}Z");
}
Console.WriteLine($"{DateTime.FromFileTime(0):o} {DateTime.FromFileTime(133500000000000000):o} {DateTime.FromBinary(-1):o}");
var now = DateTime.Now;
Console.WriteLine($"{Math.Abs((now.ToUniversalTime() - DateTime.UtcNow).TotalSeconds) < 60} {now.ToString("zzz") == now.ToString("%K")} {now.ToString("zzz") is "+10:30" or "+11:00"}");

static void Show(string label, DateTime d) =>
    Console.WriteLine($"{label} {d:yyyy-MM-dd HH:mm:ss.fffffff} {d.Kind,-11} {d:yyyy-MM-dd z zz zzz} K={d:%K} o={d:o} r={d:r} dst={d.IsDaylightSavingTime()}");
