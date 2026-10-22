// System.TimeSpan and System.DateTime logic in C# (formerly src/runtime/datetime.c; ADR 0008). Both
// types stay represented by their tick count (long, 100 ns units); the codegen (BclValueTypes.cs)
// still asks for the csrt_timespan_* / csrt_datetime_* entry points, which CoreLibRuntime redirects
// here. DateTime's Kind is not modelled. Calendar math is the CLR's own algorithm (proleptic
// Gregorian, years 1..9999); formatting follows the invariant culture. The clocks (UtcNow, Now,
// Stopwatch) stay in C.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class DateTimeOps
    {
        private const long TicksPerMs = 10000L;
        private const long TicksPerSecond = 10000000L;
        private const long TicksPerMinute = 600000000L;
        private const long TicksPerHour = 36000000000L;
        private const long TicksPerDay = 864000000000L;
        private const long MaxTicks = 3155378975999999999L;  // DateTime.MaxValue

        private static void TimeSpanOverflow() => throw new OverflowException("TimeSpan overflowed because the duration is too long.");

        private static void DateTimeRange() =>
            throw new ArgumentOutOfRangeException("value", "The added or subtracted value results in an un-representable DateTime.");

        // ==========================================================================================
        // TimeSpan
        // ==========================================================================================

        internal static long TimeSpanAdd(long a, long b)
        {
            long r = a + b;
            if (((a ^ r) & (b ^ r)) < 0) TimeSpanOverflow();
            return r;
        }

        internal static long TimeSpanSubtract(long a, long b)
        {
            long r = a - b;
            if (((a ^ b) & (a ^ r)) < 0) TimeSpanOverflow();
            return r;
        }

        internal static long TimeSpanNegate(long a)
        {
            if (a == long.MinValue) throw new OverflowException("Negating the minimum value of a twos complement number is invalid.");
            return -a;
        }

        internal static long TimeSpanDuration(long a)
        {
            if (a == long.MinValue)
                throw new OverflowException("The duration cannot be returned for TimeSpan.MinValue because the absolute value of TimeSpan.MinValue exceeds the value of TimeSpan.MaxValue.");
            return a < 0 ? -a : a;
        }

        private static long FromDoubleTicks(double ticks)
        {
            if (ticks > 9223372036854775807.0 || ticks < -9223372036854775808.0 || ticks != ticks) TimeSpanOverflow();
            if (ticks >= 9223372036854775807.0) return long.MaxValue;
            return (long)ticks;
        }

        /// <summary>TimeSpan.FromDays/Hours/... (double): exact ticks, truncated (.NET 7+ semantics).</summary>
        internal static long TimeSpanFromDouble(double value, long ticksPerUnit)
        {
            if (value != value) throw new ArgumentException("TimeSpan does not accept floating point Not-a-Number values.");
            return FromDoubleTicks(value * ticksPerUnit);
        }

        /// <summary>TimeSpan.FromX integer overloads / constructors: sum of unit counts, checked.</summary>
        internal static long TimeSpanFromUnits(long days, long hours, long minutes, long seconds, long ms, long us)
        {
            Int128 t = (Int128)days * TicksPerDay + (Int128)hours * TicksPerHour + (Int128)minutes * TicksPerMinute +
                       (Int128)seconds * TicksPerSecond + (Int128)ms * TicksPerMs + (Int128)us * 10;
            if (t > long.MaxValue || t < long.MinValue)
                throw new ArgumentOutOfRangeException(null, "TimeSpan overflowed because the duration is too long.");
            return (long)t;
        }

        internal static long TimeSpanMultiply(long ticks, double factor)
        {
            if (factor != factor) throw new ArgumentException("TimeSpan does not accept floating point Not-a-Number values.", "factor");
            return FromDoubleTicks(Math.Round(ticks * factor));
        }

        internal static long TimeSpanDivide(long ticks, double divisor)
        {
            if (divisor != divisor) throw new ArgumentException("TimeSpan does not accept floating point Not-a-Number values.", "divisor");
            return FromDoubleTicks(Math.Round(ticks / divisor));
        }

        private static void AppendNumber(ref CharBuilder sb, long value, int width)
        {
            int digits = Number.CountDigits((ulong)value);
            sb.Append('0', width - digits);
            Span<char> tmp = stackalloc char[20];
            Number.WriteDigits(tmp.Slice(0, digits), (ulong)value);
            for (int i = 0; i < digits; i++) sb.Append(tmp[i]);
        }

        private static void AppendFraction(ref CharBuilder sb, long fraction, int digits, bool trim)
        {
            Span<char> t = stackalloc char[7];
            long v = fraction;  // < 10^7
            for (int i = 6; i >= 0; i--) { t[i] = (char)('0' + (int)(v % 10)); v /= 10; }
            int n = digits;
            if (trim) while (n > 0 && t[n - 1] == '0') n--;
            for (int i = 0; i < n; i++) sb.Append(t[i]);
        }

        private static void TimeSpanStandard(ref CharBuilder sb, long ticks, char f)
        {
            bool negative = ticks < 0;
            ulong magnitude = negative ? 0UL - (ulong)ticks : (ulong)ticks;  // survives MinValue
            ulong days = magnitude / TicksPerDay, rem = magnitude % TicksPerDay;
            int hours = (int)(rem / TicksPerHour), minutes = (int)(rem / TicksPerMinute % 60), seconds = (int)(rem / TicksPerSecond % 60);
            long fraction = (long)(rem % TicksPerSecond);
            if (negative) sb.Append('-');
            if (f == 'g')
            {
                if (days != 0) { AppendNumber(ref sb, (long)days, 1); sb.Append(':'); }
                AppendNumber(ref sb, hours, 1); sb.Append(':'); AppendNumber(ref sb, minutes, 2); sb.Append(':'); AppendNumber(ref sb, seconds, 2);
                if (fraction != 0) { sb.Append('.'); AppendFraction(ref sb, fraction, 7, true); }
            }
            else if (f == 'G')
            {
                AppendNumber(ref sb, (long)days, 1); sb.Append(':'); AppendNumber(ref sb, hours, 2); sb.Append(':');
                AppendNumber(ref sb, minutes, 2); sb.Append(':'); AppendNumber(ref sb, seconds, 2); sb.Append('.');
                AppendFraction(ref sb, fraction, 7, false);
            }
            else  // 'c', the constant format
            {
                if (days != 0) { AppendNumber(ref sb, (long)days, 1); sb.Append('.'); }
                AppendNumber(ref sb, hours, 2); sb.Append(':'); AppendNumber(ref sb, minutes, 2); sb.Append(':'); AppendNumber(ref sb, seconds, 2);
                if (fraction != 0) { sb.Append('.'); AppendFraction(ref sb, fraction, 7, false); }
            }
        }

        [RuntimeExport("csrt_timespan_to_string", "csrt_Boxed_TimeSpan_MT")]
        [RuntimeExport("csrt_timespan_to_string", "csrt_Stopwatch_MT")]
        internal static string TimeSpanToString(long ticks)
        {
            Span<char> chars = stackalloc char[32];
            var sb = new CharBuilder(chars);
            TimeSpanStandard(ref sb, ticks, 'c');
            return sb.Build();
        }

        private static int RunLength(string f, int i)
        {
            int n = 1;
            while (i + n < f.Length && f[i + n] == f[i]) n++;
            return n;
        }

        private static void ThrowInvalidFormat() => throw new FormatException("Input string was not in a correct format.");

        /// <summary>
        /// .NET's DateTimeFormat.ParseQuoteString: copies a quoted literal (backslash escapes inside it)
        /// and returns its length including both quotes; an unterminated quote is an error.
        /// </summary>
        private static int ParseQuoteString(ref CharBuilder sb, string f, int pos)
        {
            int begin = pos;
            char quote = f[pos++];
            while (pos < f.Length)
            {
                char ch = f[pos++];
                if (ch == quote) return pos - begin;
                if (ch == '\\')
                {
                    if (pos >= f.Length) ThrowInvalidFormat();
                    sb.Append(f[pos++]);
                }
                else sb.Append(ch);
            }
            throw new FormatException("Cannot find a matching quote character for the character '" + quote.ToString() + "'.");
        }

        /// <summary>The character after a '%' or '\\'; a format ending there is an error.</summary>
        private static char NextFormatChar(string f, int i)
        {
            if (i + 1 >= f.Length) ThrowInvalidFormat();
            return f[i + 1];
        }

        internal static string TimeSpanFormat(long ticks, string format)
        {
            Span<char> chars = stackalloc char[64];
            var sb = new CharBuilder(chars);
            string f = format ?? "";
            if (f.Length == 0 || (f.Length == 1 && (f[0] == 'c' || f[0] == 't' || f[0] == 'T' || f[0] == 'g' || f[0] == 'G')))
            {
                TimeSpanStandard(ref sb, ticks, f.Length == 0 ? 'c' : f[0] == 'g' ? 'g' : f[0] == 'G' ? 'G' : 'c');
                return sb.Build();
            }
            // Any other single character is an unknown standard format, not a custom one.
            if (f.Length == 1) ThrowInvalidFormat();
            TimeSpanCustom(ref sb, ticks, f);
            return sb.Build();
        }

        /// <summary>.NET's TimeSpanFormat.FormatCustomized: anything but a known specifier, a quote or an escape is an error.</summary>
        private static void TimeSpanCustom(ref CharBuilder sb, long ticks, string f)
        {
            ulong magnitude = ticks < 0 ? 0UL - (ulong)ticks : (ulong)ticks;
            ulong days = magnitude / TicksPerDay, rem = magnitude % TicksPerDay;
            int hours = (int)(rem / TicksPerHour), minutes = (int)(rem / TicksPerMinute % 60), seconds = (int)(rem / TicksPerSecond % 60);
            long fraction = (long)(rem % TicksPerSecond);
            for (int i = 0; i < f.Length;)
            {
                char c = f[i];
                int n;
                switch (c)
                {
                    case 'h':
                    case 'm':
                    case 's':
                        n = RunLength(f, i);
                        if (n > 2) ThrowInvalidFormat();
                        AppendNumber(ref sb, c == 'h' ? hours : c == 'm' ? minutes : seconds, n);
                        break;
                    case 'f':
                    case 'F':
                        n = RunLength(f, i);
                        if (n > 7) ThrowInvalidFormat();
                        AppendFraction(ref sb, fraction, n, c == 'F');
                        break;
                    case 'd':
                        n = RunLength(f, i);
                        if (n > 8) ThrowInvalidFormat();
                        AppendNumber(ref sb, (long)days, n);
                        break;
                    case '\'':
                    case '"':
                        n = ParseQuoteString(ref sb, f, i);
                        break;
                    case '%':
                    {
                        char next = NextFormatChar(f, i);
                        if (next == '%') ThrowInvalidFormat();
                        TimeSpanCustom(ref sb, ticks, next.ToString());
                        n = 2;
                        break;
                    }
                    case '\\':
                        sb.Append(NextFormatChar(f, i));
                        n = 2;
                        break;
                    default:
                        ThrowInvalidFormat();
                        n = 0;
                        break;
                }
                i += n;
            }
        }

        // ==========================================================================================
        // DateTime (the CLR's calendar algorithms)
        // ==========================================================================================

        private static int DaysToMonth(bool leap, int month) => leap
            ? month switch { 0 => 0, 1 => 31, 2 => 60, 3 => 91, 4 => 121, 5 => 152, 6 => 182, 7 => 213, 8 => 244, 9 => 274, 10 => 305, 11 => 335, _ => 366 }
            : month switch { 0 => 0, 1 => 31, 2 => 59, 3 => 90, 4 => 120, 5 => 151, 6 => 181, 7 => 212, 8 => 243, 9 => 273, 10 => 304, 11 => 334, _ => 365 };

        private static string MonthName(int month) => month switch
        {
            1 => "January", 2 => "February", 3 => "March", 4 => "April", 5 => "May", 6 => "June", 7 => "July",
            8 => "August", 9 => "September", 10 => "October", 11 => "November", _ => "December",
        };

        private static string DayName(int dayOfWeek) => dayOfWeek switch
        {
            0 => "Sunday", 1 => "Monday", 2 => "Tuesday", 3 => "Wednesday", 4 => "Thursday", 5 => "Friday", _ => "Saturday",
        };

        private static bool IsLeap(int year) => (year & 3) == 0 && ((year % 100) != 0 || (year % 400) == 0);

        internal static bool IsLeapYear(int year)
        {
            if (year < 1 || year > 9999) throw new ArgumentOutOfRangeException("year", "Year must be between 1 and 9999.");
            return IsLeap(year);
        }

        internal static int DaysInMonth(int year, int month)
        {
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException("month", "Month must be between one and twelve.");
            bool leap = IsLeapYear(year);
            return DaysToMonth(leap, month) - DaysToMonth(leap, month - 1);
        }

        private static long DateToTicks(int y, int m, int d)
        {
            if (y < 1 || y > 9999 || m < 1 || m > 12)
                throw new ArgumentOutOfRangeException(null, "Year, Month, and Day parameters describe an un-representable DateTime.");
            bool leap = IsLeap(y);
            if (d < 1 || d > DaysToMonth(leap, m) - DaysToMonth(leap, m - 1))
                throw new ArgumentOutOfRangeException(null, "Year, Month, and Day parameters describe an un-representable DateTime.");
            long y1 = y - 1;
            long n = y1 * 365 + y1 / 4 - y1 / 100 + y1 / 400 + DaysToMonth(leap, m - 1) + d - 1;
            return n * TicksPerDay;
        }

        internal static long New(int y, int mo, int d, int h, int mi, int s, int ms)
        {
            long ticks = DateToTicks(y, mo, d);
            if (h < 0 || h >= 24 || mi < 0 || mi >= 60 || s < 0 || s >= 60)
                throw new ArgumentOutOfRangeException(null, "Hour, Minute, and Second parameters describe an un-representable DateTime.");
            if (ms < 0 || ms >= 1000)
                throw new ArgumentOutOfRangeException("millisecond", "Valid values are between 0 and 999, inclusive.");
            return ticks + h * TicksPerHour + mi * TicksPerMinute + s * TicksPerSecond + ms * TicksPerMs;
        }

        internal static long FromTicks(long ticks)
        {
            if (ticks < 0 || ticks > MaxTicks)
                throw new ArgumentOutOfRangeException("ticks", "Ticks must be between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks.");
            return ticks;
        }

        private static void DateParts(long ticks, out int year, out int month, out int day, out int dayOfYear)
        {
            int n = (int)(ticks / TicksPerDay);
            int y400 = n / 146097; n -= y400 * 146097;
            int y100 = n / 36524; if (y100 == 4) y100 = 3; n -= y100 * 36524;
            int y4 = n / 1461; n -= y4 * 1461;
            int y1 = n / 365; if (y1 == 4) y1 = 3;
            year = y400 * 400 + y100 * 100 + y4 * 4 + y1 + 1;
            n -= y1 * 365;
            dayOfYear = n + 1;
            bool leap = y1 == 3 && (y4 != 24 || y100 == 3);
            int m = (n >> 5) + 1;
            while (n >= DaysToMonth(leap, m)) m++;
            month = m;
            day = n - DaysToMonth(leap, m - 1) + 1;
        }

        /// <summary>part: 0 year, 1 month, 2 day, 3 day-of-year, 4 day-of-week, 5 hour, 6 minute, 7 second,
        /// 8 millisecond, 9 microsecond, 10 nanosecond.</summary>
        internal static int Part(long ticks, int part)
        {
            switch (part)
            {
                case 0: case 1: case 2: case 3:
                    DateParts(ticks, out int y, out int m, out int d, out int doy);
                    return part == 0 ? y : part == 1 ? m : part == 2 ? d : doy;
                case 4: return (int)((ticks / TicksPerDay + 1) % 7);
                case 5: return (int)(ticks / TicksPerHour % 24);
                case 6: return (int)(ticks / TicksPerMinute % 60);
                case 7: return (int)(ticks / TicksPerSecond % 60);
                case 8: return (int)(ticks / TicksPerMs % 1000);
                case 9: return (int)(ticks / 10 % 1000);
                case 10: return (int)(ticks % 10 * 100);
            }
            return 0;
        }

        internal static long AddTicks(long ticks, long value)
        {
            long r = ticks + value;
            if (((ticks ^ r) & (value ^ r)) < 0 || r < 0 || r > MaxTicks) DateTimeRange();
            return r;
        }

        /// <summary>AddDays/AddHours/...(double): integral and fractional parts separately (.NET 7+).</summary>
        internal static long AddUnits(long ticks, double value, long ticksPerUnit)
        {
            double maxUnits = (double)MaxTicks / ticksPerUnit;
            if (value != value || Math.Abs(value) > maxUnits)
                throw new ArgumentOutOfRangeException("value", "Value to add was out of range.");
            double integral = Math.Truncate(value);
            double fractional = value - integral;
            long delta = (long)integral * ticksPerUnit + (long)(fractional * ticksPerUnit);
            return AddTicks(ticks, delta);
        }

        internal static long AddMonths(long ticks, int months)
        {
            if (months < -120000 || months > 120000)
                throw new ArgumentOutOfRangeException("months", "Months value must be between +/-120000.");
            DateParts(ticks, out int y, out int m, out int d, out int _);
            int i = m - 1 + months;
            if (i >= 0) { m = i % 12 + 1; y += i / 12; }
            else { m = 12 + (i + 1) % 12; y += (i - 11) / 12; }
            if (y < 1 || y > 9999) DateTimeRange();
            int dim = DaysInMonth(y, m);
            if (d > dim) d = dim;
            return DateToTicks(y, m, d) + ticks % TicksPerDay;
        }

        internal static long AddYears(long ticks, int years)
        {
            if (years < -10000 || years > 10000)
                throw new ArgumentOutOfRangeException("years", "Years value must be between +/-10000.");
            return AddMonths(ticks, years * 12);
        }

        /// <summary>.NET's DateTimeFormat.FormatCustomized for the invariant culture and the Gregorian calendar.</summary>
        private static void FormatCustom(ref CharBuilder sb, long ticks, string f)
        {
            DateParts(ticks, out int y, out int mo, out int d, out int _);
            int h = (int)(ticks / TicksPerHour % 24), mi = (int)(ticks / TicksPerMinute % 60), s = (int)(ticks / TicksPerSecond % 60);
            long fraction = ticks % TicksPerSecond;
            int dow = (int)((ticks / TicksPerDay + 1) % 7);
            bool timeOnly = true;  // no date specifier yet: a bare time of day gets today's offset for 'z'
            for (int i = 0; i < f.Length;)
            {
                char c = f[i];
                int n = RunLength(f, i);
                switch (c)
                {
                    case 'g': sb.Append("A.D."); break;  // the invariant culture's only era
                    case 'y':
                        AppendNumber(ref sb, n <= 2 ? y % 100 : y, n);
                        timeOnly = false;
                        break;
                    case 'M':
                        if (n <= 2) AppendNumber(ref sb, mo, n);
                        else if (n == 3) sb.Append(MonthName(mo).Substring(0, 3));
                        else sb.Append(MonthName(mo));
                        timeOnly = false;
                        break;
                    case 'd':
                        if (n <= 2) AppendNumber(ref sb, d, n);
                        else if (n == 3) sb.Append(DayName(dow).Substring(0, 3));
                        else sb.Append(DayName(dow));
                        timeOnly = false;
                        break;
                    case 'H': AppendNumber(ref sb, h, n > 1 ? 2 : 1); break;
                    case 'h': AppendNumber(ref sb, h % 12 == 0 ? 12 : h % 12, n > 1 ? 2 : 1); break;
                    case 'm': AppendNumber(ref sb, mi, n > 1 ? 2 : 1); break;
                    case 's': AppendNumber(ref sb, s, n > 1 ? 2 : 1); break;
                    case 'f':
                    case 'F':
                    {
                        if (n > 7) ThrowInvalidFormat();
                        int before = sb.Length;
                        AppendFraction(ref sb, fraction, n, c == 'F');
                        if (c == 'F' && sb.Length == before && before > 0 && sb.LastChar == '.') sb.Truncate(before - 1);  // drop a dangling '.'
                        break;
                    }
                    case 't': if (n == 1) sb.Append(h < 12 ? 'A' : 'P'); else sb.Append(h < 12 ? "AM" : "PM"); break;
                    case 'z':
                        AppendUtcOffset(ref sb, timeOnly && ticks < TicksPerDay ? LocalOffsetNow() : LocalUtcOffset(ticks), n);
                        break;
                    case 'K': n = 1; break;  // Kind is not modelled: an unspecified DateTime writes nothing
                    case '\'':
                    case '"':
                        n = ParseQuoteString(ref sb, f, i);
                        break;
                    case '%':
                    {
                        char next = NextFormatChar(f, i);
                        if (next == '%') ThrowInvalidFormat();
                        FormatCustom(ref sb, ticks, next.ToString());
                        n = 2;
                        break;
                    }
                    case '\\':
                        sb.Append(NextFormatChar(f, i));
                        n = 2;
                        break;
                    default: sb.Append(c); n = 1; break;
                }
                i += n;
            }
        }

        // ------------------------------------------------------------------------------------------
        // 'z': the local time zone's UTC offset. DateTime's Kind is not modelled, so every DateTime is
        // treated like .NET treats an unspecified one: as a wall-clock time of the local zone (TZ / tzdata
        // through libc, as .NET reads them).
        // ------------------------------------------------------------------------------------------

        private const long UnixEpochTicks = 621355968000000000L;

        /// <summary>UTC offset (seconds) and DST flag of the local zone at a UTC instant.</summary>
        private static unsafe long LocalOffsetAt(long unixSeconds, out bool dst)
        {
            // struct tm (x86-64 glibc): tm_isdst @32 (int), tm_gmtoff @40 (long); 56 bytes.
            byte* tm = stackalloc byte[64];
            long t = unixSeconds;
            if (Posix.localtime_r(&t, tm) == null) { dst = false; return 0; }
            dst = *(int*)(tm + 32) > 0;
            return *(long*)(tm + 40);
        }

        private static unsafe long UnixNow() => Posix.time(null);

        private static long LocalOffsetNow() => LocalOffsetAt(UnixNow(), out bool _);

        /// <summary>
        /// The offset of a local wall-clock time, like TimeZoneInfo.Local.GetUtcOffset: an ambiguous time
        /// (clocks turned back) gets the standard offset, a skipped one (clocks turned forward) the offset
        /// in force before the transition.
        /// </summary>
        private static long LocalUtcOffset(long ticks)
        {
            long diff = ticks - UnixEpochTicks;
            long local = diff / TicksPerSecond;
            if (diff % TicksPerSecond < 0) local--;
            // Zone offsets are below a day, so these two instants lie on either side of any transition
            // that could make this wall-clock time ambiguous or skipped.
            long before = LocalOffsetAt(local - 172800, out bool dstBefore);
            long after = LocalOffsetAt(local + 172800, out bool dstAfter);
            bool validBefore = LocalOffsetAt(local - before, out bool _) == before;
            bool validAfter = LocalOffsetAt(local - after, out bool _) == after;
            long offset = validBefore && validAfter ? (dstBefore && !dstAfter ? after : before) : validAfter ? after : before;
            if (offset % 60 != 0)
            {
                // A local mean time with seconds: .NET's adjustment rules hold the difference to the zone's
                // base (current standard) offset truncated to whole minutes (New York's -4:56:02 is -4:57).
                long now = UnixNow();
                long baseOffset = LocalOffsetAt(now, out bool dst);
                for (int month = 1; dst && month < 12; month++) baseOffset = LocalOffsetAt(now + month * 30 * 86400L, out dst);
                long delta = offset - baseOffset;
                offset = baseOffset + (delta - delta % 60);
            }
            return offset;
        }

        private static void AppendUtcOffset(ref CharBuilder sb, long offsetSeconds, int n)
        {
            if (offsetSeconds >= 0) sb.Append('+');
            else { sb.Append('-'); offsetSeconds = -offsetSeconds; }
            AppendNumber(ref sb, offsetSeconds / 3600, n <= 1 ? 1 : 2);
            if (n >= 3)
            {
                sb.Append(':');
                AppendNumber(ref sb, offsetSeconds / 60 % 60, 2);
            }
        }

        private static string StandardPattern(char f) => f switch
        {
            'd' => "MM/dd/yyyy",
            'D' => "dddd, dd MMMM yyyy",
            'f' => "dddd, dd MMMM yyyy HH:mm",
            'F' => "dddd, dd MMMM yyyy HH:mm:ss",
            'g' => "MM/dd/yyyy HH:mm",
            'G' => "MM/dd/yyyy HH:mm:ss",
            'M' or 'm' => "MMMM dd",
            'Y' or 'y' => "yyyy MMMM",
            't' => "HH:mm",
            'T' => "HH:mm:ss",
            's' => "yyyy'-'MM'-'dd'T'HH':'mm':'ss",
            'u' => "yyyy'-'MM'-'dd HH':'mm':'ss'Z'",
            'o' or 'O' => "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffff",
            'R' or 'r' => "ddd, dd MMM yyyy HH':'mm':'ss 'GMT'",
            _ => null,
        };

        [RuntimeExport("csrt_datetime_to_string", "csrt_Boxed_DateTime_MT")]
        internal static string DateTimeToString(long ticks) => Format(ticks, null);

        internal static string Format(long ticks, string format)
        {
            Span<char> chars = stackalloc char[64];
            var sb = new CharBuilder(chars);
            if (format == null || format.Length == 0) FormatCustom(ref sb, ticks, "MM/dd/yyyy HH:mm:ss");
            else if (format.Length == 1)
            {
                string pattern = StandardPattern(format[0]);
                if (pattern == null) ThrowInvalidFormat();
                FormatCustom(ref sb, ticks, pattern);
            }
            else FormatCustom(ref sb, ticks, format);
            return sb.Build();
        }
    }
}
