// System.DateOnly (a day number) and System.TimeOnly (ticks since midnight), as in dotnet/runtime;
// formatting and parsing go through the DateTime implementations (DateTimeOps, DateTimeParse).
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.DateOnly")]
    public readonly struct DateOnlyImpl : IComparable, IComparable<DateOnly>, IEquatable<DateOnly>, IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        private const long TicksPerDay = 864000000000L;
        private const int MaxDayNumber = 3652058;  // 9999-12-31
        private readonly int _dayNumber;

        private DateOnlyImpl(int dayNumber, bool _) { _dayNumber = dayNumber; }

        public DateOnlyImpl(int year, int month, int day)
        {
            _dayNumber = (int)(DateTimeOps.DateToTicks(year, month, day) / TicksPerDay);
        }

        // .NET: the calendar's date as a DateTime (calendar.ToDateTime validates it and throws as .NET does), then its day number (#197).
        public DateOnlyImpl(int year, int month, int day, Calendar calendar)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            _dayNumber = (int)(calendar.ToDateTime(year, month, day, 0, 0, 0, 0).Ticks / TicksPerDay);
        }

        private static DateOnly Make(long dayNumber) => (DateOnly)(object)new DateOnlyImpl((int)dayNumber, false);
        private DateTime Midnight => CoreIntrinsics.MakeDateTime(_dayNumber * TicksPerDay);

        public static DateOnly MinValue => Make(0);
        public static DateOnly MaxValue => Make(MaxDayNumber);

        public static DateOnly FromDayNumber(int dayNumber)
        {
            if ((uint)dayNumber > MaxDayNumber)
                throw new ArgumentOutOfRangeException("dayNumber", dayNumber, "Day number must be between 0 and DateOnly.MaxValue.DayNumber.");
            return Make(dayNumber);
        }

        public static DateOnly FromDateTime(DateTime dateTime) => Make(dateTime.Ticks / TicksPerDay);

        public int DayNumber => _dayNumber;

        // The constructor CoreLib's own DateOnly code (its hosted parsing members) creates values with.
        private DateOnlyImpl(uint dayNumber) { _dayNumber = (int)dayNumber; }
        public int Year => Midnight.Year;
        public int Month => Midnight.Month;
        public int Day => Midnight.Day;
        public DayOfWeek DayOfWeek => (DayOfWeek)((_dayNumber + 1) % 7);
        public int DayOfYear => Midnight.DayOfYear;

        public DateOnly AddDays(int value)
        {
            long n = (long)_dayNumber + value;
            if (n < 0 || n > MaxDayNumber) throw new ArgumentOutOfRangeException("value", "The resulting DateOnly value is out of range.");
            return Make(n);
        }

        public DateOnly AddMonths(int value) => FromDateTime(Midnight.AddMonths(value));
        public DateOnly AddYears(int value) => FromDateTime(Midnight.AddYears(value));

        public void Deconstruct(out int year, out int month, out int day)
        {
            DateTime d = Midnight;
            year = d.Year;
            month = d.Month;
            day = d.Day;
        }

        public DateTime ToDateTime(TimeOnly time) => CoreIntrinsics.MakeDateTime(_dayNumber * TicksPerDay + time.Ticks);
        public DateTime ToDateTime(TimeOnly time, DateTimeKind kind) => DateTime.SpecifyKind(ToDateTime(time), kind);

        public static bool operator ==(DateOnlyImpl left, DateOnlyImpl right) => left._dayNumber == right._dayNumber;
        public static bool operator !=(DateOnlyImpl left, DateOnlyImpl right) => left._dayNumber != right._dayNumber;
        public static bool operator <(DateOnlyImpl left, DateOnlyImpl right) => left._dayNumber < right._dayNumber;
        public static bool operator <=(DateOnlyImpl left, DateOnlyImpl right) => left._dayNumber <= right._dayNumber;
        public static bool operator >(DateOnlyImpl left, DateOnlyImpl right) => left._dayNumber > right._dayNumber;
        public static bool operator >=(DateOnlyImpl left, DateOnlyImpl right) => left._dayNumber >= right._dayNumber;

        public int CompareTo(DateOnly value) => _dayNumber.CompareTo(value.DayNumber);

        public int CompareTo(object value)
        {
            if (value == null) return 1;
            if (!(value is DateOnly other)) throw new ArgumentException("Object must be of type DateOnly.");
            return CompareTo(other);
        }

        public bool Equals(DateOnly value) => _dayNumber == value.DayNumber;
        public override bool Equals(object value) => value is DateOnly other && _dayNumber == other.DayNumber;
        public override int GetHashCode() => _dayNumber;

        public string ToLongDateString() => ToString("D");
        public string ToShortDateString() => ToString();
        public override string ToString() => ToString("d", null);
        public string ToString(IFormatProvider provider) => ToString("d", provider);
        public string ToString(string format) => ToString(format, null);
        // The culture's patterns and names (Globalization/DateOnlyTimeOnlyCulture.cs, .NET's DateOnly.cs).
        public string ToString(string format, IFormatProvider provider) => Globalization.DateOnlyTimeOnlyCulture.FormatDate(_dayNumber, format, provider);
        // TryFormat: ToString's text ("d" for an empty format), copied when it fits.
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(ToString(format.Length == 0 ? "d" : new string(format), provider), destination, out charsWritten);
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(ToString(format.Length == 0 ? "d" : new string(format), provider), utf8Destination, out bytesWritten);

        // Parse / TryParse / ParseExact / TryParseExact: the framework's own, from its IL (src/Csharpc/upstream-types/System.Globalization.txt).
    }

    [Shadows("System.TimeOnly")]
    public readonly struct TimeOnlyImpl : IComparable, IComparable<TimeOnly>, IEquatable<TimeOnly>, IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        private const long TicksPerDay = 864000000000L, TicksPerHour = 36000000000L, TicksPerMinute = 600000000L;
        private const long TicksPerSecond = 10000000L, TicksPerMs = 10000L;
        private readonly long _ticks;

        private TimeOnlyImpl(long ticks, bool _) { _ticks = ticks; }

        // The constructor CoreLib's own TimeOnly code (its hosted parsing members) creates values with.
        private TimeOnlyImpl(ulong ticks) { _ticks = (long)ticks; }

        public TimeOnlyImpl(long ticks)
        {
            if ((ulong)ticks >= TicksPerDay) throw new ArgumentOutOfRangeException("ticks", "Ticks must be between 0 and and TimeOnly.MaxValue.Ticks.");
            _ticks = ticks;
        }

        public TimeOnlyImpl(int hour, int minute) : this(hour, minute, 0, 0) { }
        public TimeOnlyImpl(int hour, int minute, int second) : this(hour, minute, second, 0) { }

        public TimeOnlyImpl(int hour, int minute, int second, int millisecond)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60)
                throw new ArgumentOutOfRangeException(null, "Hour, Minute, and Second parameters describe an un-representable DateTime.");
            if ((uint)millisecond >= 1000) throw new ArgumentOutOfRangeException("millisecond", "Valid values are between 0 and 999, inclusive.");
            _ticks = hour * TicksPerHour + minute * TicksPerMinute + second * TicksPerSecond + millisecond * TicksPerMs;
        }

        // .NET 7+: the microsecond overload, validated after the other parts as .NET does (#197).
        public TimeOnlyImpl(int hour, int minute, int second, int millisecond, int microsecond) : this(hour, minute, second, millisecond)
        {
            if ((uint)microsecond >= 1000) throw new ArgumentOutOfRangeException("microsecond", "Valid values are between 0 and 999, inclusive.");
            _ticks += microsecond * 10L;
        }

        private static TimeOnly Make(long ticks) => (TimeOnly)(object)new TimeOnlyImpl(ticks, false);

        public static TimeOnly MinValue => Make(0);
        public static TimeOnly MaxValue => Make(TicksPerDay - 1);

        public int Hour => (int)(_ticks / TicksPerHour);
        public int Minute => (int)(_ticks / TicksPerMinute % 60);
        public int Second => (int)(_ticks / TicksPerSecond % 60);
        public int Millisecond => (int)(_ticks / TicksPerMs % 1000);
        public int Microsecond => (int)(_ticks / 10 % 1000);
        public int Nanosecond => (int)(_ticks % 10 * 100);
        public long Ticks => _ticks;

        private static long Wrap(long ticks)
        {
            ticks %= TicksPerDay;
            return ticks < 0 ? ticks + TicksPerDay : ticks;
        }

        public TimeOnly Add(TimeSpan value) => Make(Wrap(_ticks + value.Ticks % TicksPerDay));
        public TimeOnly AddHours(double value) => Add(TimeSpan.FromHours(value));
        public TimeOnly AddMinutes(double value) => Add(TimeSpan.FromMinutes(value));

        public bool IsBetween(TimeOnly start, TimeOnly end)
        {
            long s = start.Ticks, e = end.Ticks;
            return s <= e ? s <= _ticks && _ticks < e : _ticks >= s || _ticks < e;
        }

        public static TimeSpan operator -(TimeOnlyImpl t1, TimeOnlyImpl t2) => new TimeSpan(Wrap(t1._ticks - t2._ticks));
        public static bool operator ==(TimeOnlyImpl left, TimeOnlyImpl right) => left._ticks == right._ticks;
        public static bool operator !=(TimeOnlyImpl left, TimeOnlyImpl right) => left._ticks != right._ticks;
        public static bool operator <(TimeOnlyImpl left, TimeOnlyImpl right) => left._ticks < right._ticks;
        public static bool operator <=(TimeOnlyImpl left, TimeOnlyImpl right) => left._ticks <= right._ticks;
        public static bool operator >(TimeOnlyImpl left, TimeOnlyImpl right) => left._ticks > right._ticks;
        public static bool operator >=(TimeOnlyImpl left, TimeOnlyImpl right) => left._ticks >= right._ticks;

        public static TimeOnly FromTimeSpan(TimeSpan timeSpan) => (TimeOnly)(object)new TimeOnlyImpl(timeSpan.Ticks);
        public static TimeOnly FromDateTime(DateTime dateTime) => Make(dateTime.TimeOfDay.Ticks);
        public TimeSpan ToTimeSpan() => new TimeSpan(_ticks);

        public void Deconstruct(out int hour, out int minute)
        {
            hour = Hour;
            minute = Minute;
        }

        public void Deconstruct(out int hour, out int minute, out int second)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
        }

        public int CompareTo(TimeOnly value) => _ticks.CompareTo(value.Ticks);

        public int CompareTo(object value)
        {
            if (value == null) return 1;
            if (!(value is TimeOnly other)) throw new ArgumentException("Object must be of type TimeOnly.");
            return CompareTo(other);
        }

        public bool Equals(TimeOnly value) => _ticks == value.Ticks;
        public override bool Equals(object value) => value is TimeOnly other && _ticks == other.Ticks;
        public override int GetHashCode() => (int)_ticks ^ (int)(_ticks >> 32);

        public string ToLongTimeString() => ToString("T");
        public string ToShortTimeString() => ToString();
        public override string ToString() => ToString("t", null);
        public string ToString(IFormatProvider provider) => ToString("t", provider);
        public string ToString(string format) => ToString(format, null);
        // The culture's patterns and names (Globalization/DateOnlyTimeOnlyCulture.cs, .NET's TimeOnly.cs).
        public string ToString(string format, IFormatProvider provider) => Globalization.DateOnlyTimeOnlyCulture.FormatTime(_ticks, format, provider);
        // TryFormat: ToString's text ("t" for an empty format), copied when it fits.
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(ToString(format.Length == 0 ? "t" : new string(format), provider), destination, out charsWritten);
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(ToString(format.Length == 0 ? "t" : new string(format), provider), utf8Destination, out bytesWritten);

        // Parse / TryParse / ParseExact / TryParseExact: the framework's own, from its IL (src/Csharpc/upstream-types/System.Globalization.txt).
    }
}
