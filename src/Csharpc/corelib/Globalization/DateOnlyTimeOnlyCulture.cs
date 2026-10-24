// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Formatting and parsing of DateOnly / TimeOnly as System.DateOnly / System.TimeOnly do it (dotnet/runtime
// release/11.0, commit bc123d0bd173, src/libraries/System.Private.CoreLib/src/System/{DateOnly,TimeOnly}.cs;
// see THIRD-PARTY-NOTICES.md): through DateTimeFormat / DateTimeParse with the provider's DateTimeFormatInfo,
// on the equivalent DateTime. csharpc's DateOnly/TimeOnly shadows (corelib DateOnlyTimeOnly.cs) call these.
#nullable enable annotations

using System;
using System.Globalization;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Globalization;
    internal static class DateOnlyTimeOnlyCulture
    {
        private const string DateOFormat = "yyyy'-'MM'-'dd";
        private const string DateRFormat = "ddd, dd MMM yyyy";
        private const string TimeOFormat = "HH':'mm':'ss'.'fffffff";
        private const string TimeRFormat = "HH':'mm':'ss";

        private const ParseFlags ParseFlagsDateMask = ParseFlags.HaveHour | ParseFlags.HaveMinute | ParseFlags.HaveSecond | ParseFlags.HaveTime | ParseFlags.TimeZoneUsed |
                                                      ParseFlags.TimeZoneUtc | ParseFlags.CaptureOffset | ParseFlags.UtcSortPattern;

        private const ParseFlags ParseFlagsTimeMask = ParseFlags.HaveYear | ParseFlags.HaveMonth | ParseFlags.HaveDay | ParseFlags.HaveDate | ParseFlags.TimeZoneUsed |
                                                      ParseFlags.TimeZoneUtc | ParseFlags.ParsedMonthName | ParseFlags.CaptureOffset | ParseFlags.UtcSortPattern;

        // ---- formatting --------------------------------------------------------------------------

        internal static string FormatDate(long dayNumber, string? format, IFormatProvider? provider)
        {
            DateTime dateTime = new DateTime(dayNumber * TimeSpan.TicksPerDay);
            if (string.IsNullOrEmpty(format)) format = "d";
            if (format.Length == 1)
            {
                switch (format[0] | 0x20)
                {
                    case 'o': return DateTimeFormat.Format(dateTime, DateOFormat, CultureInfo.InvariantCulture);
                    case 'r': return DateTimeFormat.Format(dateTime, DateRFormat, CultureInfo.InvariantCulture);
                    case 'm':
                    case 'd':
                    case 'y': return DateTimeFormat.Format(dateTime, format, provider);
                    default: throw new FormatException(SR.Format_InvalidString);
                }
            }
            DateTimeFormat.IsValidCustomDateOnlyFormat(format.AsSpan(), throwOnError: true);
            return DateTimeFormat.Format(dateTime, format, provider);
        }

        internal static string FormatTime(long ticks, string? format, IFormatProvider? provider)
        {
            DateTime dateTime = new DateTime(ticks);
            if (string.IsNullOrEmpty(format)) format = "t";
            if (format.Length == 1)
            {
                switch (format[0] | 0x20)
                {
                    case 'o': return DateTimeFormat.Format(dateTime, TimeOFormat, CultureInfo.InvariantCulture);
                    case 'r': return DateTimeFormat.Format(dateTime, TimeRFormat, CultureInfo.InvariantCulture);
                    case 't': return DateTimeFormat.Format(dateTime, format, provider);
                    default: throw new FormatException(SR.Format_InvalidString);
                }
            }
            DateTimeFormat.IsValidCustomTimeOnlyFormat(format.AsSpan(), throwOnError: true);
            return DateTimeFormat.Format(dateTime, format, provider);
        }

        // ---- parsing -----------------------------------------------------------------------------
        // `value` is the day number (DateOnly) or the ticks (TimeOnly) of the result.

        private static void CheckStyle(DateTimeStyles style)
        {
            if ((style & ~DateTimeStyles.AllowWhiteSpaces) != 0)
                throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
        }

        private static long ValueOf(DateTime parsed, bool date) =>
            date ? parsed.Ticks / TimeSpan.TicksPerDay : parsed.TimeOfDay.Ticks;

        private static ParseFailureKind TryParseInternal(ReadOnlySpan<char> s, IFormatProvider? provider, DateTimeStyles style, bool date, out long value)
        {
            DateTimeResult dtResult = default;
            dtResult.Init(s);
            value = 0;
            if (!DateTimeParse.TryParse(s, DateTimeFormatInfo.GetInstance(provider), style, ref dtResult))
                return date ? ParseFailureKind.Format_BadDateOnly : ParseFailureKind.Format_BadTimeOnly;
            if ((dtResult.flags & (date ? ParseFlagsDateMask : ParseFlagsTimeMask)) != 0)
                return ParseFailureKind.Format_DateTimeOnlyContainsNoneDateParts;
            value = ValueOf(dtResult.parsedDate, date);
            return ParseFailureKind.None;
        }

        private static ParseFailureKind TryParseExactInternal(ReadOnlySpan<char> s, string?[] formats, IFormatProvider? provider, DateTimeStyles style, bool date, out long value)
        {
            DateTimeFormatInfo dtfi = DateTimeFormatInfo.GetInstance(provider);
            value = 0;
            for (int i = 0; i < formats.Length; i++)
            {
                DateTimeFormatInfo dtfiToUse = dtfi;
                string? format = formats[i];
                if (string.IsNullOrEmpty(format))
                    return ParseFailureKind.Argument_BadFormatSpecifier;
                if (format.Length == 1)
                {
                    switch (format[0] | 0x20)
                    {
                        case 'o':
                            format = date ? DateOFormat : TimeOFormat;
                            dtfiToUse = DateTimeFormat.InvariantFormatInfo;
                            break;
                        case 'r':
                            format = date ? DateRFormat : TimeRFormat;
                            dtfiToUse = DateTimeFormat.InvariantFormatInfo;
                            break;
                    }
                }

                DateTimeResult dtResult = default;
                dtResult.Init(s);
                if (DateTimeParse.TryParseExact(s, format, dtfiToUse, style, ref dtResult) && ((dtResult.flags & (date ? ParseFlagsDateMask : ParseFlagsTimeMask)) == 0))
                {
                    value = ValueOf(dtResult.parsedDate, date);
                    return ParseFailureKind.None;
                }
                if (dtResult.failure == ParseFailureKind.Argument_BadFormatSpecifier)
                    return ParseFailureKind.Argument_BadFormatSpecifier;
            }
            return date ? ParseFailureKind.Format_BadDateOnly : ParseFailureKind.Format_BadTimeOnly;
        }

        private static void ThrowOnError(ParseFailureKind result, ReadOnlySpan<char> s, bool date)
        {
            switch (result)
            {
                case ParseFailureKind.Argument_InvalidDateStyles: throw new ArgumentException(SR.Argument_InvalidDateStyles, "style");
                case ParseFailureKind.Argument_BadFormatSpecifier: throw new FormatException(SR.Argument_BadFormatSpecifier);
                case ParseFailureKind.Format_BadDateOnly: throw new FormatException(SR.Format(SR.Format_BadDateOnly, s.ToString()));
                case ParseFailureKind.Format_BadTimeOnly: throw new FormatException(SR.Format(SR.Format_BadTimeOnly, s.ToString()));
                default: throw new FormatException(SR.Format(SR.Format_DateTimeOnlyContainsNoneDateParts, s.ToString(), date ? "DateOnly" : "TimeOnly"));
            }
        }

        internal static long Parse(string? s, IFormatProvider? provider, DateTimeStyles style, bool date)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            CheckStyle(style);
            ParseFailureKind result = TryParseInternal(s, provider, style, date, out long value);
            if (result != ParseFailureKind.None) ThrowOnError(result, s, date);
            return value;
        }

        internal static bool TryParse(string? s, IFormatProvider? provider, DateTimeStyles style, bool date, out long value)
        {
            CheckStyle(style);
            if (s == null) { value = 0; return false; }
            return TryParseInternal(s, provider, style, date, out value) == ParseFailureKind.None;
        }

        internal static long ParseExact(string? s, string?[]? formats, IFormatProvider? provider, DateTimeStyles style, bool date)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (formats == null) throw new ArgumentNullException(nameof(formats));
            CheckStyle(style);
            ParseFailureKind result = TryParseExactInternal(s, formats, provider, style, date, out long value);
            if (result != ParseFailureKind.None) ThrowOnError(result, s, date);
            return value;
        }

        internal static bool TryParseExact(string? s, string?[]? formats, IFormatProvider? provider, DateTimeStyles style, bool date, out long value)
        {
            CheckStyle(style);
            value = 0;
            if (formats == null) throw new ArgumentException(SR.Argument_InvalidDateStyles, nameof(style));
            for (int i = 0; i < formats.Length; i++)
                if (string.IsNullOrEmpty(formats[i])) throw new FormatException(SR.Argument_BadFormatSpecifier);
            if (s == null) return false;
            return TryParseExactInternal(s, formats, provider, style, date, out value) == ParseFailureKind.None;
        }
    }
}
