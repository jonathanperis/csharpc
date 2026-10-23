// System.TimeSpan and System.DateTime logic in C# (formerly src/runtime/datetime.c; ADR 0008).
// TimeSpan is its tick count (long, 100 ns units). DateTime is .NET's `_dateData`: the ticks in the
// low 62 bits and the Kind in the top two (00 Unspecified, 01 Utc, 10 Local, 11 Local in the
// daylight-saving occurrence of an ambiguous hour), so every entry point here that takes a DateTime
// takes that whole word. The codegen (BclValueTypes.cs) still asks for the csrt_timespan_* /
// csrt_datetime_* entry points, which CoreLibRuntime redirects here. Calendar math is the CLR's own
// algorithm (proleptic Gregorian, years 1..9999); formatting follows the invariant culture. The
// clocks (UtcNow, Stopwatch) stay in C; the local time zone is libc's (TZ / tzdata, as .NET reads).
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

        // DateTime._dateData layout (dotnet/runtime DateTime.cs).
        private const ulong TicksMask = 0x3FFFFFFFFFFFFFFF;
        private const ulong FlagsMask = 0xC000000000000000;
        private const long KindUtc = 0x4000000000000000;
        private const long KindLocal = unchecked((long)0x8000000000000000);
        private const long KindLocalAmbiguousDst = unchecked((long)0xC000000000000000);
        private const long TicksCeiling = 0x4000000000000000;

        internal static long TicksOf(long data) => (long)((ulong)data & TicksMask);
        private static long FlagsOf(long data) => (long)((ulong)data & FlagsMask);
        private static bool IsUtc(long data) => FlagsOf(data) == KindUtc;
        private static bool IsLocal(long data) => (data & KindLocal) != 0;
        private static bool IsAmbiguousDst(long data) => FlagsOf(data) == KindLocalAmbiguousDst;

        /// <summary>DateTime.Kind: both local states read as Local.</summary>
        internal static int Kind(long data)
        {
            int k = (int)((ulong)data >> 62);
            return k == 3 ? 2 : k;
        }

        private static void TimeSpanOverflow() => throw new OverflowException("TimeSpan overflowed because the duration is too long.");

        /// <summary>.NET's ThrowDateArithmetic: the parameter is "value" for methods, "t" for operators, "months" for AddMonths.</summary>
        private static void DateTimeRange(string param = "value") =>
            throw new ArgumentOutOfRangeException(param, "The added or subtracted value results in an un-representable DateTime.");

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

        private static void TimeSpanStandard(ref CharBuilder sb, long ticks, char f, string decimalSeparator = ".")
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
                if (fraction != 0) { sb.Append(decimalSeparator); AppendFraction(ref sb, fraction, 7, true); }
            }
            else if (f == 'G')
            {
                AppendNumber(ref sb, (long)days, 1); sb.Append(':'); AppendNumber(ref sb, hours, 2); sb.Append(':');
                AppendNumber(ref sb, minutes, 2); sb.Append(':'); AppendNumber(ref sb, seconds, 2); sb.Append(decimalSeparator);
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

        internal static string TimeSpanFormat(long ticks, string format) => TimeSpanFormatWith(ticks, format, null);

        /// <summary>TimeSpan.ToString(format, provider): only "g"/"G" depend on the culture (its decimal separator).</summary>
        internal static string TimeSpanFormatWith(long ticks, string format, IFormatProvider provider)
        {
            Span<char> chars = stackalloc char[64];
            var sb = new CharBuilder(chars);
            string f = format ?? "";
            if (f.Length == 0 || (f.Length == 1 && (f[0] == 'c' || f[0] == 't' || f[0] == 'T' || f[0] == 'g' || f[0] == 'G')))
            {
                char standard = f.Length == 0 ? 'c' : f[0] == 'g' ? 'g' : f[0] == 'G' ? 'G' : 'c';
                TimeSpanStandard(ref sb, ticks, standard,
                    standard == 'c' ? "." : CsharpcCoreLib.Globalization.DateTimeFormatInfo.GetInstance(provider).DecimalSeparator);
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

        internal static int DaysToMonth(bool leap, int month) => leap
            ? month switch { 0 => 0, 1 => 31, 2 => 60, 3 => 91, 4 => 121, 5 => 152, 6 => 182, 7 => 213, 8 => 244, 9 => 274, 10 => 305, 11 => 335, _ => 366 }
            : month switch { 0 => 0, 1 => 31, 2 => 59, 3 => 90, 4 => 120, 5 => 151, 6 => 181, 7 => 212, 8 => 243, 9 => 273, 10 => 304, 11 => 334, _ => 365 };

        internal static bool IsLeap(int year) => (year & 3) == 0 && ((year % 100) != 0 || (year % 400) == 0);

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

        internal static long DateToTicks(int y, int m, int d)
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

        private static long KindBits(int kind)
        {
            if ((uint)kind > 2) throw new ArgumentException("Invalid DateTimeKind value.", "kind");
            return (long)((ulong)(uint)kind << 62);
        }

        /// <summary>new DateTime(ticks, kind): the ticks are checked before the kind, as in .NET.</summary>
        internal static long FromTicksKind(long ticks, int kind) => FromTicks(ticks) | KindBits(kind);

        /// <summary>new DateTime(y, M, d, h, m, s[, ms], kind): the kind is checked first, as in .NET.</summary>
        internal static long NewKind(int y, int mo, int d, int h, int mi, int s, int ms, int kind)
        {
            long bits = KindBits(kind);
            return New(y, mo, d, h, mi, s, ms) | bits;
        }

        internal static long SpecifyKind(long data, int kind) => TicksOf(data) | KindBits(kind);

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
        internal static int Part(long data, int part)
        {
            long ticks = TicksOf(data);
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

        /// <summary>AddTicks / Add(TimeSpan) / Subtract(TimeSpan): the result keeps all kind flags, as in .NET.</summary>
        internal static long AddTicks(long data, long value) => AddTicks(data, value, "value");

        /// <summary>DateTime + TimeSpan / DateTime - TimeSpan.</summary>
        internal static long AddTicksOperator(long data, long value) => AddTicks(data, value, "t");

        private static long AddTicks(long data, long value, string param)
        {
            long ticks = TicksOf(data);
            long r = ticks + value;
            if (((ticks ^ r) & (value ^ r)) < 0 || r < 0 || r > MaxTicks) DateTimeRange(param);
            return r | FlagsOf(data);
        }

        /// <summary>AddDays/AddHours/...(double): integral and fractional parts separately (.NET 7+).</summary>
        internal static long AddUnits(long data, double value, long ticksPerUnit)
        {
            double maxUnits = (double)MaxTicks / ticksPerUnit;
            if (value != value || Math.Abs(value) > maxUnits)
                throw new ArgumentOutOfRangeException("value", "Value to add was out of range.");
            double integral = Math.Truncate(value);
            double fractional = value - integral;
            long delta = (long)integral * ticksPerUnit + (long)(fractional * ticksPerUnit);
            return AddTicks(data, delta);
        }

        internal static long AddMonths(long data, int months)
        {
            long ticks = TicksOf(data);
            if (months < -120000 || months > 120000)
                throw new ArgumentOutOfRangeException("months", "Months value must be between +/-120000.");
            DateParts(ticks, out int y, out int m, out int d, out int _);
            int i = m - 1 + months;
            if (i >= 0) { m = i % 12 + 1; y += i / 12; }
            else { m = 12 + (i + 1) % 12; y += (i - 11) / 12; }
            if (y < 1 || y > 9999) DateTimeRange("months");
            int dim = DaysInMonth(y, m);
            if (d > dim) d = dim;
            return (DateToTicks(y, m, d) + ticks % TicksPerDay) | FlagsOf(data);
        }

        internal static long AddYears(long data, int years)
        {
            if (years < -10000 || years > 10000)
                throw new ArgumentOutOfRangeException("value", "Years value must be between +/-10000.");
            DateParts(TicksOf(data), out int y, out int _, out int _, out int _);
            if (y + years < 1 || y + years > 9999) DateTimeRange();
            return AddMonths(data, years * 12);
        }

        // ------------------------------------------------------------------------------------------
        // The local time zone (TZ / tzdata through libc localtime_r, which .NET also reads), resolved
        // like TimeZoneInfo.Local: GetUtcOffset for wall-clock (Unspecified / Local) times and
        // GetUtcOffsetFromUtc for UTC instants.
        // ------------------------------------------------------------------------------------------

        private const long UnixEpochTicks = 621355968000000000L;
        private const long FileTimeOffset = 504911232000000000L;  // 1601-01-01

        private const long Year10000UnixSeconds = 253402300800L;

        /// <summary>
        /// UTC offset (seconds) and DST flag of the local zone at a UTC instant. Like .NET's adjustment
        /// rules, a daylight-saving period still running at the end of year 9999 (southern hemisphere)
        /// ends at local midnight, when the clocks would go back to standard time.
        /// </summary>
        private static long LocalOffsetAt(long unixSeconds, out bool dst)
        {
            long offset = LibcOffsetAt(unixSeconds, out dst);
            if (dst && unixSeconds + offset >= Year10000UnixSeconds)
            {
                bool earlierDst = true;
                for (int month = 1; earlierDst && month < 12; month++) offset = LibcOffsetAt(unixSeconds - month * 30 * 86400L, out earlierDst);
                dst = false;
            }
            return offset;
        }

        private static unsafe long LibcOffsetAt(long unixSeconds, out bool dst)
        {
            // struct tm (LP64 glibc and Darwin alike): tm_isdst @32 (int), tm_gmtoff @40 (long); 56 bytes.
            byte* tm = stackalloc byte[64];
            long t = unixSeconds;
            if (Posix.localtime_r(&t, tm) == null) { dst = false; return 0; }
            dst = *(int*)(tm + 32) > 0;
            return *(long*)(tm + 40);
        }

        private static unsafe long UnixNow() => Posix.time(null);

        private static long LocalOffsetNow() => LocalOffsetAt(UnixNow(), out bool _);

        private static long UnixSeconds(long ticks)
        {
            long diff = ticks - UnixEpochTicks;
            long seconds = diff / TicksPerSecond;
            if (diff % TicksPerSecond < 0) seconds--;
            return seconds;
        }

        private const long MaxOffset = 14 * 3600;  // TimeZoneInfo's offset range is +/-14 hours

        /// <summary>TimeZoneInfo.Local.BaseUtcOffset: the zone's current standard offset.</summary>
        private static long BaseOffset()
        {
            long now = UnixNow();
            long baseOffset = LocalOffsetAt(now, out bool dst);
            for (int month = 1; dst && month < 12; month++) baseOffset = LocalOffsetAt(now + month * 30 * 86400L, out dst);
            return baseOffset;
        }

        /// <summary>
        /// A tzdata offset as .NET's adjustment rules (TimeZoneInfo.Unix.cs) hold it: the difference to the
        /// base offset, if it has seconds (local mean time), keeps only its hours and minutes components
        /// (New York's -4:56:02 is -4:57, Apia's -11:26:04 against +13 is +12:34), and the total is
        /// clamped to +/-14 hours (Yakutat's +14:41 is +14:00).
        /// </summary>
        private static long RuleOffset(long offset)
        {
            if (offset % 60 == 0 && offset >= -MaxOffset && offset <= MaxOffset) return offset;
            long baseOffset = BaseOffset();
            long delta = offset - baseOffset;
            if (delta % 60 != 0) delta = (delta - delta % 60) % 86400;
            return Math.Clamp(baseOffset + delta, -MaxOffset, MaxOffset);
        }

        /// <summary>
        /// The offset of a local wall-clock time, like TimeZoneInfo.Local.GetUtcOffset: an ambiguous time
        /// (clocks turned back) gets standard time unless it is a Local DateTime flagged as the
        /// daylight-saving occurrence; a skipped one (clocks turned forward, or a whole day skipped
        /// across the date line) gets the zone's base offset. <paramref name="dst"/> is
        /// IsDaylightSavingTime for that time (false for ambiguous and skipped times, as in .NET);
        /// <paramref name="invalid"/> marks a skipped time.
        /// </summary>
        private static long LocalUtcOffset(long ticks, bool ambiguousDst, out bool dst, out bool invalid)
        {
            long local = UnixSeconds(ticks);
            // Zone offsets are below a day, so these two instants lie on either side of any transition
            // that could make this wall-clock time ambiguous or skipped.
            long before = LocalOffsetAt(local - 172800, out bool dstBefore);
            long after = LocalOffsetAt(local + 172800, out bool dstAfter);
            bool validBefore = LocalOffsetAt(local - before, out bool _) == before;
            bool validAfter = LocalOffsetAt(local - after, out bool _) == after;
            bool ambiguous = validBefore && validAfter && dstBefore && !dstAfter;
            bool useAfter = ambiguous ? !ambiguousDst : !validBefore && validAfter;
            invalid = !validBefore && !validAfter;
            dst = !ambiguous && !invalid && (useAfter ? dstAfter : dstBefore);
            return invalid ? BaseOffset() : RuleOffset(useAfter ? after : before);
        }

        private static long LocalUtcOffset(long ticks, bool ambiguousDst) => LocalUtcOffset(ticks, ambiguousDst, out bool _, out bool _);

        // For the System.Globalization port's TimeZoneInfo stand-in (Globalization/GlobalizationHelpers.cs).
        internal static long LocalUtcOffsetTicks(long data) =>
            (IsUtc(data) ? OffsetFromUtc(TicksOf(data), out bool _) : LocalUtcOffset(TicksOf(data), IsAmbiguousDst(data))) * TicksPerSecond;

        internal static long OffsetFromUtcTicks(long utcTicks, out bool ambiguousDst) => OffsetFromUtc(utcTicks, out ambiguousDst) * TicksPerSecond;

        internal static long MakeLocal(long ticks, bool ambiguousDst) => ticks | (ambiguousDst ? KindLocalAmbiguousDst : KindLocal);

        /// <summary>
        /// The offset at a UTC instant, like TimeZoneInfo.GetUtcOffsetFromUtc; <paramref name="ambiguousDst"/>
        /// is set when the resulting local time is the daylight-saving occurrence of an ambiguous hour.
        /// </summary>
        private static long OffsetFromUtc(long utcTicks, out bool ambiguousDst)
        {
            long u = UnixSeconds(utcTicks);
            long offset = LocalOffsetAt(u, out bool dst);
            ambiguousDst = false;
            if (dst)
            {
                // Clocks turned back within the next `offset - next` seconds: this local time recurs.
                long next = LocalOffsetAt(u + 172800, out bool _);
                if (next < offset && LocalOffsetAt(u + (offset - next), out bool _) == next) ambiguousDst = true;
            }
            return RuleOffset(offset);
        }

        /// <summary>A UTC tick count as a Local DateTime (ToLocalTime, Now): clamped to the DateTime range.</summary>
        private static long UtcToLocal(long utcTicks)
        {
            long ticks = utcTicks + OffsetFromUtc(utcTicks, out bool ambiguousDst) * TicksPerSecond;
            if (ticks > MaxTicks) return MaxTicks | KindLocal;
            if (ticks < 0) return KindLocal;
            return ticks | (ambiguousDst ? KindLocalAmbiguousDst : KindLocal);
        }

        internal static long Now(long utcNow) => UtcToLocal(TicksOf(utcNow));

        internal static long Today(long utcNow)
        {
            long now = Now(utcNow);
            return now - TicksOf(now) % TicksPerDay;
        }

        internal static long ToLocalTime(long data) => IsLocal(data) ? data : UtcToLocal(TicksOf(data));

        /// <summary>ToUniversalTime: an Unspecified DateTime counts as local time; the result is clamped.</summary>
        internal static long ToUniversalTime(long data)
        {
            if (IsUtc(data)) return data;
            long ticks = TicksOf(data);
            long offset = LocalUtcOffset(ticks, IsAmbiguousDst(data), out bool _, out bool invalid) * TicksPerSecond;
            // .NET (TimeZoneInfo.ConvertTime with NoThrowOnInvalidTime) adds the offset of a skipped time
            // instead of subtracting it: 02:30 on a New York spring-forward day becomes 21:30Z the day before.
            long utc = invalid ? ticks + offset : ticks - offset;
            if (utc > MaxTicks) utc = MaxTicks;
            else if (utc < 0) utc = 0;
            return utc | KindUtc;
        }

        internal static bool IsDaylightSavingTime(long data)
        {
            if (IsUtc(data)) return false;
            LocalUtcOffset(TicksOf(data), IsAmbiguousDst(data), out bool dst, out bool _);
            return dst;
        }

        /// <summary>ToBinary: a Local DateTime is stored as UTC ticks (wrapped below zero) with the local bit.</summary>
        internal static long ToBinary(long data)
        {
            if (!IsLocal(data)) return data;
            long ticks = TicksOf(data);
            long stored = ticks - LocalUtcOffset(ticks, IsAmbiguousDst(data)) * TicksPerSecond;
            if (stored < 0) stored = TicksCeiling + stored;
            return stored | KindLocal;
        }

        internal static long FromBinary(long binary)
        {
            if ((binary & KindLocal) != 0)
            {
                long ticks = TicksOf(binary);
                if (ticks > TicksCeiling - TicksPerDay) ticks -= TicksCeiling;
                bool ambiguousDst = false;
                long offset;
                if (ticks < 0) offset = LocalUtcOffset(0, false);
                else if (ticks > MaxTicks) offset = LocalUtcOffset(MaxTicks, false);
                else offset = OffsetFromUtc(ticks, out ambiguousDst);
                ticks += offset * TicksPerSecond;
                if (ticks < 0) ticks += TicksPerDay;  // .NET's own wrap for MinValue round trips
                if (ticks < 0 || ticks > MaxTicks) BadBinaryData();
                return ticks | (ambiguousDst ? KindLocalAmbiguousDst : KindLocal);
            }
            if (TicksOf(binary) > MaxTicks) BadBinaryData();
            return binary;
        }

        private static void BadBinaryData() => throw new ArgumentException(
            "The binary data must result in a DateTime with ticks between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks.", "dateData");

        internal static long ToFileTimeUtc(long data)
        {
            long ticks = IsLocal(data) ? TicksOf(ToUniversalTime(data)) : TicksOf(data);
            ticks -= FileTimeOffset;
            if (ticks < 0) throw new ArgumentOutOfRangeException(null, "Not a valid Win32 FileTime.");
            return ticks;
        }

        internal static long ToFileTime(long data) => ToFileTimeUtc(ToUniversalTime(data));

        internal static long FromFileTimeUtc(long fileTime)
        {
            if ((ulong)fileTime > (ulong)(MaxTicks - FileTimeOffset))
                throw new ArgumentOutOfRangeException("fileTime", "Not a valid Win32 FileTime.");
            return (fileTime + FileTimeOffset) | KindUtc;
        }

        internal static long FromFileTime(long fileTime) => ToLocalTime(FromFileTimeUtc(fileTime));

        [RuntimeExport("csrt_datetime_to_string", "csrt_Boxed_DateTime_MT")]
        internal static string DateTimeToString(long data) => FormatWith(data, null, null);

        internal static string Format(long data, string format) => FormatWith(data, format, null);

        /// <summary>DateTime.ToString(format, provider): .NET's DateTimeFormat (Globalization/DateTimeFormat.cs) with the culture's DateTimeFormatInfo.</summary>
        internal static string FormatWith(long data, string format, IFormatProvider provider) =>
            CsharpcCoreLib.Globalization.DateTimeFormat.Format(CoreIntrinsics.MakeDateTime(data), format, provider);

    }

    /// <summary>
    /// DateTime members beyond the codegen's inline ones (BclValueTypes.cs), on the raw `_dateData`
    /// word (CoreIntrinsics.DateTimeData / MakeDateTime).
    /// </summary>
    [ShadowMembers("System.DateTime")]
    internal static class DateTimeMembers
    {
        private static long D(DateTime d) => CoreIntrinsics.DateTimeData(d);
        private static DateTime M(long data) => CoreIntrinsics.MakeDateTime(data);

        public static DateTime SpecifyKind(DateTime value, DateTimeKind kind) => M(DateTimeOps.SpecifyKind(D(value), (int)kind));
        [ShadowInstance] public static DateTime ToUniversalTime(DateTime @this) => M(DateTimeOps.ToUniversalTime(D(@this)));
        [ShadowInstance] public static DateTime ToLocalTime(DateTime @this) => M(DateTimeOps.ToLocalTime(D(@this)));
        [ShadowInstance] public static bool IsDaylightSavingTime(DateTime @this) => DateTimeOps.IsDaylightSavingTime(D(@this));
        [ShadowInstance] public static long ToBinary(DateTime @this) => DateTimeOps.ToBinary(D(@this));
        public static DateTime FromBinary(long dateData) => M(DateTimeOps.FromBinary(dateData));
        [ShadowInstance] public static long ToFileTime(DateTime @this) => DateTimeOps.ToFileTime(D(@this));
        [ShadowInstance] public static long ToFileTimeUtc(DateTime @this) => DateTimeOps.ToFileTimeUtc(D(@this));
        public static DateTime FromFileTime(long fileTime) => M(DateTimeOps.FromFileTime(fileTime));
        public static DateTime FromFileTimeUtc(long fileTime) => M(DateTimeOps.FromFileTimeUtc(fileTime));

        // Parsing: .NET's DateTimeParse (Globalization/DateTimeParse.cs) with the provider's DateTimeFormatInfo,
        // argument checks as in DateTime.cs.
        private static Globalization.DateTimeFormatInfo Dtfi(IFormatProvider provider) => Globalization.DateTimeFormatInfo.GetInstance(provider);

        public static DateTime Parse(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            return Globalization.DateTimeParse.Parse(s, Globalization.DateTimeFormatInfo.CurrentInfo, System.Globalization.DateTimeStyles.None);
        }

        public static DateTime Parse(string s, IFormatProvider provider)
        {
            if (s == null) throw new ArgumentNullException("s");
            return Globalization.DateTimeParse.Parse(s, Dtfi(provider), System.Globalization.DateTimeStyles.None);
        }

        public static DateTime Parse(string s, IFormatProvider provider, System.Globalization.DateTimeStyles styles)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(styles, styles: true);
            if (s == null) throw new ArgumentNullException("s");
            return Globalization.DateTimeParse.Parse(s, Dtfi(provider), styles);
        }

        public static DateTime Parse(ReadOnlySpan<char> s, IFormatProvider provider) =>
            Globalization.DateTimeParse.Parse(s, Dtfi(provider), System.Globalization.DateTimeStyles.None);

        public static DateTime Parse(ReadOnlySpan<char> s, IFormatProvider provider, System.Globalization.DateTimeStyles styles)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(styles, styles: true);
            return Globalization.DateTimeParse.Parse(s, Dtfi(provider), styles);
        }

        public static bool TryParse(string s, out DateTime result)
        {
            if (s == null) { result = default; return false; }
            return Globalization.DateTimeParse.TryParse(s, Globalization.DateTimeFormatInfo.CurrentInfo, System.Globalization.DateTimeStyles.None, out result);
        }

        public static bool TryParse(string s, IFormatProvider provider, out DateTime result) =>
            TryParse(s, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(string s, IFormatProvider provider, System.Globalization.DateTimeStyles styles, out DateTime result)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(styles, styles: true);
            if (s == null) { result = default; return false; }
            return Globalization.DateTimeParse.TryParse(s, Dtfi(provider), styles, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, out DateTime result) =>
            Globalization.DateTimeParse.TryParse(s, Globalization.DateTimeFormatInfo.CurrentInfo, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out DateTime result) =>
            TryParse(s, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, System.Globalization.DateTimeStyles styles, out DateTime result)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(styles, styles: true);
            return Globalization.DateTimeParse.TryParse(s, Dtfi(provider), styles, out result);
        }

        public static DateTime ParseExact(string s, string format, IFormatProvider provider)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (format == null) throw new ArgumentNullException("format");
            return Globalization.DateTimeParse.ParseExact(s, format, Dtfi(provider), System.Globalization.DateTimeStyles.None);
        }

        public static DateTime ParseExact(string s, string format, IFormatProvider provider, System.Globalization.DateTimeStyles style)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(style);
            if (s == null) throw new ArgumentNullException("s");
            if (format == null) throw new ArgumentNullException("format");
            return Globalization.DateTimeParse.ParseExact(s, format, Dtfi(provider), style);
        }

        public static DateTime ParseExact(string s, string[] formats, IFormatProvider provider, System.Globalization.DateTimeStyles style)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(style);
            if (s == null) throw new ArgumentNullException("s");
            return Globalization.DateTimeParse.ParseExactMultiple(s, formats, Dtfi(provider), style);
        }

        public static bool TryParseExact(string s, string format, IFormatProvider provider, System.Globalization.DateTimeStyles style, out DateTime result)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(style);
            if (s == null || format == null) { result = default; return false; }
            return Globalization.DateTimeParse.TryParseExact(s, format, Dtfi(provider), style, out result);
        }

        public static bool TryParseExact(string s, string[] formats, IFormatProvider provider, System.Globalization.DateTimeStyles style, out DateTime result)
        {
            Globalization.DateTimeFormatInfo.ValidateStyles(style);
            if (s == null) { result = default; return false; }
            return Globalization.DateTimeParse.TryParseExactMultiple(s, formats, Dtfi(provider), style, out result);
        }

        // DateTime.ToString / TryFormat with a provider (the provider-less forms are codegen members).
        [ShadowInstance] public static string ToShortDateString(DateTime @this) => Globalization.DateTimeFormat.Format(@this, "d", null);
        [ShadowInstance] public static string ToLongDateString(DateTime @this) => Globalization.DateTimeFormat.Format(@this, "D", null);
        [ShadowInstance] public static string ToShortTimeString(DateTime @this) => Globalization.DateTimeFormat.Format(@this, "t", null);
        [ShadowInstance] public static string ToLongTimeString(DateTime @this) => Globalization.DateTimeFormat.Format(@this, "T", null);
        [ShadowInstance] public static string[] GetDateTimeFormats(DateTime @this) => GetDateTimeFormats(@this, null);
        [ShadowInstance] public static string[] GetDateTimeFormats(DateTime @this, IFormatProvider provider) =>
            Globalization.DateTimeFormat.GetAllDateTimes(@this, Dtfi(provider));
        [ShadowInstance] public static string[] GetDateTimeFormats(DateTime @this, char format) => GetDateTimeFormats(@this, format, null);
        [ShadowInstance] public static string[] GetDateTimeFormats(DateTime @this, char format, IFormatProvider provider) =>
            Globalization.DateTimeFormat.GetAllDateTimes(@this, format, Dtfi(provider));

        [ShadowInstance] public static bool Equals(DateTime @this, object value) => value is DateTime other && @this.Ticks == other.Ticks;

        [ShadowInstance]
        public static int CompareTo(DateTime @this, object value)
        {
            if (value == null) return 1;
            if (!(value is DateTime other)) throw new ArgumentException("Object must be of type DateTime.");
            return @this.Ticks.CompareTo(other.Ticks);
        }
    }
}
