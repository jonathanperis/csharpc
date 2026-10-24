// The culture-sensitive string operations of System.String / System.Char / Comparer<string>, routed the
// way CoreLib routes them (String.Comparison.cs, String.Searching.cs, String.Manipulation.cs, Char.cs in
// dotnet/runtime): CurrentCulture* to CultureInfo.CurrentCulture's CompareInfo / TextInfo, InvariantCulture*
// to the invariant culture's, Ordinal* without globalization. CompareInfo and TextInfo are the port of .NET's
// (ICU, or ordinal operations in globalization-invariant mode).
#nullable disable
using System;
using System.Globalization;
using CompareInfo = CsharpcCoreLib.Globalization.CompareInfo;
using CultureInfo = CsharpcCoreLib.Globalization.CultureInfo;
using TextInfo = CsharpcCoreLib.Globalization.TextInfo;

namespace CsharpcCoreLib
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Globalization;
    using CompareInfo = CsharpcCoreLib.Globalization.CompareInfo;
    using CultureInfo = CsharpcCoreLib.Globalization.CultureInfo;
    using TextInfo = CsharpcCoreLib.Globalization.TextInfo;
    internal static class CultureStrings
    {
        private const string NotSupportedComparison = "The string comparison type passed in is currently not supported.";

        internal static bool IsOrdinal(StringComparison comparisonType) =>
            comparisonType == StringComparison.Ordinal || comparisonType == StringComparison.OrdinalIgnoreCase;

        /// <summary>The CompareInfo of a culture StringComparison (throws for unknown values, like .NET).</summary>
        /// <summary>CultureInfo.GetFormat(typeof(DateTimeFormatInfo)), exported to the runtime only for programs that
        /// use DateTimeFormatInfo (its method table is the trigger; see CultureInfo.GetFormat).</summary>
        [RuntimeExport("csrt_culture_get_format_dtfi", "_CN14CsharpcCoreLib13Globalization18DateTimeFormatInfo2MTE")]
        internal static object? GetFormatDateTimeFormat(CultureInfo culture, Type? formatType) =>
            formatType == typeof(DateTimeFormatInfo) ? culture.DateTimeFormat : null;

        internal static CompareInfo CompareInfoFor(StringComparison comparisonType)
        {
            switch (comparisonType)
            {
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                    return CultureInfo.CurrentCulture.CompareInfo;
                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return CompareInfo.Invariant;
                default:
                    throw new ArgumentException(NotSupportedComparison, "comparisonType");
            }
        }

        internal static CompareOptions OptionsFor(StringComparison comparisonType) =>
            ((int)comparisonType & 1) != 0 ? CompareOptions.IgnoreCase : CompareOptions.None;

        internal static void CheckStringComparison(StringComparison comparisonType)
        {
            if ((uint)comparisonType > (uint)StringComparison.OrdinalIgnoreCase)
                throw new ArgumentException(NotSupportedComparison, "comparisonType");
        }

        /// <summary>string.Compare(a, b[, ignoreCase]), string.CompareTo, Comparer&lt;string&gt;.Default.</summary>
        [RuntimeExport("csrt_string_compare_culture", "csrt_array_sort")]
        internal static int CompareCulture(string a, string b, bool ignoreCase) =>
            CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None);

        /// <summary>string.Compare(a, b, StringComparison) (formerly csrt_string_compare_with).</summary>
        internal static int CompareWith(string a, string b, int comparison)
        {
            StringComparison comparisonType = (StringComparison)comparison;
            switch (comparisonType)
            {
                case StringComparison.Ordinal:
                    return StringOps.CompareOrdinal(a, b);
                case StringComparison.OrdinalIgnoreCase:
                    return OrdinalCasing.Compare(a, b);
                default:
                    return CompareInfoFor(comparisonType).Compare(a, b, OptionsFor(comparisonType));
            }
        }

        internal static bool StartsWith(string s, string value, StringComparison comparisonType)
        {
            if (value == null) throw new ArgumentNullException("value");
            if ((object)s == (object)value || value.Length == 0)
            {
                CheckStringComparison(comparisonType);
                return true;
            }
            switch (comparisonType)
            {
                case StringComparison.Ordinal:
                    return value.Length <= s.Length && StringOps.EqualsOrdinal(s.AsSpan(0, value.Length), value.AsSpan());
                case StringComparison.OrdinalIgnoreCase:
                    return value.Length <= s.Length && OrdinalCasing.Compare(s.Substring(0, value.Length), value) == 0;
                default:
                    return CompareInfoFor(comparisonType).IsPrefix(s, value, OptionsFor(comparisonType));
            }
        }

        internal static bool EndsWith(string s, string value, StringComparison comparisonType)
        {
            if (value == null) throw new ArgumentNullException("value");
            if ((object)s == (object)value || value.Length == 0)
            {
                CheckStringComparison(comparisonType);
                return true;
            }
            switch (comparisonType)
            {
                case StringComparison.Ordinal:
                    return value.Length <= s.Length && StringOps.EqualsOrdinal(s.AsSpan(s.Length - value.Length), value.AsSpan());
                case StringComparison.OrdinalIgnoreCase:
                    return value.Length <= s.Length && OrdinalCasing.Compare(s.Substring(s.Length - value.Length), value) == 0;
                default:
                    return CompareInfoFor(comparisonType).IsSuffix(s, value, OptionsFor(comparisonType));
            }
        }

        /// <summary>string.IndexOf(value, startIndex, count, comparisonType): .NET's validation and dispatch.</summary>
        internal static int IndexOf(string s, string value, int startIndex, int count, StringComparison comparisonType)
        {
            switch (comparisonType)
            {
                case StringComparison.Ordinal:
                case StringComparison.OrdinalIgnoreCase:
                {
                    // Ordinal.IndexOf
                    if (value == null) throw new ArgumentNullException("value");
                    if ((uint)startIndex > (uint)s.Length)
                        throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
                    if ((uint)count > (uint)(s.Length - startIndex))
                        throw new ArgumentOutOfRangeException("count", "Count must be positive and count must refer to a location within the string/array/collection.");
                    ReadOnlySpan<char> span = s.AsSpan(startIndex, count);
                    int result = comparisonType == StringComparison.OrdinalIgnoreCase
                        ? Globalization.Ordinal.IndexOfOrdinalIgnoreCase(span, value.AsSpan())
                        : StringOps.IndexOfOrdinal(span, value.AsSpan(), 0);
                    return result >= 0 ? startIndex + result : result;
                }
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return CompareInfoFor(comparisonType).IndexOf(s, value, startIndex, count, OptionsFor(comparisonType));
                default:
                    throw value == null ? new ArgumentNullException("value") : new ArgumentException(NotSupportedComparison, "comparisonType");
            }
        }

        /// <summary>string.LastIndexOf(value, startIndex, count, comparisonType): CompareInfo checks the arguments.</summary>
        internal static int LastIndexOf(string s, string value, int startIndex, int count, StringComparison comparisonType)
        {
            switch (comparisonType)
            {
                case StringComparison.Ordinal:
                    return CompareInfo.Invariant.LastIndexOf(s, value, startIndex, count, CompareOptions.Ordinal);
                case StringComparison.OrdinalIgnoreCase:
                    return CompareInfo.Invariant.LastIndexOf(s, value, startIndex, count, CompareOptions.OrdinalIgnoreCase);
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return CompareInfoFor(comparisonType).LastIndexOf(s, value, startIndex, count, OptionsFor(comparisonType));
                default:
                    throw value == null ? new ArgumentNullException("value") : new ArgumentException(NotSupportedComparison, "comparisonType");
            }
        }

        internal static int IndexOf(string s, char value, StringComparison comparisonType)
        {
            switch (comparisonType)
            {
                case StringComparison.Ordinal:
                    return StringOps.IndexOf(s, value, 0);
                case StringComparison.OrdinalIgnoreCase:
                    return Globalization.Ordinal.IndexOfOrdinalIgnoreCase(s.AsSpan(), value.ToString().AsSpan());
                default:
                    return CompareInfoFor(comparisonType).IndexOf(s, value, OptionsFor(comparisonType));
            }
        }

        // ---- casing ------------------------------------------------------------------------------

        internal static string ToUpper(string s) => CultureInfo.CurrentCulture.TextInfo.ToUpper(s);
        internal static string ToLower(string s) => CultureInfo.CurrentCulture.TextInfo.ToLower(s);
        internal static string ToUpper(string s, System.Globalization.CultureInfo culture) => Culture(culture).TextInfo.ToUpper(s);
        internal static string ToLower(string s, System.Globalization.CultureInfo culture) => Culture(culture).TextInfo.ToLower(s);
        internal static string ToUpperInvariant(string s) => TextInfo.Invariant.ToUpper(s);
        internal static string ToLowerInvariant(string s) => TextInfo.Invariant.ToLower(s);

        internal static char ToUpper(char c) => CultureInfo.CurrentCulture.TextInfo.ToUpper(c);
        internal static char ToLower(char c) => CultureInfo.CurrentCulture.TextInfo.ToLower(c);
        internal static char ToUpper(char c, System.Globalization.CultureInfo culture)
        {
            if (culture == null) throw new ArgumentNullException("culture");
            return Culture(culture).TextInfo.ToUpper(c);
        }
        internal static char ToLower(char c, System.Globalization.CultureInfo culture)
        {
            if (culture == null) throw new ArgumentNullException("culture");
            return Culture(culture).TextInfo.ToLower(c);
        }
        internal static char ToUpperInvariant(char c) => TextInfo.ToUpperInvariant(c);
        internal static char ToLowerInvariant(char c) => TextInfo.ToLowerInvariant(c);

        // Thread.CurrentCulture / CurrentUICulture (Thread.cs): the calling thread's culture.
        internal static System.Globalization.CultureInfo GetThreadCulture(bool isCurrentThread, bool ui)
        {
            if (!isCurrentThread)
                throw new InvalidOperationException("This operation must be performed on the same thread as that represented by the Thread instance.");
            return (System.Globalization.CultureInfo)(object)(ui ? CultureInfo.CurrentUICulture : CultureInfo.CurrentCulture);
        }

        internal static void SetThreadCulture(bool isCurrentThread, System.Globalization.CultureInfo value, bool ui)
        {
            // .NET keeps the culture of an unstarted thread until it starts.
            if (!isCurrentThread) throw new NotSupportedException("Setting the culture of another thread is not supported by csharpc yet.");
            if (ui) CultureInfo.CurrentUICulture = (CultureInfo)(object)value;
            else CultureInfo.CurrentCulture = (CultureInfo)(object)value;
        }

        /// <summary>A CultureInfo argument (the real type in signatures, the corelib's object at run time); null is the current culture.</summary>
        private static CultureInfo Culture(System.Globalization.CultureInfo culture) =>
            culture == null ? CultureInfo.CurrentCulture : (CultureInfo)(object)culture;
    }
}
