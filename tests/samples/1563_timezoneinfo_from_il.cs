// TimeZoneInfo served from CoreLib's IL (#98): finding zones by IANA id, conversions across DST, adjustment rules, custom zones,
// serialization, invalid and ambiguous times, and the local zone from TZ (1563_timezoneinfo_from_il.env). Display names come from ICU
// and vary with its version, so only whether they are there is printed.
#nullable enable
using System;
using System.Globalization;
using System.Linq;

static class Program
{
    static void Show(string name, Func<object?> f)
    {
        string result;
        try { result = f()?.ToString() ?? "null"; }
        catch (Exception e) { result = e.GetType().Name; }
        Console.WriteLine($"{name}: {result}");
    }

    static void Main()
    {
        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var utc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        Show("ids", () => $"{prague.Id} {newYork.Id} {tokyo.Id} {TimeZoneInfo.Utc.Id}");
        Show("base offsets", () => $"{prague.BaseUtcOffset} {newYork.BaseUtcOffset} {tokyo.BaseUtcOffset}");
        Show("DST support", () => $"{prague.SupportsDaylightSavingTime} {tokyo.SupportsDaylightSavingTime}");
        Show("not found", () => TimeZoneInfo.FindSystemTimeZoneById("Nowhere/Nothing"));
        Show("try find", () => $"{TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out var z)} {z?.Id} {TimeZoneInfo.TryFindSystemTimeZoneById("Nowhere", out _)}");
        Show("system zones", () => TimeZoneInfo.GetSystemTimeZones().Count > 100 && TimeZoneInfo.GetSystemTimeZones().Any(z => z.Id == "Europe/Prague"));
        Show("from UTC", () => $"{TimeZoneInfo.ConvertTimeFromUtc(utc, prague):s} {TimeZoneInfo.ConvertTimeFromUtc(utc, newYork):s}");
        Show("to UTC", () => TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 1, 15, 9, 0, 0), prague).ToString("s"));
        Show("between", () => TimeZoneInfo.ConvertTime(new DateTime(2026, 1, 15, 9, 0, 0), prague, tokyo).ToString("s"));
        Show("by id", () => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "America/New_York").ToString("s"));
        Show("offset", () => TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), prague).ToString("o"));
        Show("summer/winter offset", () => $"{prague.GetUtcOffset(new DateTime(2026, 7, 1))} {prague.GetUtcOffset(new DateTime(2026, 1, 1))}");
        Show("is DST", () => $"{prague.IsDaylightSavingTime(new DateTime(2026, 7, 1))} {prague.IsDaylightSavingTime(new DateTime(2026, 1, 1))}");
        Show("invalid", () => $"{prague.IsInvalidTime(new DateTime(2026, 3, 29, 2, 30, 0))} {prague.IsInvalidTime(new DateTime(2026, 3, 29, 4, 0, 0))}");
        Show("ambiguous", () => $"{prague.IsAmbiguousTime(new DateTime(2026, 10, 25, 2, 30, 0))} {string.Join(",", prague.GetAmbiguousTimeOffsets(new DateTime(2026, 10, 25, 2, 30, 0)))}");
        Show("invalid convert", () => TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 29, 2, 30, 0), prague));
        Show("rules", () =>
        {
            var rule = prague.GetAdjustmentRules()[^1];
            return $"{rule.DaylightDelta} {rule.DaylightTransitionStart.Month} {rule.DaylightTransitionStart.Week} {rule.DaylightTransitionStart.DayOfWeek} {rule.DaylightTransitionEnd.Month} {rule.DaylightTransitionEnd.IsFixedDateRule}";
        });
        Show("floating rule", () => TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday).Week);
        Show("fixed rule", () => TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 31).Day);
        Show("bad rule", () => TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 13, 5, DayOfWeek.Sunday));
        Show("custom", () =>
        {
            var custom = TimeZoneInfo.CreateCustomTimeZone("Custom/Plus2", TimeSpan.FromHours(2), "Plus Two", "Plus Two");
            var same = TimeZoneInfo.CreateCustomTimeZone("Custom/Plus2", TimeSpan.FromHours(2), "Plus Two", "Plus Two");
            return $"{custom.Equals(same)} {custom.HasSameRules(same)} {custom.DisplayName} {custom.ToSerializedString()}";
        });
        Show("serialized round trip", () => TimeZoneInfo.FromSerializedString(prague.ToSerializedString()).HasSameRules(prague));
        Show("display names", () => $"{prague.DisplayName.Length > 0} {prague.StandardName.Length > 0} {prague.DaylightName.Length > 0}");
        Show("local", () => $"{TimeZoneInfo.Local.Id} {TimeZoneInfo.Local.BaseUtcOffset}");
        Show("cleared", () => { TimeZoneInfo.ClearCachedData(); return TimeZoneInfo.Local.Id; });
        Show("legacy TimeZone", () =>
        {
#pragma warning disable CS0618
            return TimeZone.CurrentTimeZone.GetUtcOffset(new DateTime(2026, 1, 1));
#pragma warning restore CS0618
        });
    }
}
