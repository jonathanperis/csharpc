// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Stand-ins for the CoreLib internals the System.Globalization port calls (dotnet/runtime release/11.0,
// commit bc123d0bd173; see THIRD-PARTY-NOTICES.md): Number's digit writers, Ordinal's ignore-case
// searches, CharUnicodeInfo's casing/category lookups, DateTime's internal accessors and the AppContext
// switches the globalization code reads. Behavior follows the upstream members they replace.
#nullable enable annotations

using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Globalization;
    internal static partial class Number
    {
        internal static unsafe void WriteTwoDigits(uint value, char* ptr)
        {
            ptr[0] = (char)('0' + value / 10);
            ptr[1] = (char)('0' + value % 10);
        }

        internal static unsafe void WriteFourDigits(uint value, char* ptr)
        {
            WriteTwoDigits(value / 100, ptr);
            WriteTwoDigits(value % 100, ptr + 2);
        }

        /// <summary>Writes exactly `count` digits of `value` (zero-padded on the left) at `ptr`.</summary>
        internal static unsafe void WriteDigits(uint value, char* ptr, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                ptr[i] = (char)('0' + value % 10);
                value /= 10;
            }
        }

        /// <summary>Writes `value` backwards ending before `bufferEnd`, at least `digits` digits; returns the first char.</summary>
        internal static unsafe char* UInt32ToDecChars(char* bufferEnd, uint value, int digits)
        {
            while (--digits >= 0 || value != 0)
            {
                *(--bufferEnd) = (char)('0' + value % 10);
                value /= 10;
            }
            return bufferEnd;
        }
    }
}

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Globalization;
    /// <summary>The AppContext switches CoreLib's globalization reads (LocalAppContextSwitches); all default to false.</summary>
    internal static class LocalAppContextSwitches
    {
        internal const bool EnforceJapaneseEraYearRanges = false;
        internal const bool EnforceLegacyJapaneseDateParsing = false;
        internal const bool FormatJapaneseFirstYearAsANumber = false;
    }

    /// <summary>System.Globalization.Ordinal's ignore-case operations, on csharpc's ordinal casing (corelib OrdinalCasing.cs).</summary>
    internal static class Ordinal
    {
        private static int CodePointAt(ReadOnlySpan<char> s, int i, out int width)
        {
            char c = s[i];
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
            {
                width = 2;
                return 0x10000 + ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00);
            }
            width = 1;
            return c;
        }

        internal static int CompareStringIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char x = a[i], y = b[j];
                if (x < 0x80 && y < 0x80)
                {
                    if (x != y)
                    {
                        int ux = (uint)(x - 'a') <= 'z' - 'a' ? x - 0x20 : x;
                        int uy = (uint)(y - 'a') <= 'z' - 'a' ? y - 0x20 : y;
                        if (ux != uy) return ux - uy;
                    }
                    i++; j++;
                    continue;
                }
                int p = CsharpcCoreLib.OrdinalCasing.ToUpper(CodePointAt(a, i, out int wa));
                int q = CsharpcCoreLib.OrdinalCasing.ToUpper(CodePointAt(b, j, out int wb));
                if (p != q) return p - q;
                i += wa; j += wb;
            }
            return (a.Length - i) - (b.Length - j);
        }

        internal static bool EqualsIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
            a.Length == b.Length && CompareStringIgnoreCase(a, b) == 0;

        internal static int IndexOfOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
        {
            if (value.Length == 0) return 0;
            for (int i = 0; i + value.Length <= source.Length; i++)
                if (EqualsIgnoreCase(source.Slice(i, value.Length), value)) return i;
            return -1;
        }

        internal static int LastIndexOfOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
        {
            if (value.Length == 0) return source.Length;
            for (int i = source.Length - value.Length; i >= 0; i--)
                if (EqualsIgnoreCase(source.Slice(i, value.Length), value)) return i;
            return -1;
        }

        internal static int GetHashCodeOrdinalIgnoreCase(ReadOnlySpan<char> source) =>
            CsharpcCoreLib.OrdinalCasing.ToUpperOrdinal(source.ToString()).GetHashCode();
    }

    /// <summary>The internal members of CharUnicodeInfo the port calls (csharpc's Unicode tables, .NET's data).</summary>
    internal static class CharUnicodeInfo
    {
        internal static char ToUpper(char c) => CharOps.ToUpperTable(c);
        internal static char ToLower(char c) => CharOps.ToLowerTable(c);
        internal static uint ToUpper(uint cp) => cp < 0x10000 ? CharOps.ToUpperTable((char)cp) : (uint)CsharpcCoreLib.OrdinalCasing.ToUpper((int)cp);
        internal static uint ToLower(uint cp) => cp < 0x10000 ? CharOps.ToLowerTable((char)cp) : (uint)CsharpcCoreLib.OrdinalCasing.ToLower((int)cp);

        internal static UnicodeCategory GetUnicodeCategory(char c) => CharOps.GetUnicodeCategory(c);

        internal static UnicodeCategory GetUnicodeCategory(string s, int index) => GetUnicodeCategoryInternal(s, index, out _);

        internal static int GetDecimalDigitValue(string s, int index) => CharUnicodeInfoExtras.GetDecimalDigitValue(s, index);

        internal static UnicodeCategory GetUnicodeCategoryInternal(string value, int index, out int charLength)
        {
            char c = value[index];
            if (c >= 0xD800 && c <= 0xDBFF && index + 1 < value.Length && value[index + 1] >= 0xDC00 && value[index + 1] <= 0xDFFF)
            {
                // csharpc: the corelib's Unicode tables cover the BMP only.
                throw new NotSupportedException("The Unicode category of a supplementary-plane character is not supported by csharpc yet.");
            }
            charLength = 1;
            return CharOps.GetUnicodeCategory(c);
        }

        internal static UnicodeCategory InternalGetUnicodeCategory(string value, int index) =>
            GetUnicodeCategoryInternal(value, index, out _);
    }

    internal enum TimeZoneInfoOptions
    {
        None = 1,
        NoThrowOnInvalidTime = 2,
    }

    /// <summary>The members of TimeZoneInfo the formatting/parsing code uses, on csharpc's local-zone code (DateTimeOps).</summary>
    internal sealed class TimeZoneInfo
    {
        private static TimeZoneInfo? s_local;
        private readonly bool _utc;

        private TimeZoneInfo(bool utc) => _utc = utc;

        internal static TimeZoneInfo Local => s_local ??= new TimeZoneInfo(false);

        internal TimeSpan GetUtcOffset(DateTime dateTime) => GetUtcOffset(dateTime, TimeZoneInfoOptions.NoThrowOnInvalidTime);

        internal TimeSpan GetUtcOffset(DateTime dateTime, TimeZoneInfoOptions flags) =>
            _utc ? TimeSpan.Zero : new TimeSpan(DateTimeOps.LocalUtcOffsetTicks(CoreIntrinsics.DateTimeData(dateTime)));

        internal static TimeSpan GetLocalUtcOffset(DateTime dateTime, TimeZoneInfoOptions flags) => Local.GetUtcOffset(dateTime, flags);

        internal static TimeSpan GetUtcOffsetFromUtc(DateTime time, TimeZoneInfo zone, out bool isDaylightSavings, out bool isAmbiguousLocalDst)
        {
            isDaylightSavings = false;
            long offset = DateTimeOps.OffsetFromUtcTicks(time.Ticks, out isAmbiguousLocalDst);
            return new TimeSpan(offset);
        }
    }

    /// <summary>DateTime / DateTimeOffset internals (constants, TryCreate, the Local-with-ambiguity constructor).</summary>
    internal static class DateTimeHelpers
    {
        internal const long MinTicks = 0;
        internal const long MaxTicks = 3155378975999999999L;
        internal const long MinOffset = -TimeSpan.TicksPerHour * 14;
        internal const long MaxOffset = TimeSpan.TicksPerHour * 14;

        internal static bool TryCreate(int year, int month, int day, int hour, int minute, int second, int millisecond, out DateTime result)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
                || (uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60 || (uint)millisecond >= 1000)
            {
                result = default;
                return false;
            }
            result = new DateTime(year, month, day, hour, minute, second, millisecond);
            return true;
        }

        /// <summary>TimeSpanParse.Pow10UpToMaxFractionDigits: 10^pow for pow in 0..7.</summary>
        internal static int Pow10UpToMaxFractionDigits(int pow)
        {
            int result = 1;
            for (int i = 0; i < pow; i++) result *= 10;
            return result;
        }

        internal static DateTime CreateLocal(long ticks, bool isAmbiguousDst) => CoreIntrinsics.MakeDateTime(DateTimeOps.MakeLocal(ticks, isAmbiguousDst));
    }

    /// <summary>System.Text.UnicodeUtility (CoreLib-internal).</summary>
    internal static class UnicodeUtility
    {
        internal static bool IsAsciiCodePoint(uint value) => value <= 0x7Fu;

        internal static bool IsBmpCodePoint(uint value) => value <= 0xFFFFu;

        internal static uint GetScalarFromUtf16SurrogatePair(uint highSurrogateCodePoint, uint lowSurrogateCodePoint) =>
            (highSurrogateCodePoint << 10) + lowSurrogateCodePoint - ((0xD800u << 10) + 0xDC00u - (1u << 16));

        internal static void GetUtf16SurrogatesFromSupplementaryPlaneScalar(uint value, out char highSurrogateCodePoint, out char lowSurrogateCodePoint)
        {
            highSurrogateCodePoint = (char)((value + ((0xD800u - 0x40u) << 10)) >> 10);
            lowSurrogateCodePoint = (char)((value & 0x3FFu) + 0xDC00u);
        }
    }

    /// <summary>System.Text.Unicode.Utf16Utility (CoreLib-internal): two UTF-16 chars packed in a uint.</summary>
    internal static class Utf16Utility
    {
        internal static bool AllCharsInUInt32AreAscii(uint value) => (value & ~0x007F_007Fu) == 0;

        internal static bool UInt32ContainsAnyLowercaseAsciiChar(uint value)
        {
            uint lowerIndicator = value + 0x0080_0080u - 0x0061_0061u;
            uint upperIndicator = value + 0x0080_0080u - 0x007B_007Bu;
            return ((lowerIndicator ^ upperIndicator) & 0x0080_0080u) != 0;
        }

        internal static bool UInt32ContainsAnyUppercaseAsciiChar(uint value)
        {
            uint lowerIndicator = value + 0x0080_0080u - 0x0041_0041u;
            uint upperIndicator = value + 0x0080_0080u - 0x005B_005Bu;
            return ((lowerIndicator ^ upperIndicator) & 0x0080_0080u) != 0;
        }
    }

    /// <summary>
    /// System.Globalization.OrdinalCasing (ICU's simple case mapping per char in .NET, OrdinalCasing.Icu.cs);
    /// csharpc maps through its own tables of .NET's Unicode data (corelib OrdinalCasing.cs).
    /// </summary>
    internal static class OrdinalCasing
    {
        internal static char ToUpper(char c) => (char)CsharpcCoreLib.OrdinalCasing.ToUpper(c);

        internal static char ToLower(char c) => c == 'İ' || c == 'ϴ' || c == 'ẞ' || c == 'Ω' || c == 'K' || c == 'Å'
            ? c // InitOrdinalLowerCasingPage keeps these (their lower case would leave the ordinal upper-case class)
            : CharOps.ToLowerTable(c);
    }

    /// <summary>System.Text.Ascii.ToUpper/ToLower(source, destination, out charsConsumed) (UTF-16).</summary>
    internal static class Ascii
    {
        internal static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten) =>
            ChangeCase(source, destination, out charsWritten, true);

        internal static System.Buffers.OperationStatus ToLower(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten) =>
            ChangeCase(source, destination, out charsWritten, false);

        private static System.Buffers.OperationStatus ChangeCase(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten, bool toUpper)
        {
            int n = source.Length < destination.Length ? source.Length : destination.Length;
            int i = 0;
            for (; i < n; i++)
            {
                char c = source[i];
                if (c >= 0x80) break;
                if (toUpper) { if ((uint)(c - 'a') <= 'z' - 'a') c = (char)(c - 0x20); }
                else if ((uint)(c - 'A') <= 'Z' - 'A') c = (char)(c + 0x20);
                destination[i] = c;
            }
            charsWritten = i;
            if (i < source.Length && i < destination.Length) return System.Buffers.OperationStatus.InvalidData;
            return i == source.Length ? System.Buffers.OperationStatus.Done : System.Buffers.OperationStatus.DestinationTooSmall;
        }
    }

    internal static class GlobalizationExtensions
    {
        /// <summary>ReadOnlySpan&lt;char&gt; == ReadOnlySpan&lt;char&gt;: same start and length.</summary>
        internal static unsafe bool SameSpan(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            if (a.Length != b.Length) return false;
            fixed (char* pa = a)
            fixed (char* pb = b)
            {
                return pa == pb;
            }
        }

        /// <summary>A hash of sort-key bytes (CompareInfo.GetHashCode; .NET's Marvin hash is randomized per process anyway).</summary>
        internal static int HashBytes(ReadOnlySpan<byte> bytes)
        {
            uint hash = 2166136261;
            for (int i = 0; i < bytes.Length; i++) hash = (hash ^ bytes[i]) * 16777619;
            return (int)hash;
        }

        // DateTime.TryAddTicks (CoreLib-internal).
        internal static bool TryAddTicks(this DateTime dt, long value, out DateTime result)
        {
            long ticks = dt.Ticks;
            if (value > DateTimeHelpers.MaxTicks - ticks || value < DateTimeHelpers.MinTicks - ticks)
            {
                result = default;
                return false;
            }
            result = new DateTime(ticks + value, dt.Kind);
            return true;
        }

        // TimeOnly.ToDateTime (CoreLib-internal).
        internal static DateTime ToDateTime(this TimeOnly time) => new DateTime(time.Ticks);

        // string.TryGetSpan (CoreLib-internal): the slice if the range is valid.
        internal static bool TryGetSpan(this string s, int startIndex, int count, out ReadOnlySpan<char> slice)
        {
            if ((uint)startIndex > (uint)s.Length || (uint)count > (uint)(s.Length - startIndex))
            {
                slice = default;
                return false;
            }
            slice = s.AsSpan(startIndex, count);
            return true;
        }

        internal static bool StartsWithOrdinalIgnoreCase(this ReadOnlySpan<char> source, ReadOnlySpan<char> value) =>
            value.Length <= source.Length && Ordinal.EqualsIgnoreCase(source.Slice(0, value.Length), value);

        internal static bool EndsWithOrdinalIgnoreCase(this ReadOnlySpan<char> source, ReadOnlySpan<char> value) =>
            value.Length <= source.Length && Ordinal.EqualsIgnoreCase(source.Slice(source.Length - value.Length), value);

        // DateTime's internal accessors (DateTime.GetDate/GetTime/GetTimePrecise).
        internal static void GetDate(this DateTime dt, out int year, out int month, out int day)
        {
            year = dt.Year;
            month = dt.Month;
            day = dt.Day;
        }

        internal static void GetTime(this DateTime dt, out int hour, out int minute, out int second)
        {
            long seconds = dt.Ticks / TimeSpan.TicksPerSecond;
            long minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            long hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        internal static void GetTimePrecise(this DateTime dt, out int hour, out int minute, out int second, out int tick)
        {
            long ticks = dt.Ticks;
            long seconds = ticks / TimeSpan.TicksPerSecond;
            tick = (int)(ticks - (seconds * TimeSpan.TicksPerSecond));
            long minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            long hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }
    }
}
