// System.TimeZoneInfo: the local zone and UTC, over csharpc's local-time code (DateTimeOps: the
// process's TZ / tzdata rules, as DateTime.ToLocalTime uses). Offsets only: zone ids, names, adjustment
// rules and FindSystemTimeZoneById are not implemented yet (CSLLVM0002).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [Shadows("System.TimeZoneInfo")]
    public sealed class TimeZoneInfoImpl
    {
        private static TimeZoneInfo s_local, s_utc;
        private readonly bool _utc;

        private TimeZoneInfoImpl(bool utc) { _utc = utc; }

        public static TimeZoneInfo Local => s_local ??= (TimeZoneInfo)(object)new TimeZoneInfoImpl(false);
        public static TimeZoneInfo Utc => s_utc ??= (TimeZoneInfo)(object)new TimeZoneInfoImpl(true);

        public TimeSpan BaseUtcOffset => _utc ? TimeSpan.Zero : new TimeSpan(DateTimeOps.BaseOffset() * TimeSpan.TicksPerSecond);

        /// <summary>The offset at a wall-clock time (Local / Unspecified) or at a UTC instant.</summary>
        public TimeSpan GetUtcOffset(DateTime dateTime) =>
            _utc ? TimeSpan.Zero : new TimeSpan(DateTimeOps.LocalUtcOffsetTicks(CoreIntrinsics.DateTimeData(dateTime)));

        public TimeSpan GetUtcOffset(DateTimeOffset dateTimeOffset) =>
            _utc ? TimeSpan.Zero : new TimeSpan(DateTimeOps.OffsetFromUtcTicks(dateTimeOffset.UtcTicks, out bool _));

        // CoreLib-internal members that the framework's DateTimeParse and DateTimeFormat call (#426). The options only choose whether an
        // invalid local time throws, which these offsets never do.
        internal TimeSpan GetUtcOffset(DateTime dateTime, Globalization.TimeZoneInfoOptions flags) => GetUtcOffset(dateTime);

        internal static TimeSpan GetLocalUtcOffset(DateTime dateTime, Globalization.TimeZoneInfoOptions flags) => Local.GetUtcOffset(dateTime);

        internal static TimeSpan GetUtcOffsetFromUtc(DateTime time, TimeZoneInfo zone, out bool isDaylightSavings, out bool isAmbiguousLocalDst)
        {
            isDaylightSavings = false;
            isAmbiguousLocalDst = false;
            if (((TimeZoneInfoImpl)(object)zone)._utc) return TimeSpan.Zero;
            return new TimeSpan(DateTimeOps.OffsetFromUtcTicks(time.Ticks, out isAmbiguousLocalDst));
        }

        public bool IsDaylightSavingTime(DateTime dateTime)
        {
            if (_utc) return false;
            // A UTC time is converted to the zone first, as .NET does.
            if (dateTime.Kind == DateTimeKind.Utc) dateTime = dateTime.ToLocalTime();
            return DateTimeOps.IsDaylightSavingTime(CoreIntrinsics.DateTimeData(dateTime));
        }
    }
}
