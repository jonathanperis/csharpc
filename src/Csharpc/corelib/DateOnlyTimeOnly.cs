// System.DateOnly (a day number) and System.TimeOnly (ticks since midnight), as in dotnet/runtime;
// formatting and parsing go through the DateTime implementations (DateTimeOps, DateTimeParse).
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.DateOnly")]
    public readonly struct DateOnlyImpl : IComparable, IComparable<DateOnly>, IEquatable<DateOnly>, IFormattable
    {
        private const long TicksPerDay = 864000000000L;
        private const int MaxDayNumber = 3652058;  // 9999-12-31
        private readonly int _dayNumber;

        private DateOnlyImpl(int dayNumber, bool _) { _dayNumber = dayNumber; }

        public DateOnlyImpl(int year, int month, int day)
        {
            _dayNumber = (int)(DateTimeOps.DateToTicks(year, month, day) / TicksPerDay);
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
        public override string ToString() => ToString("d");
        public string ToString(IFormatProvider provider) => ToString("d");
        public string ToString(string format, IFormatProvider provider) => ToString(format);

        public string ToString(string format)
        {
            if (format == null || format.Length == 0) format = "d";
            if (format.Length == 1)
            {
                switch (format[0])
                {
                    case 'o': case 'O': return DateTimeOps.Format(_dayNumber * TicksPerDay, "yyyy'-'MM'-'dd");
                    case 'r': case 'R': return DateTimeOps.Format(_dayNumber * TicksPerDay, "ddd, dd MMM yyyy");
                    case 'd': case 'D': case 'm': case 'M': case 'y': case 'Y': return DateTimeOps.Format(_dayNumber * TicksPerDay, format);
                    default: throw new NotSupportedException("DateOnly.ToString(\"" + format + "\") is not supported by csharpc yet.");
                }
            }
            if (TimeOnlyImpl.HasSpecifier(format, "hHmsfFtzK"))
                throw new NotSupportedException("DateOnly.ToString(\"" + format + "\") with time specifiers is not supported by csharpc yet.");
            return DateTimeOps.Format(_dayNumber * TicksPerDay, format);
        }

        // ---- parsing -----------------------------------------------------------------------------
        private static DateOnly Parsed(string s, string[] formats, DateTimeStyles style)
        {
            DateTimeParse.ParseOnly(s, formats, style, true, true, out long day);
            return Make(day);
        }

        private static bool TryParsed(string s, string[] formats, DateTimeStyles style, out DateOnly result)
        {
            bool ok = DateTimeParse.ParseOnly(s, formats, style, true, false, out long day);
            result = ok ? Make(day) : default;
            return ok;
        }

        public static DateOnly Parse(string s) => Parsed(s, null, DateTimeStyles.None);
        public static DateOnly Parse(string s, IFormatProvider provider) => Parsed(s, null, DateTimeStyles.None);
        public static DateOnly Parse(string s, IFormatProvider provider, DateTimeStyles style) => Parsed(s, null, style);
        public static DateOnly ParseExact(string s, string format) => Parsed(s, new string[] { Required(format) }, DateTimeStyles.None);
        public static DateOnly ParseExact(string s, string format, IFormatProvider provider) => Parsed(s, new string[] { Required(format) }, DateTimeStyles.None);
        public static DateOnly ParseExact(string s, string format, IFormatProvider provider, DateTimeStyles style) => Parsed(s, new string[] { Required(format) }, style);
        public static DateOnly ParseExact(string s, string[] formats) => Parsed(s, Required(formats), DateTimeStyles.None);
        public static DateOnly ParseExact(string s, string[] formats, IFormatProvider provider, DateTimeStyles style) => Parsed(s, Required(formats), style);
        public static bool TryParse(string s, out DateOnly result) => TryParsed(s, null, DateTimeStyles.None, out result);
        public static bool TryParse(string s, IFormatProvider provider, out DateOnly result) => TryParsed(s, null, DateTimeStyles.None, out result);
        public static bool TryParse(string s, IFormatProvider provider, DateTimeStyles style, out DateOnly result) => TryParsed(s, null, style, out result);
        public static bool TryParseExact(string s, string format, out DateOnly result) => TryParsed(s, new string[] { format }, DateTimeStyles.None, out result);
        public static bool TryParseExact(string s, string format, IFormatProvider provider, DateTimeStyles style, out DateOnly result) =>
            TryParsed(s, new string[] { format }, style, out result);
        public static bool TryParseExact(string s, string[] formats, out DateOnly result) => TryParsed(s, formats ?? new string[0], DateTimeStyles.None, out result);
        public static bool TryParseExact(string s, string[] formats, IFormatProvider provider, DateTimeStyles style, out DateOnly result) =>
            TryParsed(s, formats ?? new string[0], style, out result);

        internal static string Required(string format) => format ?? throw new ArgumentNullException("format");
        internal static string[] Required(string[] formats) => formats ?? throw new ArgumentNullException("formats");
    }

    [Shadows("System.TimeOnly")]
    public readonly struct TimeOnlyImpl : IComparable, IComparable<TimeOnly>, IEquatable<TimeOnly>, IFormattable
    {
        private const long TicksPerDay = 864000000000L, TicksPerHour = 36000000000L, TicksPerMinute = 600000000L;
        private const long TicksPerSecond = 10000000L, TicksPerMs = 10000L;
        private readonly long _ticks;

        private TimeOnlyImpl(long ticks, bool _) { _ticks = ticks; }

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
        public override string ToString() => ToString("t");
        public string ToString(IFormatProvider provider) => ToString("t");
        public string ToString(string format, IFormatProvider provider) => ToString(format);

        public string ToString(string format)
        {
            if (format == null || format.Length == 0) format = "t";
            if (format.Length == 1)
            {
                switch (format[0])
                {
                    case 'o': case 'O': return DateTimeOps.Format(_ticks, "HH':'mm':'ss'.'fffffff");
                    case 'r': case 'R': return DateTimeOps.Format(_ticks, "HH':'mm':'ss");
                    case 't': case 'T': return DateTimeOps.Format(_ticks, format);
                    default: throw new NotSupportedException("TimeOnly.ToString(\"" + format + "\") is not supported by csharpc yet.");
                }
            }
            if (HasSpecifier(format, "yMdgzK"))
                throw new NotSupportedException("TimeOnly.ToString(\"" + format + "\") with date specifiers is not supported by csharpc yet.");
            return DateTimeOps.Format(_ticks, format);
        }

        /// <summary>Does a custom format use one of <paramref name="letters"/> outside quotes and escapes?</summary>
        internal static bool HasSpecifier(string format, string letters)
        {
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c == '\\') { i++; continue; }
                if (c == '\'' || c == '"')
                {
                    int end = format.IndexOf(c, i + 1);
                    if (end < 0) return false;
                    i = end;
                    continue;
                }
                if (letters.IndexOf(c) >= 0) return true;
            }
            return false;
        }

        // ---- parsing -----------------------------------------------------------------------------
        private static TimeOnly Parsed(string s, string[] formats, DateTimeStyles style)
        {
            DateTimeParse.ParseOnly(s, formats, style, false, true, out long ticks);
            return Make(ticks);
        }

        private static bool TryParsed(string s, string[] formats, DateTimeStyles style, out TimeOnly result)
        {
            bool ok = DateTimeParse.ParseOnly(s, formats, style, false, false, out long ticks);
            result = ok ? Make(ticks) : default;
            return ok;
        }

        public static TimeOnly Parse(string s) => Parsed(s, null, DateTimeStyles.None);
        public static TimeOnly Parse(string s, IFormatProvider provider) => Parsed(s, null, DateTimeStyles.None);
        public static TimeOnly Parse(string s, IFormatProvider provider, DateTimeStyles style) => Parsed(s, null, style);
        public static TimeOnly ParseExact(string s, string format) => Parsed(s, new string[] { DateOnlyImpl.Required(format) }, DateTimeStyles.None);
        public static TimeOnly ParseExact(string s, string format, IFormatProvider provider) => Parsed(s, new string[] { DateOnlyImpl.Required(format) }, DateTimeStyles.None);
        public static TimeOnly ParseExact(string s, string format, IFormatProvider provider, DateTimeStyles style) =>
            Parsed(s, new string[] { DateOnlyImpl.Required(format) }, style);
        public static TimeOnly ParseExact(string s, string[] formats) => Parsed(s, DateOnlyImpl.Required(formats), DateTimeStyles.None);
        public static TimeOnly ParseExact(string s, string[] formats, IFormatProvider provider, DateTimeStyles style) => Parsed(s, DateOnlyImpl.Required(formats), style);
        public static bool TryParse(string s, out TimeOnly result) => TryParsed(s, null, DateTimeStyles.None, out result);
        public static bool TryParse(string s, IFormatProvider provider, out TimeOnly result) => TryParsed(s, null, DateTimeStyles.None, out result);
        public static bool TryParse(string s, IFormatProvider provider, DateTimeStyles style, out TimeOnly result) => TryParsed(s, null, style, out result);
        public static bool TryParseExact(string s, string format, out TimeOnly result) => TryParsed(s, new string[] { format }, DateTimeStyles.None, out result);
        public static bool TryParseExact(string s, string format, IFormatProvider provider, DateTimeStyles style, out TimeOnly result) =>
            TryParsed(s, new string[] { format }, style, out result);
        public static bool TryParseExact(string s, string[] formats, out TimeOnly result) => TryParsed(s, formats ?? new string[0], DateTimeStyles.None, out result);
        public static bool TryParseExact(string s, string[] formats, IFormatProvider provider, DateTimeStyles style, out TimeOnly result) =>
            TryParsed(s, formats ?? new string[0], style, out result);
    }
}
