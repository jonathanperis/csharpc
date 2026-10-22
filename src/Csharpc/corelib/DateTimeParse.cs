// DateTime.Parse / TryParse / ParseExact / TryParseExact in the invariant culture.
//
// ParseExact follows .NET's ParseByFormat for the custom specifiers (y M d h H m s f F t z K, quotes,
// escapes, ':' '/') and the invariant standard patterns. DateTime.Parse is .NET's lenient parser,
// which accepts an open-ended set of shapes; csharpc implements the common ones (ISO 8601 dates and
// times, M/d/yyyy, month names, AM/PM, 'Z'/GMT/offsets) and throws NotSupportedException for any
// other shape rather than guess. Inputs that are certainly invalid (empty, unknown words, impossible
// dates) fail with .NET's FormatException messages.
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    internal static class DateTimeParse
    {
        private const long TicksPerSecond = 10000000L, TicksPerMinute = 600000000L, TicksPerHour = 36000000000L;
        private const long MaxTicks = 3155378975999999999L;

        // Failure kinds (0 = success).
        private const int Ok = 0, Bad = 1, UnknownWord = 2, BadDayOfWeek = 3, NotInCalendar = 4, OffsetRange = 5, Unsupported = 6;

        private struct Parts
        {
            public int Year, Month, Day, Hour, Minute, Second, Ampm, DayOfWeek;
            public double Fraction;
            public bool TwoDigitYear, HasOffset, UtcDesignator;
            public long OffsetTicks;

            public static Parts Empty()
            {
                Parts p = default;
                p.Year = p.Month = p.Day = p.Hour = p.Minute = p.Second = p.Ampm = p.DayOfWeek = -1;
                return p;
            }
        }

        // ---- public entry points (DateTimeMembers) -------------------------------------------------

        internal static DateTime Parse(string s, DateTimeStyles styles)
        {
            if (s == null) throw new ArgumentNullException("s");
            ValidateStyles(styles, "styles");
            int failure = Lenient(s, styles, out long data, out int index);
            if (failure != Ok) Throw(failure, s, index, "DateTime.Parse");
            return CoreIntrinsics.MakeDateTime(data);
        }

        internal static bool TryParse(string s, DateTimeStyles styles, out DateTime result)
        {
            ValidateStyles(styles, "styles");
            result = default;
            if (s == null) return false;
            int failure = Lenient(s, styles, out long data, out int _);
            if (failure == Unsupported) Throw(failure, s, 0, "DateTime.TryParse");
            if (failure != Ok) return false;
            result = CoreIntrinsics.MakeDateTime(data);
            return true;
        }

        internal static DateTime ParseExact(string s, string[] formats, DateTimeStyles style)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (formats == null) throw new ArgumentNullException("formats");
            ValidateStyles(style, "style");
            if (formats.Length == 0) throw new FormatException("Format specifier was invalid.");
            int failure = Bad;
            for (int i = 0; i < formats.Length; i++)
            {
                if (string.IsNullOrEmpty(formats[i])) throw new FormatException("Format specifier was invalid.");
                failure = Exact(s, formats[i], style, out long data);
                if (failure == Ok) return CoreIntrinsics.MakeDateTime(data);
                if (failure == Unsupported) break;
            }
            Throw(failure, s, 0, "DateTime.ParseExact");
            return default;
        }

        internal static bool TryParseExact(string s, string[] formats, DateTimeStyles style, out DateTime result)
        {
            ValidateStyles(style, "style");
            result = default;
            if (s == null || formats == null) return false;
            for (int i = 0; i < formats.Length; i++)
            {
                if (string.IsNullOrEmpty(formats[i])) return false;
                int failure = Exact(s, formats[i], style, out long data);
                if (failure == Ok) { result = CoreIntrinsics.MakeDateTime(data); return true; }
                if (failure == Unsupported) Throw(failure, s, 0, "DateTime.TryParseExact");
            }
            return false;
        }

        /// <summary>
        /// DateOnly / TimeOnly parsing (<paramref name="formats"/> null = Parse): the DateTime parsers,
        /// then the parts that don't belong to the type are an error. Result: the day number or the
        /// time-of-day ticks.
        /// </summary>
        internal static bool ParseOnly(string s, string[] formats, DateTimeStyles styles, bool date, bool throwOnError, out long value)
        {
            value = 0;
            string type = date ? "DateOnly" : "TimeOnly";
            string api = type + (formats == null ? ".Parse" : ".ParseExact");
            if (s == null)
            {
                if (throwOnError) throw new ArgumentNullException("s");
                return false;
            }
            if ((styles & ~DateTimeStyles.AllowWhiteSpaces) != 0) throw new NotSupportedException(api + " with DateTimeStyles '" + styles + "' is not supported by csharpc yet.");
            Parts p = Parts.Empty();
            int failure = Bad;
            if (formats == null) failure = LenientParts(s, out p, out int _);
            else
            {
                if (throwOnError && formats.Length == 0) throw new FormatException("Format specifier was invalid.");
                for (int i = 0; i < formats.Length && failure != Ok && failure != Unsupported; i++)
                {
                    if (string.IsNullOrEmpty(formats[i]))
                    {
                        if (throwOnError) throw new FormatException("Format specifier was invalid.");
                        return false;
                    }
                    failure = ExactParts(s, formats[i], styles, out p);
                }
            }
            if (failure == Unsupported) Throw(Unsupported, s, 0, api);
            if (failure == Ok)
            {
                bool foreign = date ? p.Hour >= 0 || p.HasOffset
                    : p.Year >= 0 || p.Month >= 0 || p.Day >= 0 || p.DayOfWeek >= 0 || p.HasOffset;
                if (foreign)
                {
                    if (throwOnError) throw new FormatException("String '" + s + "' contains parts which are not specific to the " + type + ".");
                    return false;
                }
                failure = Finish(ref p, DateTimeStyles.NoCurrentDateDefault, timeRangeIsCalendarError: false, out long data);
                if (failure == Ok)
                {
                    long ticks = data & 0x3FFFFFFFFFFFFFFF;
                    value = date ? ticks / (TicksPerHour * 24) : ticks % (TicksPerHour * 24);
                    return true;
                }
            }
            if (throwOnError) throw new FormatException("String '" + s + "' was not recognized as a valid " + type + ".");
            return false;
        }

        private static void ValidateStyles(DateTimeStyles styles, string param)
        {
            const DateTimeStyles all = DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite | DateTimeStyles.AllowInnerWhite
                | DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal
                | DateTimeStyles.AssumeUniversal | DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces;
            if ((styles & ~all) != 0) throw new ArgumentException("An undefined DateTimeStyles value is being used.", param);
            if ((styles & DateTimeStyles.AssumeLocal) != 0 && (styles & DateTimeStyles.AssumeUniversal) != 0)
                throw new ArgumentException("The DateTimeStyles values AssumeLocal and AssumeUniversal cannot be used together.", param);
            if ((styles & DateTimeStyles.RoundtripKind) != 0
                && (styles & (DateTimeStyles.AssumeLocal | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)) != 0)
                throw new ArgumentException("The DateTimeStyles value RoundtripKind cannot be used with the values AssumeLocal, AssumeUniversal or AdjustToUniversal.", param);
        }

        private static void Throw(int failure, string s, int index, string api)
        {
            switch (failure)
            {
                case UnknownWord:
                    throw new FormatException("The string '" + s + "' was not recognized as a valid DateTime. There is an unknown word starting at index '" + index + "'.");
                case BadDayOfWeek:
                    throw new FormatException("String '" + s + "' was not recognized as a valid DateTime because the day of week was incorrect.");
                case NotInCalendar:
                    throw new FormatException("The DateTime represented by the string '" + s + "' is not supported in calendar 'System.Globalization.GregorianCalendar'.");
                case OffsetRange:
                    throw new FormatException("The time zone offset of string '" + s + "' must be within plus or minus 14 hours.");
                case Unsupported:
                    throw new NotSupportedException(api + ": the format of '" + s + "' is not supported by csharpc yet.");
                default:
                    throw new FormatException("String '" + s + "' was not recognized as a valid DateTime.");
            }
        }

        // ---- building the result -----------------------------------------------------------------

        /// <summary>
        /// Parsed parts -> _dateData. Missing date parts come from today (or 0001-01-01 with
        /// NoCurrentDateDefault), a missing day is 1; offsets and styles decide the Kind as in .NET.
        /// </summary>
        private static int Finish(ref Parts p, DateTimeStyles styles, bool timeRangeIsCalendarError, out long data)
        {
            data = 0;
            if (p.Year < 0 && p.Month < 0 && p.Day < 0)
            {
                if ((styles & DateTimeStyles.NoCurrentDateDefault) != 0) { p.Year = 1; p.Month = 1; p.Day = 1; }
                else
                {
                    DateTime today = DateTime.Now;
                    p.Year = today.Year; p.Month = today.Month; p.Day = today.Day;
                }
            }
            else
            {
                if (p.Year < 0) p.Year = DateTime.Now.Year;
                if (p.Month < 0) p.Month = 1;
                if (p.Day < 0) p.Day = 1;
            }
            if (p.TwoDigitYear) p.Year = p.Year > 49 ? 1900 + p.Year : 2000 + p.Year;  // Calendar.TwoDigitYearMax = 2049
            if (p.Year < 1 || p.Year > 9999 || p.Month < 1 || p.Month > 12) return Bad;
            if (p.Day < 1 || p.Day > DateTimeOps.DaysInMonth(p.Year, p.Month)) return Bad;

            int hour = p.Hour < 0 ? 0 : p.Hour, minute = p.Minute < 0 ? 0 : p.Minute, second = p.Second < 0 ? 0 : p.Second;
            if (p.Ampm >= 0)
            {
                if (hour > 12 && p.Ampm == 0) return Bad;
                if (hour == 12) hour = p.Ampm == 0 ? 0 : 12;
                else if (p.Ampm == 1 && hour < 12) hour += 12;
            }
            if (hour > 23 || minute > 59 || second > 59) return timeRangeIsCalendarError ? NotInCalendar : Bad;

            long ticks = DateTimeOps.DateToTicks(p.Year, p.Month, p.Day);
            if (p.DayOfWeek >= 0 && (int)((ticks / (TicksPerHour * 24) + 1) % 7) != p.DayOfWeek) return BadDayOfWeek;
            ticks += hour * TicksPerHour + minute * TicksPerMinute + second * TicksPerSecond;
            ticks += (long)Math.Round(p.Fraction * TicksPerSecond);
            if (ticks > MaxTicks) return Bad;

            bool adjust = (styles & DateTimeStyles.AdjustToUniversal) != 0;
            const long Utc = 0x4000000000000000, Local = unchecked((long)0x8000000000000000);
            if (p.HasOffset)
            {
                if (p.OffsetTicks > 14 * TicksPerHour || p.OffsetTicks < -14 * TicksPerHour) return OffsetRange;
                long utc = ticks - p.OffsetTicks;
                if (utc < 0 || utc > MaxTicks) return Bad;
                if (adjust || ((styles & DateTimeStyles.RoundtripKind) != 0 && p.UtcDesignator)) data = utc | Utc;
                else data = DateTimeOps.ToLocalTime(utc | Utc);
            }
            else if ((styles & DateTimeStyles.AssumeUniversal) != 0) data = adjust ? ticks | Utc : DateTimeOps.ToLocalTime(ticks | Utc);
            else if ((styles & DateTimeStyles.AssumeLocal) != 0) data = adjust ? DateTimeOps.ToUniversalTime(ticks | Local) : ticks | Local;
            else data = ticks;
            return Ok;
        }

        // ---- names -------------------------------------------------------------------------------

        private static bool EqualsIgnoreCase(string s, int start, int length, string word)
        {
            if (length != word.Length) return false;
            for (int i = 0; i < length; i++)
                if (CharOps.ToUpperInvariant(s[start + i]) != CharOps.ToUpperInvariant(word[i])) return false;
            return true;
        }

        /// <summary>1..12 for a full or abbreviated (3-letter) invariant month name, else 0.</summary>
        private static int MonthOf(string s, int start, int length)
        {
            for (int m = 1; m <= 12; m++)
            {
                string name = DateTimeOps.MonthName(m);
                if (EqualsIgnoreCase(s, start, length, name) || (length == 3 && EqualsIgnoreCase(s, start, 3, name.Substring(0, 3)))) return m;
            }
            return 0;
        }

        /// <summary>0..6 for a full or abbreviated invariant day name, else -1.</summary>
        private static int DayOfWeekOf(string s, int start, int length)
        {
            for (int d = 0; d < 7; d++)
            {
                string name = DateTimeOps.DayName(d);
                if (EqualsIgnoreCase(s, start, length, name) || (length == 3 && EqualsIgnoreCase(s, start, 3, name.Substring(0, 3)))) return d;
            }
            return -1;
        }

        // ---- DateTime.Parse ----------------------------------------------------------------------

        private const int TNum = 1, TWord = 2, TSep = 3, TSpace = 4;

        private struct Token
        {
            public int Kind, Start, Length, Value;
            public char Sep;
        }

        private static int Lenient(string s, DateTimeStyles styles, out long data, out int index)
        {
            data = 0;
            int failure = LenientParts(s, out Parts p, out index);
            return failure != Ok ? failure : Finish(ref p, styles, timeRangeIsCalendarError: true, out data);
        }

        private static int LenientParts(string s, out Parts p, out int index)
        {
            p = Parts.Empty();
            index = 0;
            Token[] tokens = new Token[s.Length + 1];
            int count = 0;
            for (int i = 0; i < s.Length; )
            {
                char c = s[i];
                Token t = default;
                t.Start = i;
                if (c >= '0' && c <= '9')
                {
                    t.Kind = TNum;
                    long v = 0;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') { if (v < 100000000) v = v * 10 + (s[i] - '0'); i++; }
                    t.Value = (int)v;
                }
                else if (CharOps.IsLetter(c))
                {
                    t.Kind = TWord;
                    while (i < s.Length && CharOps.IsLetter(s[i])) i++;
                }
                else if (CharOps.IsWhiteSpace(c))
                {
                    t.Kind = TSpace;
                    while (i < s.Length && CharOps.IsWhiteSpace(s[i])) i++;
                }
                else if (c == '/' || c == '-' || c == ':' || c == '.' || c == ',' || c == '+')
                {
                    t.Kind = TSep;
                    t.Sep = c;
                    i++;
                }
                else return Unsupported;
                t.Length = i - t.Start;
                tokens[count++] = t;
            }
            // Unknown words fail first, like .NET's lexer.
            for (int k = 0; k < count; k++)
            {
                if (tokens[k].Kind != TWord) continue;
                int st = tokens[k].Start, len = tokens[k].Length;
                if (MonthOf(s, st, len) > 0 || DayOfWeekOf(s, st, len) >= 0 || EqualsIgnoreCase(s, st, len, "AM") || EqualsIgnoreCase(s, st, len, "PM")
                    || EqualsIgnoreCase(s, st, len, "T") || EqualsIgnoreCase(s, st, len, "Z") || EqualsIgnoreCase(s, st, len, "GMT"))
                    continue;
                index = st;
                return UnknownWord;
            }

            int pos = 0;
            SkipSpace(tokens, count, ref pos);
            if (pos == count) return Bad;
            // Optional leading day name: "Wednesday, January 15, 2025".
            if (tokens[pos].Kind == TWord && DayOfWeekOf(s, tokens[pos].Start, tokens[pos].Length) is int dow && dow >= 0)
            {
                p.DayOfWeek = dow;
                pos++;
                if (pos < count && tokens[pos].Kind == TSep && tokens[pos].Sep == ',') pos++;
                SkipSpace(tokens, count, ref pos);
            }

            int r = ParseDate(s, tokens, count, ref pos, ref p);
            if (r != Ok) return r;
            bool hasDate = p.Month >= 0;
            if (hasDate && pos < count)
            {
                // Date and time are separated by 'T' or white space.
                if (tokens[pos].Kind == TWord && tokens[pos].Length == 1 && (s[tokens[pos].Start] == 'T' || s[tokens[pos].Start] == 't'))
                {
                    pos++;
                    if (pos == count || tokens[pos].Kind != TNum) return pos == count ? Bad : Unsupported;
                }
                else if (tokens[pos].Kind == TSpace) SkipSpace(tokens, count, ref pos);
                else if (!(tokens[pos].Kind == TWord || (tokens[pos].Kind == TSep && tokens[pos].Sep is '+' or '-'))) return Unsupported;
            }
            if (pos < count && tokens[pos].Kind == TNum)
            {
                r = ParseTime(s, tokens, count, ref pos, ref p);
                if (r != Ok) return r;
            }
            else if (!hasDate) return Unsupported;
            SkipSpace(tokens, count, ref pos);
            if (pos < count)
            {
                r = ParseOffset(s, tokens, count, ref pos, ref p);
                if (r != Ok) return r;
                SkipSpace(tokens, count, ref pos);
            }
            return pos != count ? Unsupported : Ok;
        }

        private static void SkipSpace(Token[] t, int count, ref int pos)
        {
            while (pos < count && t[pos].Kind == TSpace) pos++;
        }

        private static bool IsNum(Token[] t, int count, int pos, int minDigits, int maxDigits) =>
            pos < count && t[pos].Kind == TNum && t[pos].Length >= minDigits && t[pos].Length <= maxDigits;

        private static bool IsSep(Token[] t, int count, int pos, char sep) => pos < count && t[pos].Kind == TSep && t[pos].Sep == sep;

        private static int ParseDate(string s, Token[] t, int count, ref int pos, ref Parts p)
        {
            // yyyy-M-d, yyyy/M/d, yyyy-M
            if (IsNum(t, count, pos, 4, 4) && pos + 1 < count && t[pos + 1].Kind == TSep && t[pos + 1].Sep is '-' or '/')
            {
                char sep = t[pos + 1].Sep;
                if (!IsNum(t, count, pos + 2, 1, 2)) return Unsupported;
                p.Year = t[pos].Value;
                p.Month = t[pos + 2].Value;
                pos += 3;
                if (IsSep(t, count, pos, sep))
                {
                    if (!IsNum(t, count, pos + 1, 1, 2)) return Unsupported;
                    p.Day = t[pos + 1].Value;
                    pos += 2;
                }
                return Ok;
            }
            // d MMMM yyyy, d-MMM-yyyy
            if (IsNum(t, count, pos, 1, 2) && pos + 2 < count && (t[pos + 1].Kind == TSpace || IsSep(t, count, pos + 1, '-'))
                && t[pos + 2].Kind == TWord && MonthOf(s, t[pos + 2].Start, t[pos + 2].Length) is int m2 && m2 > 0)
            {
                bool dash = t[pos + 1].Kind == TSep;
                int q = pos + 3;
                if (dash) { if (!IsSep(t, count, q, '-')) return Unsupported; q++; }
                else SkipSpace(t, count, ref q);
                if (!IsNum(t, count, q, 4, 4)) return Unsupported;
                p.Day = t[pos].Value;
                p.Month = m2;
                p.Year = t[q].Value;
                pos = q + 1;
                return Ok;
            }
            // M/d/yyyy, M-d-yyyy, M/d/yy, M/d
            if (IsNum(t, count, pos, 1, 2) && pos + 1 < count && t[pos + 1].Kind == TSep && t[pos + 1].Sep is '/' or '-')
            {
                char sep = t[pos + 1].Sep;
                if (!IsNum(t, count, pos + 2, 1, 2)) return Unsupported;
                p.Month = t[pos].Value;
                p.Day = t[pos + 2].Value;
                pos += 3;
                if (IsSep(t, count, pos, sep))
                {
                    if (IsNum(t, count, pos + 1, 4, 4)) p.Year = t[pos + 1].Value;
                    else if (IsNum(t, count, pos + 1, 2, 2)) { p.Year = t[pos + 1].Value; p.TwoDigitYear = true; }
                    else return Unsupported;
                    pos += 2;
                    if (IsSep(t, count, pos, sep)) return Bad;  // 1/2/3/4
                }
                return Ok;
            }
            // MMMM d[,] yyyy, MMMM yyyy, MMMM d
            if (pos < count && t[pos].Kind == TWord && MonthOf(s, t[pos].Start, t[pos].Length) is int month && month > 0)
            {
                p.Month = month;
                int q = pos + 1;
                SkipSpace(t, count, ref q);
                if (IsNum(t, count, q, 4, 4)) { p.Year = t[q].Value; pos = q + 1; return Ok; }
                if (!IsNum(t, count, q, 1, 2)) return q == count ? Bad : Unsupported;
                p.Day = t[q].Value;
                q++;
                int afterDay = q;
                if (IsSep(t, count, q, ',')) q++;
                SkipSpace(t, count, ref q);
                if (IsNum(t, count, q, 4, 4) && !IsSep(t, count, q + 1, ':')) { p.Year = t[q].Value; pos = q + 1; }
                else pos = afterDay;
                return Ok;
            }
            // No date: a time must follow ("15:30", "3 PM"); a lone number is not a date.
            if (IsNum(t, count, pos, 1, 2) && (IsSep(t, count, pos + 1, ':') || NextIsAmPm(s, t, count, pos + 1))) return Ok;
            if (pos < count && t[pos].Kind == TNum)
            {
                int q = pos + 1;
                SkipSpace(t, count, ref q);
                if (q == count) return Bad;
            }
            return Unsupported;
        }

        private static bool NextIsAmPm(string s, Token[] t, int count, int pos)
        {
            SkipSpace(t, count, ref pos);
            return pos < count && t[pos].Kind == TWord && (EqualsIgnoreCase(s, t[pos].Start, t[pos].Length, "AM") || EqualsIgnoreCase(s, t[pos].Start, t[pos].Length, "PM"));
        }

        private static int ParseTime(string s, Token[] t, int count, ref int pos, ref Parts p)
        {
            if (!IsNum(t, count, pos, 1, 2)) return Unsupported;
            p.Hour = t[pos].Value;
            pos++;
            if (IsSep(t, count, pos, ':'))
            {
                if (!IsNum(t, count, pos + 1, 1, 2)) return Unsupported;
                p.Minute = t[pos + 1].Value;
                pos += 2;
                if (IsSep(t, count, pos, ':'))
                {
                    if (!IsNum(t, count, pos + 1, 1, 2)) return Unsupported;
                    p.Second = t[pos + 1].Value;
                    pos += 2;
                    if (IsSep(t, count, pos, '.'))
                    {
                        if (pos + 1 >= count || t[pos + 1].Kind != TNum) return Unsupported;
                        // .NET's ParseFraction: digit by digit with a decimal base of 0.1, 0.01, ...
                        double fraction = 0, scale = 0.1;
                        for (int i = 0; i < t[pos + 1].Length; i++) { fraction += (s[t[pos + 1].Start + i] - '0') * scale; scale *= 0.1; }
                        p.Fraction = fraction;
                        pos += 2;
                    }
                }
            }
            else if (!NextIsAmPm(s, t, count, pos)) return Unsupported;
            int q = pos;
            SkipSpace(t, count, ref q);
            if (q < count && t[q].Kind == TWord)
            {
                if (EqualsIgnoreCase(s, t[q].Start, t[q].Length, "AM")) { p.Ampm = 0; pos = q + 1; }
                else if (EqualsIgnoreCase(s, t[q].Start, t[q].Length, "PM")) { p.Ampm = 1; pos = q + 1; }
            }
            return Ok;
        }

        private static int ParseOffset(string s, Token[] t, int count, ref int pos, ref Parts p)
        {
            if (t[pos].Kind == TWord && (EqualsIgnoreCase(s, t[pos].Start, t[pos].Length, "Z") || EqualsIgnoreCase(s, t[pos].Start, t[pos].Length, "GMT")))
            {
                p.HasOffset = true;
                p.UtcDesignator = true;
                p.OffsetTicks = 0;
                pos++;
                return Ok;
            }
            if (t[pos].Kind == TSep && t[pos].Sep is '+' or '-')
            {
                int sign = t[pos].Sep == '-' ? -1 : 1;
                int q = pos + 1;
                int hours, minutes = 0;
                if (IsNum(t, count, q, 1, 2))
                {
                    hours = t[q].Value;
                    q++;
                    if (IsSep(t, count, q, ':'))
                    {
                        if (!IsNum(t, count, q + 1, 2, 2)) return Unsupported;
                        minutes = t[q + 1].Value;
                        q += 2;
                    }
                }
                else if (IsNum(t, count, q, 4, 4)) { hours = t[q].Value / 100; minutes = t[q].Value % 100; q++; }
                else return Unsupported;
                if (minutes > 59) return Bad;
                p.HasOffset = true;
                p.OffsetTicks = sign * (hours * TicksPerHour + minutes * TicksPerMinute);
                pos = q;
                return Ok;
            }
            return Unsupported;
        }

        // ---- DateTime.ParseExact -----------------------------------------------------------------

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
            'o' or 'O' => "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffffK",
            'R' or 'r' => "ddd, dd MMM yyyy HH':'mm':'ss 'GMT'",
            _ => null,
        };

        private static int Exact(string s, string format, DateTimeStyles styles, out long data)
        {
            data = 0;
            int failure = ExactParts(s, format, styles, out Parts p);
            return failure != Ok ? failure : Finish(ref p, styles, timeRangeIsCalendarError: false, out data);
        }

        private static int ExactParts(string s, string format, DateTimeStyles styles, out Parts p)
        {
            p = Parts.Empty();
            if ((styles & DateTimeStyles.AllowInnerWhite) != 0) return Unsupported;
            if (format.Length == 1)
            {
                string pattern = StandardPattern(format[0]);
                if (pattern == null) return format[0] == 'u' || format[0] == 'U' ? Unsupported : Bad;
                format = pattern;
            }
            int i = 0;
            if ((styles & DateTimeStyles.AllowLeadingWhite) != 0) while (i < s.Length && CharOps.IsWhiteSpace(s[i])) i++;
            int f = 0;
            while (f < format.Length)
            {
                char c = format[f];
                int n = 1;
                while (f + n < format.Length && format[f + n] == c) n++;
                switch (c)
                {
                    case 'y':
                        if (!Digits(s, ref i, n == 1 ? 1 : n, n == 1 ? 2 : n, out p.Year)) return Bad;
                        p.TwoDigitYear = n <= 2;
                        break;
                    case 'M':
                        if (n <= 2) { if (!Digits(s, ref i, n, 2, out p.Month) || p.Month < 1 || p.Month > 12) return Bad; }
                        else if (!MatchName(s, ref i, n == 3, isMonth: true, out p.Month)) return Bad;
                        break;
                    case 'd':
                        if (n <= 2) { if (!Digits(s, ref i, n, 2, out p.Day) || p.Day < 1 || p.Day > 31) return Bad; }
                        else if (!MatchName(s, ref i, n == 3, isMonth: false, out p.DayOfWeek)) return Bad;
                        break;
                    case 'h':
                    case 'H':
                        if (n > 2 || !Digits(s, ref i, n, 2, out p.Hour)) return Bad;
                        if (c == 'h') { if (p.Hour < 1 || p.Hour > 12) return Bad; }
                        else if (p.Hour > 23) return Bad;
                        break;
                    case 'm':
                        if (n > 2 || !Digits(s, ref i, n, 2, out p.Minute) || p.Minute > 59) return Bad;
                        break;
                    case 's':
                        if (n > 2 || !Digits(s, ref i, n, 2, out p.Second) || p.Second > 59) return Bad;
                        break;
                    case 'f':
                    case 'F':
                    {
                        if (n > 7) return Bad;
                        int start = i;
                        while (i < s.Length && i - start < n && s[i] >= '0' && s[i] <= '9') i++;
                        int got = i - start;
                        if (c == 'f' && got != n) return Bad;
                        long digits = 0;
                        for (int k = start; k < i; k++) digits = digits * 10 + (s[k] - '0');
                        double scale = 1;
                        for (int k = 0; k < got; k++) scale *= 10;
                        p.Fraction = got == 0 ? 0 : digits / scale;
                        break;
                    }
                    case 't':
                        if (n == 1)
                        {
                            if (i >= s.Length) return Bad;
                            char a = CharOps.ToUpperInvariant(s[i]);
                            if (a == 'A') p.Ampm = 0; else if (a == 'P') p.Ampm = 1; else return Bad;
                            i++;
                        }
                        else
                        {
                            if (i + 2 <= s.Length && EqualsIgnoreCase(s, i, 2, "AM")) p.Ampm = 0;
                            else if (i + 2 <= s.Length && EqualsIgnoreCase(s, i, 2, "PM")) p.Ampm = 1;
                            else return Bad;
                            i += 2;
                        }
                        break;
                    case 'z':
                    {
                        if (i >= s.Length || (s[i] != '+' && s[i] != '-')) return Bad;
                        int sign = s[i] == '-' ? -1 : 1;
                        i++;
                        if (!Digits(s, ref i, n == 1 ? 1 : 2, 2, out int hours)) return Bad;
                        int minutes = 0;
                        if (n >= 3)
                        {
                            if (i >= s.Length || s[i] != ':') return Bad;
                            i++;
                            if (!Digits(s, ref i, 2, 2, out minutes) || minutes > 59) return Bad;
                        }
                        p.HasOffset = true;
                        p.OffsetTicks = sign * (hours * TicksPerHour + minutes * TicksPerMinute);
                        break;
                    }
                    case 'K':
                        for (int k = 0; k < n; k++)
                        {
                            if (i < s.Length && (s[i] == 'Z' || s[i] == 'z')) { p.HasOffset = true; p.UtcDesignator = true; p.OffsetTicks = 0; i++; }
                            else if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                            {
                                int sign = s[i] == '-' ? -1 : 1;
                                i++;
                                if (!Digits(s, ref i, 2, 2, out int hours)) return Bad;
                                if (i >= s.Length || s[i] != ':') return Bad;
                                i++;
                                if (!Digits(s, ref i, 2, 2, out int minutes) || minutes > 59) return Bad;
                                p.HasOffset = true;
                                p.OffsetTicks = sign * (hours * TicksPerHour + minutes * TicksPerMinute);
                            }
                        }
                        break;
                    case '\'':
                    case '"':
                    {
                        int end = f + 1;
                        while (end < format.Length && format[end] != c)
                        {
                            if (format[end] == '\\' && end + 1 < format.Length) end++;
                            if (i >= s.Length || s[i] != format[end]) return Bad;
                            i++;
                            end++;
                        }
                        if (end >= format.Length) throw new FormatException("Cannot find a matching quote character for the character '" + c + "'.");
                        f = end + 1;
                        continue;
                    }
                    case '\\':
                        if (f + 1 >= format.Length) throw new FormatException("Input string was not in a correct format.");
                        if (i >= s.Length || s[i] != format[f + 1]) return Bad;
                        i++;
                        f += 2;
                        continue;
                    case '%':
                        if (f + 1 >= format.Length || format[f + 1] == '%') throw new FormatException("Input string was not in a correct format.");
                        f++;
                        continue;
                    default:
                        // Literal characters (':' and '/' are the invariant separators) match one for one.
                        if (CharOps.IsLetter(c) && c != 'T' && c != 'Z') return Unsupported;  // g, other letters
                        for (int k = 0; k < n; k++)
                        {
                            if (i >= s.Length || s[i] != c) return Bad;
                            i++;
                        }
                        break;
                }
                f += n;
            }
            if ((styles & DateTimeStyles.AllowTrailingWhite) != 0) while (i < s.Length && CharOps.IsWhiteSpace(s[i])) i++;
            return i != s.Length ? Bad : Ok;
        }

        /// <summary>Between min and max ASCII digits at s[i] (greedy).</summary>
        private static bool Digits(string s, ref int i, int min, int max, out int value)
        {
            value = 0;
            int start = i;
            while (i < s.Length && i - start < max && s[i] >= '0' && s[i] <= '9') { value = value * 10 + (s[i] - '0'); i++; }
            return i - start >= min;
        }

        /// <summary>A month (1..12) or day-of-week (0..6) name at s[i]: abbreviated (MMM/ddd) or full.</summary>
        private static bool MatchName(string s, ref int i, bool abbreviated, bool isMonth, out int value)
        {
            int count = isMonth ? 12 : 7;
            // Longest match first, as .NET does (e.g. "May" vs. a longer full name is irrelevant here, but
            // full names never share a prefix that is another full name).
            for (int k = 0; k < count; k++)
            {
                string name = isMonth ? DateTimeOps.MonthName(k + 1) : DateTimeOps.DayName(k);
                if (abbreviated) name = name.Substring(0, 3);
                if (i + name.Length <= s.Length && EqualsIgnoreCase(s, i, name.Length, name))
                {
                    value = isMonth ? k + 1 : k;
                    i += name.Length;
                    return true;
                }
            }
            value = -1;
            return false;
        }
    }
}
