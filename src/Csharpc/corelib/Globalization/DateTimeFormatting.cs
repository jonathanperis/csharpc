// DateTime, DateTimeOffset, DateOnly and TimeOnly formatting and parsing are CoreLib's own DateTimeFormat and DateTimeParse, served from
// the framework's IL with the rest of globalization (#426, src/Csharpc/upstream-types/System.Globalization.txt). The corelib keeps those
// types' representation and their thin public members, which call the framework's code through these accessors (bound at compile time:
// CodeGen/FunctionCodeGen.UnsafeAccessor.cs). The signatures are CoreLib's.
#nullable enable annotations

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Globalization;
    using System.Runtime.CompilerServices;

    internal static class DateTimeFormat
    {
        private const string Type = "CsharpcCoreLib.Upstream.System.DateTimeFormat, CsharpcUpstream";

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Format")]
        private static extern string UpstreamFormat([UnsafeAccessorType(Type)] object? type, DateTime dateTime, string? format, IFormatProvider? provider);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Format")]
        private static extern string UpstreamFormat([UnsafeAccessorType(Type)] object? type, DateTime dateTime, string? format, IFormatProvider? provider, TimeSpan offset);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryFormat")]
        private static extern bool UpstreamTryFormat<TChar>([UnsafeAccessorType(Type)] object? type, DateTime dateTime, Span<TChar> destination, out int charsWritten,
            ReadOnlySpan<char> format, IFormatProvider? provider) where TChar : unmanaged;

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryFormat")]
        private static extern bool UpstreamTryFormat<TChar>([UnsafeAccessorType(Type)] object? type, DateTime dateTime, Span<TChar> destination, out int charsWritten,
            ReadOnlySpan<char> format, IFormatProvider? provider, TimeSpan offset) where TChar : unmanaged;

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetAllDateTimes")]
        private static extern string[] UpstreamGetAllDateTimes([UnsafeAccessorType(Type)] object? type, DateTime dateTime, DateTimeFormatInfo dtfi);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetAllDateTimes")]
        private static extern string[] UpstreamGetAllDateTimes([UnsafeAccessorType(Type)] object? type, DateTime dateTime, char format, DateTimeFormatInfo dtfi);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "IsValidCustomDateOnlyFormat")]
        private static extern bool UpstreamIsValidCustomDateOnlyFormat([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> format, bool throwOnError);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "IsValidCustomTimeOnlyFormat")]
        private static extern bool UpstreamIsValidCustomTimeOnlyFormat([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> format, bool throwOnError);

        internal static string Format(DateTime dateTime, string? format, IFormatProvider? provider) => UpstreamFormat(null, dateTime, format, provider);

        internal static string Format(DateTime dateTime, string? format, IFormatProvider? provider, TimeSpan offset) =>
            UpstreamFormat(null, dateTime, format, provider, offset);

        internal static bool TryFormat(DateTime dateTime, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            UpstreamTryFormat(null, dateTime, destination, out charsWritten, format, provider);

        internal static bool TryFormat(DateTime dateTime, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider, TimeSpan offset) =>
            UpstreamTryFormat(null, dateTime, destination, out charsWritten, format, provider, offset);

        internal static bool TryFormat(DateTime dateTime, Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            UpstreamTryFormat(null, dateTime, destination, out bytesWritten, format, provider);

        internal static bool TryFormat(DateTime dateTime, Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider, TimeSpan offset) =>
            UpstreamTryFormat(null, dateTime, destination, out bytesWritten, format, provider, offset);

        internal static string[] GetAllDateTimes(DateTime dateTime, DateTimeFormatInfo dtfi) => UpstreamGetAllDateTimes(null, dateTime, dtfi);

        internal static string[] GetAllDateTimes(DateTime dateTime, char format, DateTimeFormatInfo dtfi) => UpstreamGetAllDateTimes(null, dateTime, format, dtfi);

        internal static bool IsValidCustomDateOnlyFormat(ReadOnlySpan<char> format, bool throwOnError) => UpstreamIsValidCustomDateOnlyFormat(null, format, throwOnError);

        internal static bool IsValidCustomTimeOnlyFormat(ReadOnlySpan<char> format, bool throwOnError) => UpstreamIsValidCustomTimeOnlyFormat(null, format, throwOnError);
    }

    internal static class DateTimeParse
    {
        private const string Type = "CsharpcCoreLib.Upstream.System.DateTimeParse, CsharpcUpstream";

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Parse")]
        private static extern DateTime UpstreamParse([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Parse")]
        private static extern DateTime UpstreamParse([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles, out TimeSpan offset);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryParse")]
        private static extern bool UpstreamTryParse([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles, out DateTime result);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryParse")]
        private static extern bool UpstreamTryParse([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles, out DateTime result, out TimeSpan offset);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseExact")]
        private static extern DateTime UpstreamParseExact([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseExact")]
        private static extern DateTime UpstreamParseExact([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style, out TimeSpan offset);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryParseExact")]
        private static extern bool UpstreamTryParseExact([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryParseExact")]
        private static extern bool UpstreamTryParseExact([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result, out TimeSpan offset);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseExactMultiple")]
        private static extern DateTime UpstreamParseExactMultiple([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseExactMultiple")]
        private static extern DateTime UpstreamParseExactMultiple([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style, out TimeSpan offset);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryParseExactMultiple")]
        private static extern bool UpstreamTryParseExactMultiple([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryParseExactMultiple")]
        private static extern bool UpstreamTryParseExactMultiple([UnsafeAccessorType(Type)] object? type, ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result, out TimeSpan offset);

        internal static DateTime Parse(ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles) => UpstreamParse(null, s, dtfi, styles);
        internal static DateTime Parse(ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles, out TimeSpan offset) => UpstreamParse(null, s, dtfi, styles, out offset);
        internal static bool TryParse(ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles, out DateTime result) => UpstreamTryParse(null, s, dtfi, styles, out result);
        internal static bool TryParse(ReadOnlySpan<char> s, DateTimeFormatInfo dtfi, DateTimeStyles styles, out DateTime result, out TimeSpan offset) =>
            UpstreamTryParse(null, s, dtfi, styles, out result, out offset);
        internal static DateTime ParseExact(ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style) => UpstreamParseExact(null, s, format, dtfi, style);
        internal static DateTime ParseExact(ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style, out TimeSpan offset) =>
            UpstreamParseExact(null, s, format, dtfi, style, out offset);
        internal static bool TryParseExact(ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result) =>
            UpstreamTryParseExact(null, s, format, dtfi, style, out result);
        internal static bool TryParseExact(ReadOnlySpan<char> s, ReadOnlySpan<char> format, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result, out TimeSpan offset) =>
            UpstreamTryParseExact(null, s, format, dtfi, style, out result, out offset);
        internal static DateTime ParseExactMultiple(ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style) =>
            UpstreamParseExactMultiple(null, s, formats, dtfi, style);
        internal static DateTime ParseExactMultiple(ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style, out TimeSpan offset) =>
            UpstreamParseExactMultiple(null, s, formats, dtfi, style, out offset);
        internal static bool TryParseExactMultiple(ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result) =>
            UpstreamTryParseExactMultiple(null, s, formats, dtfi, style, out result);
        internal static bool TryParseExactMultiple(ReadOnlySpan<char> s, string?[]? formats, DateTimeFormatInfo dtfi, DateTimeStyles style, out DateTime result, out TimeSpan offset) =>
            UpstreamTryParseExactMultiple(null, s, formats, dtfi, style, out result, out offset);
    }

    /// <summary>DateTimeFormatInfo's internal style check, which DateTime's parsing members call first.</summary>
    internal static class DateTimeFormatInfoInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ValidateStyles")]
        private static extern void UpstreamValidateStyles([UnsafeAccessorType("CsharpcCoreLib.Upstream.System.Globalization.DateTimeFormatInfo, CsharpcUpstream")] object? type,
            DateTimeStyles style, bool styles);

        internal static void ValidateStyles(DateTimeStyles style, bool styles = false) => UpstreamValidateStyles(null, style, styles);

        /// <summary>The culture's decimal separator for TimeSpan's "g" and "G" formats (its NumberFormatInfo's).</summary>
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DecimalSeparator")]
        internal static extern string DecimalSeparator(DateTimeFormatInfo dtfi);
    }

    /// <summary>DateOnly's and TimeOnly's ToString(format, provider), as DateOnly.cs and TimeOnly.cs do it (their parsing is hosted from IL).</summary>
    internal static class DateOnlyTimeOnlyCulture
    {
        private const string DateOFormat = "yyyy'-'MM'-'dd";
        private const string DateRFormat = "ddd, dd MMM yyyy";
        private const string TimeOFormat = "HH':'mm':'ss'.'fffffff";
        private const string TimeRFormat = "HH':'mm':'ss";

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
    }
}
