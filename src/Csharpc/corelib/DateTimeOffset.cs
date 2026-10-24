// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Private.CoreLib/src/System/
// DateTimeOffset.cs (+ DateTimeOffset.NonAndroid.cs); see THIRD-PARTY-NOTICES.md. Same layout (the UTC
// DateTime, Kind Unspecified, and the offset in minutes); formatting and parsing are .NET's DateTimeFormat /
// DateTimeParse (corelib Globalization/) with the offset, the local zone is csharpc's (DateTimeOps, libc's
// TZ / tzdata, as .NET reads). Values passed in as the real `DateTimeOffset` are read through a pointer cast
// (same layout, no boxing). Binary serialization (ISerializable) is not implemented.
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.DateTimeOffset")]
    public readonly unsafe struct DateTimeOffsetImpl : IComparable, ISpanFormattable, IComparable<DateTimeOffset>, IEquatable<DateTimeOffset>,
        ISpanParsable<DateTimeOffset>, IUtf8SpanFormattable
    {
        private const long TicksPerMillisecond = 10000L;
        private const long TicksPerSecond = 10000000L;
        private const long TicksPerMinute = 600000000L;
        private const long MaxTicks = 3155378975999999999L;
        private const long UnixEpochTicks = 621355968000000000L;
        private const long KindUtc = 0x4000000000000000;

        private const int MaxOffsetMinutes = 14 * 60;
        private const int MinOffsetMinutes = -MaxOffsetMinutes;

        private const long UnixEpochSeconds = UnixEpochTicks / TicksPerSecond; // 62,135,596,800
        private const long UnixEpochMilliseconds = UnixEpochTicks / TicksPerMillisecond; // 62,135,596,800,000

        internal const long UnixMinSeconds = 0 / TicksPerSecond - UnixEpochSeconds;
        internal const long UnixMaxSeconds = MaxTicks / TicksPerSecond - UnixEpochSeconds;

        // .NET's static readonly fields: MinValue is the default value; MaxValue and UnixEpoch are read
        // through BclValueTypes' field table (corelib types get no static constructor).
        public static readonly DateTimeOffset MinValue;
        internal static DateTimeOffset GetMaxValue() => To(new DateTimeOffsetImpl(0, CoreIntrinsics.MakeDateTime(MaxTicks)));
        internal static DateTimeOffset GetUnixEpoch() => To(new DateTimeOffsetImpl(0, CoreIntrinsics.MakeDateTime(UnixEpochTicks)));

        private readonly DateTime _dateTime;
        private readonly int _offsetMinutes;

        private static DateTimeOffsetImpl Of(DateTimeOffset d) => *(DateTimeOffsetImpl*)&d;
        private static DateTimeOffset To(DateTimeOffsetImpl d) => *(DateTimeOffset*)&d;

        private DateTimeOffsetImpl(int validOffsetMinutes, DateTime validDateTime)
        {
            _dateTime = validDateTime;
            _offsetMinutes = validOffsetMinutes;
        }

        public DateTimeOffsetImpl(long ticks, TimeSpan offset) : this(ValidateOffset(offset), ValidateDate(new DateTime(ticks), offset))
        {
        }

        private static DateTimeOffset CreateValidateOffset(DateTime dateTime, TimeSpan offset) =>
            To(new DateTimeOffsetImpl(ValidateOffset(offset), ValidateDate(dateTime, offset)));

        // Local and Unspecified are both treated as Local.
        public DateTimeOffsetImpl(DateTime dateTime)
        {
            if (dateTime.Kind != DateTimeKind.Utc)
            {
                TimeSpan offset = Globalization.TimeZoneInfo.GetLocalUtcOffset(dateTime, Globalization.TimeZoneInfoOptions.NoThrowOnInvalidTime);
                _offsetMinutes = ValidateOffset(offset);
                _dateTime = ValidateDate(dateTime, offset);
            }
            else
            {
                _offsetMinutes = 0;
                _dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
            }
        }

        public DateTimeOffsetImpl(DateTime dateTime, TimeSpan offset)
        {
            if (dateTime.Kind == DateTimeKind.Local)
            {
                if (offset != Globalization.TimeZoneInfo.GetLocalUtcOffset(dateTime, Globalization.TimeZoneInfoOptions.NoThrowOnInvalidTime))
                    throw new ArgumentException("The UTC Offset of the local dateTime parameter does not match the offset argument.", "offset");
            }
            else if (dateTime.Kind == DateTimeKind.Utc)
            {
                if (offset.Ticks != 0)
                    throw new ArgumentException("The UTC Offset for Utc DateTime instances must be 0.", "offset");
            }
            _offsetMinutes = ValidateOffset(offset);
            _dateTime = ValidateDate(dateTime, offset);
        }

        public DateTimeOffsetImpl(DateOnly date, TimeOnly time, TimeSpan offset) : this(date.ToDateTime(time), offset)
        {
        }

        // Leap seconds: DateTime.SystemSupportsLeapSeconds is false on Unix, so second 60 is rejected by DateTime.
        public DateTimeOffsetImpl(int year, int month, int day, int hour, int minute, int second, TimeSpan offset)
        {
            _offsetMinutes = ValidateOffset(offset);
            _dateTime = ValidateDate(new DateTime(year, month, day, hour, minute, second), offset);
        }

        public DateTimeOffsetImpl(int year, int month, int day, int hour, int minute, int second, int millisecond, TimeSpan offset)
            : this(year, month, day, hour, minute, second, offset)
        {
            if ((uint)millisecond >= 1000) ThrowMillisecondOutOfRange();
            _dateTime = CoreIntrinsics.MakeDateTime(UtcTicks + (uint)millisecond * TicksPerMillisecond);
        }

        public DateTimeOffsetImpl(int year, int month, int day, int hour, int minute, int second, int millisecond, Calendar calendar, TimeSpan offset)
        {
            if (calendar == null) throw new ArgumentNullException("calendar");
            _offsetMinutes = ValidateOffset(offset);
            _dateTime = ValidateDate(calendar.ToDateTime(year, month, day, hour, minute, second, millisecond), offset);
        }

        public DateTimeOffsetImpl(int year, int month, int day, int hour, int minute, int second, int millisecond, int microsecond, TimeSpan offset)
            : this(year, month, day, hour, minute, second, millisecond, offset)
        {
            if ((uint)microsecond >= 1000) ThrowMicrosecondOutOfRange();
            _dateTime = CoreIntrinsics.MakeDateTime(UtcTicks + (uint)microsecond * 10L);
        }

        public DateTimeOffsetImpl(int year, int month, int day, int hour, int minute, int second, int millisecond, int microsecond, Calendar calendar, TimeSpan offset)
            : this(year, month, day, hour, minute, second, millisecond, calendar, offset)
        {
            if ((uint)microsecond >= 1000) ThrowMicrosecondOutOfRange();
            _dateTime = CoreIntrinsics.MakeDateTime(UtcTicks + (uint)microsecond * 10L);
        }

        private static void ThrowMillisecondOutOfRange() =>
            throw new ArgumentOutOfRangeException("millisecond", "Valid values are between 0 and 999, inclusive.");

        private static void ThrowMicrosecondOutOfRange() =>
            throw new ArgumentOutOfRangeException("microsecond", "Valid values are between 0 and 999, inclusive.");

        public static DateTimeOffset UtcNow => To(new DateTimeOffsetImpl(0, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)));

        public static DateTimeOffset Now => ToLocalTime(DateTime.UtcNow, true);

        public DateTime DateTime => ClockDateTime;

        public DateTime UtcDateTime => CoreIntrinsics.MakeDateTime(UtcTicks | KindUtc);

        public DateTime LocalDateTime => UtcDateTime.ToLocalTime();

        // Adjust to a given offset with the same UTC time.
        public DateTimeOffset ToOffset(TimeSpan offset) => CreateValidateOffset(_dateTime + offset, offset);

        // The clock time: for display, not identity.
        private DateTime ClockDateTime => CoreIntrinsics.MakeDateTime(UtcTicks + _offsetMinutes * TicksPerMinute);

        public DateTime Date => ClockDateTime.Date;
        public int Day => ClockDateTime.Day;
        public DayOfWeek DayOfWeek => ClockDateTime.DayOfWeek;
        public int DayOfYear => ClockDateTime.DayOfYear;
        public int Hour => ClockDateTime.Hour;
        public int Millisecond => UtcDateTime.Millisecond;
        public int Microsecond => UtcDateTime.Microsecond;
        public int Nanosecond => UtcDateTime.Nanosecond;
        public int Minute => ClockDateTime.Minute;
        public int Month => ClockDateTime.Month;
        public TimeSpan Offset => new TimeSpan(_offsetMinutes * TicksPerMinute);
        public int TotalOffsetMinutes => _offsetMinutes;
        public int Second => UtcDateTime.Second;
        public long Ticks => ClockDateTime.Ticks;
        public long UtcTicks => CoreIntrinsics.DateTimeData(_dateTime);
        public TimeSpan TimeOfDay => ClockDateTime.TimeOfDay;
        public int Year => ClockDateTime.Year;

        public DateTimeOffset Add(TimeSpan timeSpan) => Add(ClockDateTime.Add(timeSpan));
        public DateTimeOffset AddDays(double days) => Add(ClockDateTime.AddDays(days));
        public DateTimeOffset AddHours(double hours) => Add(ClockDateTime.AddHours(hours));
        public DateTimeOffset AddMilliseconds(double milliseconds) => Add(ClockDateTime.AddMilliseconds(milliseconds));
        public DateTimeOffset AddMicroseconds(double microseconds) => Add(ClockDateTime.AddMicroseconds(microseconds));
        public DateTimeOffset AddMinutes(double minutes) => Add(ClockDateTime.AddMinutes(minutes));
        public DateTimeOffset AddMonths(int months) => Add(ClockDateTime.AddMonths(months));
        public DateTimeOffset AddSeconds(double seconds) => Add(ClockDateTime.AddSeconds(seconds));
        public DateTimeOffset AddTicks(long ticks) => Add(ClockDateTime.AddTicks(ticks));
        public DateTimeOffset AddYears(int years) => Add(ClockDateTime.AddYears(years));

        private DateTimeOffset Add(DateTime dateTime) => To(new DateTimeOffsetImpl(_offsetMinutes, ValidateDate(dateTime, Offset)));

        public static int Compare(DateTimeOffset first, DateTimeOffset second) => Of(first).UtcTicks.CompareTo(Of(second).UtcTicks);

        int IComparable.CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (!(obj is DateTimeOffset other)) throw new ArgumentException("Object must be of type DateTimeOffset.");
            return UtcTicks.CompareTo(Of(other).UtcTicks);
        }

        public int CompareTo(DateTimeOffset other) => UtcTicks.CompareTo(Of(other).UtcTicks);

        public override bool Equals(object obj) => obj is DateTimeOffset other && UtcTicks == Of(other).UtcTicks;

        public bool Equals(DateTimeOffset other) => UtcTicks == Of(other).UtcTicks;

        // True when the ClockDateTime, Kind and Offset match.
        public bool EqualsExact(DateTimeOffset other) => UtcTicks == Of(other).UtcTicks && _offsetMinutes == Of(other)._offsetMinutes;

        public static bool Equals(DateTimeOffset first, DateTimeOffset second) => Of(first).UtcTicks == Of(second).UtcTicks;

        public static DateTimeOffset FromFileTime(long fileTime) => ToLocalTime(DateTime.FromFileTimeUtc(fileTime), true);

        public static DateTimeOffset FromUnixTimeSeconds(long seconds)
        {
            if (seconds < UnixMinSeconds || seconds > UnixMaxSeconds)
                ThrowRange("seconds", seconds, UnixMinSeconds, UnixMaxSeconds);
            long ticks = seconds * TicksPerSecond + UnixEpochTicks;
            return To(new DateTimeOffsetImpl(0, CoreIntrinsics.MakeDateTime(ticks)));
        }

        public static DateTimeOffset FromUnixTimeMilliseconds(long milliseconds)
        {
            const long MinMilliseconds = 0 / TicksPerMillisecond - UnixEpochMilliseconds;
            const long MaxMilliseconds = MaxTicks / TicksPerMillisecond - UnixEpochMilliseconds;
            if (milliseconds < MinMilliseconds || milliseconds > MaxMilliseconds)
                ThrowRange("milliseconds", milliseconds, MinMilliseconds, MaxMilliseconds);
            long ticks = milliseconds * TicksPerMillisecond + UnixEpochTicks;
            return To(new DateTimeOffsetImpl(0, CoreIntrinsics.MakeDateTime(ticks)));
        }

        // ThrowHelper.ThrowArgumentOutOfRange_Range (SR.Format with the current culture, like the concatenation).
        private static void ThrowRange(string parameterName, long value, long minInclusive, long maxInclusive) =>
            throw new ArgumentOutOfRangeException(parameterName, value, "Valid values are between " + minInclusive.ToString() + " and " + maxInclusive.ToString() + ", inclusive.");

        public override int GetHashCode() => UtcTicks.GetHashCode();

        // ---- parsing (.NET's DateTimeParse, capturing the offset) -----------------------------------

        private static Globalization.DateTimeFormatInfo Dtfi(IFormatProvider provider) => Globalization.DateTimeFormatInfo.GetInstance(provider);

        public static DateTimeOffset Parse(string input)
        {
            if (input == null) throw new ArgumentNullException("input");
            DateTime dateResult = Globalization.DateTimeParse.Parse(input, Globalization.DateTimeFormatInfo.CurrentInfo, DateTimeStyles.None, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset Parse(string input, IFormatProvider formatProvider) => Parse(input, formatProvider, DateTimeStyles.None);

        public static DateTimeOffset Parse(string input, IFormatProvider formatProvider, DateTimeStyles styles)
        {
            styles = ValidateStyles(styles);
            if (input == null) throw new ArgumentNullException("input");
            DateTime dateResult = Globalization.DateTimeParse.Parse(input, Dtfi(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset Parse(ReadOnlySpan<char> input, IFormatProvider formatProvider = null, DateTimeStyles styles = DateTimeStyles.None)
        {
            styles = ValidateStyles(styles);
            DateTime dateResult = Globalization.DateTimeParse.Parse(input, Dtfi(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset ParseExact(string input, string format, IFormatProvider formatProvider) =>
            ParseExact(input, format, formatProvider, DateTimeStyles.None);

        public static DateTimeOffset ParseExact(string input, string format, IFormatProvider formatProvider, DateTimeStyles styles)
        {
            styles = ValidateStyles(styles);
            if (input == null) throw new ArgumentNullException("input");
            if (format == null) throw new ArgumentNullException("format");
            DateTime dateResult = Globalization.DateTimeParse.ParseExact(input, format, Dtfi(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset ParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider formatProvider, DateTimeStyles styles = DateTimeStyles.None)
        {
            styles = ValidateStyles(styles);
            DateTime dateResult = Globalization.DateTimeParse.ParseExact(input, format, Dtfi(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset ParseExact(string input, string[] formats, IFormatProvider formatProvider, DateTimeStyles styles)
        {
            styles = ValidateStyles(styles);
            if (input == null) throw new ArgumentNullException("input");
            DateTime dateResult = Globalization.DateTimeParse.ParseExactMultiple(input, formats, Dtfi(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public static DateTimeOffset ParseExact(ReadOnlySpan<char> input, string[] formats, IFormatProvider formatProvider, DateTimeStyles styles = DateTimeStyles.None)
        {
            styles = ValidateStyles(styles);
            DateTime dateResult = Globalization.DateTimeParse.ParseExactMultiple(input, formats, Dtfi(formatProvider), styles, out TimeSpan offset);
            return CreateValidateOffset(dateResult, offset);
        }

        public TimeSpan Subtract(DateTimeOffset value) => new TimeSpan(UtcTicks - Of(value).UtcTicks);

        public DateTimeOffset Subtract(TimeSpan value) => Add(ClockDateTime.Subtract(value));

        public long ToFileTime() => UtcDateTime.ToFileTimeUtc();

        public long ToUnixTimeSeconds()
        {
            // Truncate toward 0001-01-01 (not toward the Unix epoch) before offsetting.
            long seconds = (long)((ulong)UtcTicks / TicksPerSecond);
            return seconds - UnixEpochSeconds;
        }

        public long ToUnixTimeMilliseconds()
        {
            long milliseconds = (long)((ulong)UtcTicks / TicksPerMillisecond);
            return milliseconds - UnixEpochMilliseconds;
        }

        public DateTimeOffset ToLocalTime() => ToLocalTime(UtcDateTime, false);

        private static DateTimeOffset ToLocalTime(DateTime utcDateTime, bool throwOnOverflow)
        {
            TimeSpan offset = Globalization.TimeZoneInfo.GetLocalUtcOffset(utcDateTime, Globalization.TimeZoneInfoOptions.NoThrowOnInvalidTime);
            long localTicks = utcDateTime.Ticks + offset.Ticks;
            if ((ulong)localTicks > MaxTicks)
            {
                if (throwOnOverflow) throw new ArgumentException("Specified argument was out of the range of valid values.");
                localTicks = localTicks < 0 ? 0 : MaxTicks;
            }
            return CreateValidateOffset(CoreIntrinsics.MakeDateTime(localTicks), offset);
        }

        // ---- formatting (.NET's DateTimeFormat with the offset) -------------------------------------

        public override string ToString() => Globalization.DateTimeFormat.Format(ClockDateTime, null, null, Offset);

        public string ToString(string format) => Globalization.DateTimeFormat.Format(ClockDateTime, format, null, Offset);

        public string ToString(IFormatProvider formatProvider) => Globalization.DateTimeFormat.Format(ClockDateTime, null, formatProvider, Offset);

        public string ToString(string format, IFormatProvider formatProvider) => Globalization.DateTimeFormat.Format(ClockDateTime, format, formatProvider, Offset);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider formatProvider = null) =>
            Globalization.DateTimeFormat.TryFormat(ClockDateTime, destination, out charsWritten, format, formatProvider, Offset);

        // UTF-8: formatted as UTF-16, then transcoded (.NET formats into the bytes directly; same result).
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider formatProvider = null)
        {
            string text = Globalization.DateTimeFormat.Format(ClockDateTime, format.Length == 0 ? null : new string(format), formatProvider, Offset);
            int count = System.Text.Encoding.UTF8.GetByteCount(text);
            if (count > utf8Destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            bytesWritten = System.Text.Encoding.UTF8.GetBytes(text.AsSpan(), utf8Destination);
            return true;
        }

        public DateTimeOffset ToUniversalTime() => To(new DateTimeOffsetImpl(0, _dateTime));

        public static bool TryParse(string input, out DateTimeOffset result)
        {
            bool parsed = Globalization.DateTimeParse.TryParse(input, Globalization.DateTimeFormatInfo.CurrentInfo, DateTimeStyles.None, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParse(ReadOnlySpan<char> input, out DateTimeOffset result)
        {
            bool parsed = Globalization.DateTimeParse.TryParse(input, Globalization.DateTimeFormatInfo.CurrentInfo, DateTimeStyles.None, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParse(string input, IFormatProvider formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            if (input == null)
            {
                result = default;
                return false;
            }
            bool parsed = Globalization.DateTimeParse.TryParse(input, Dtfi(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParse(ReadOnlySpan<char> input, IFormatProvider formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            bool parsed = Globalization.DateTimeParse.TryParse(input, Dtfi(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact(string input, string format, IFormatProvider formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            if (input == null || format == null)
            {
                result = default;
                return false;
            }
            bool parsed = Globalization.DateTimeParse.TryParseExact(input, format, Dtfi(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, IFormatProvider formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            bool parsed = Globalization.DateTimeParse.TryParseExact(input, format, Dtfi(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact(string input, string[] formats, IFormatProvider formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            if (input == null)
            {
                result = default;
                return false;
            }
            bool parsed = Globalization.DateTimeParse.TryParseExactMultiple(input, formats, Dtfi(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        public static bool TryParseExact(ReadOnlySpan<char> input, string[] formats, IFormatProvider formatProvider, DateTimeStyles styles, out DateTimeOffset result)
        {
            styles = ValidateStyles(styles);
            bool parsed = Globalization.DateTimeParse.TryParseExactMultiple(input, formats, Dtfi(formatProvider), styles, out DateTime dateResult, out TimeSpan offset);
            result = CreateValidateOffset(dateResult, offset);
            return parsed;
        }

        // Ensures the TimeSpan is valid to go in a DateTimeOffset.
        private static int ValidateOffset(TimeSpan offset)
        {
            long minutes = offset.Ticks / TicksPerMinute;
            if (offset.Ticks != minutes * TicksPerMinute)
                throw new ArgumentException("Offset must be specified in whole minutes.", "offset");
            if (minutes < MinOffsetMinutes || minutes > MaxOffsetMinutes)
                throw new ArgumentOutOfRangeException("offset", "Offset must be within plus or minus 14 hours.");
            return (int)minutes;
        }

        // Both the UTC and the clock time must fit (the clock time was validated by DateTime). The result is Unspecified.
        private static DateTime ValidateDate(DateTime dateTime, TimeSpan offset)
        {
            long utcTicks = dateTime.Ticks - offset.Ticks;
            if ((ulong)utcTicks > MaxTicks)
                throw new ArgumentOutOfRangeException("offset", "The UTC time represented when the offset is applied must be between year 0 and 10,000.");
            return CoreIntrinsics.MakeDateTime(utcTicks);
        }

        private static DateTimeStyles ValidateStyles(DateTimeStyles styles)
        {
            const DateTimeStyles localUniversal = DateTimeStyles.AssumeLocal | DateTimeStyles.AssumeUniversal;
            const DateTimeStyles invalidDateTimeStyles = ~(DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite
                | DateTimeStyles.AllowInnerWhite | DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal
                | DateTimeStyles.AssumeLocal | DateTimeStyles.AssumeUniversal | DateTimeStyles.RoundtripKind);

            if ((styles & (invalidDateTimeStyles | DateTimeStyles.NoCurrentDateDefault)) != 0 || (styles & localUniversal) == localUniversal)
            {
                string message = (styles & invalidDateTimeStyles) != 0 ? "An undefined DateTimeStyles value is being used."
                    : (styles & localUniversal) == localUniversal ? "The DateTimeStyles values AssumeLocal and AssumeUniversal cannot be used together."
                    : "The DateTimeStyles value 'NoCurrentDateDefault' is not allowed when parsing DateTimeOffset.";
                throw new ArgumentException(message, "styles");
            }

            // RoundtripKind does not make sense for DateTimeOffset (ignored for compatibility with DateTime);
            // AssumeLocal is what DateTimeOffset.Parse does by default.
            return styles & (~DateTimeStyles.RoundtripKind & ~DateTimeStyles.AssumeLocal);
        }

        // ---- operators -----------------------------------------------------------------------------

        public static implicit operator DateTimeOffsetImpl(DateTime dateTime) => new DateTimeOffsetImpl(dateTime);

        public static DateTimeOffset operator +(DateTimeOffsetImpl dateTimeOffset, TimeSpan timeSpan) =>
            dateTimeOffset.Add(dateTimeOffset.ClockDateTime + timeSpan);

        public static DateTimeOffset operator -(DateTimeOffsetImpl dateTimeOffset, TimeSpan timeSpan) =>
            dateTimeOffset.Add(dateTimeOffset.ClockDateTime - timeSpan);

        public static TimeSpan operator -(DateTimeOffsetImpl left, DateTimeOffsetImpl right) => new TimeSpan(left.UtcTicks - right.UtcTicks);

        public static bool operator ==(DateTimeOffsetImpl left, DateTimeOffsetImpl right) => left.UtcTicks == right.UtcTicks;
        public static bool operator !=(DateTimeOffsetImpl left, DateTimeOffsetImpl right) => left.UtcTicks != right.UtcTicks;
        public static bool operator <(DateTimeOffsetImpl left, DateTimeOffsetImpl right) => left.UtcTicks < right.UtcTicks;
        public static bool operator <=(DateTimeOffsetImpl left, DateTimeOffsetImpl right) => left.UtcTicks <= right.UtcTicks;
        public static bool operator >(DateTimeOffsetImpl left, DateTimeOffsetImpl right) => left.UtcTicks > right.UtcTicks;
        public static bool operator >=(DateTimeOffsetImpl left, DateTimeOffsetImpl right) => left.UtcTicks >= right.UtcTicks;

        public void Deconstruct(out DateOnly date, out TimeOnly time, out TimeSpan offset)
        {
            DateTime clock = ClockDateTime;
            date = DateOnly.FromDateTime(clock);
            time = TimeOnly.FromDateTime(clock);
            offset = Offset;
        }

        // ---- IParsable / ISpanParsable ----------------------------------------------------------------

        public static bool TryParse(string s, IFormatProvider provider, out DateTimeOffset result) => TryParse(s, provider, DateTimeStyles.None, out result);

        public static DateTimeOffset Parse(ReadOnlySpan<char> s, IFormatProvider provider) => Parse(s, provider, DateTimeStyles.None);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out DateTimeOffset result) => TryParse(s, provider, DateTimeStyles.None, out result);
    }
}
