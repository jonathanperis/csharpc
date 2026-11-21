// The culture-sensitive comparisons csharpc's runtime entry points make (string.CompareTo, Comparer<string>,
// Array.Sort of strings), routed the way CoreLib routes them (String.Comparison.cs in dotnet/runtime): CurrentCulture*
// to CultureInfo.CurrentCulture's CompareInfo, InvariantCulture* to the invariant culture's, Ordinal* without
// globalization. String's and Char's own members (searching, casing, trimming, splitting) come from CoreLib's IL
// (src/Csharpc/upstream-types/System.String.txt). CompareInfo and TextInfo are the port of .NET's
// (ICU, or ordinal operations in globalization-invariant mode), served from the framework's IL (#426).
#nullable disable
using System;
using System.Globalization;
using CompareInfo = System.Globalization.CompareInfo;
using CultureInfo = System.Globalization.CultureInfo;
using TextInfo = System.Globalization.TextInfo;

namespace CsharpcCoreLib
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    using System.Globalization;
    using CompareInfo = System.Globalization.CompareInfo;
    using CultureInfo = System.Globalization.CultureInfo;
    using TextInfo = System.Globalization.TextInfo;
    internal static class CultureStrings
    {
        private const string NotSupportedComparison = "The string comparison type passed in is currently not supported.";

        /// <summary>The CompareInfo of a culture StringComparison (throws for unknown values, like .NET).</summary>
        internal static CompareInfo CompareInfoFor(StringComparison comparisonType)
        {
            switch (comparisonType)
            {
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                    return CultureInfo.CurrentCulture.CompareInfo;
                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return CultureInfo.InvariantCulture.CompareInfo;
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
                    return string.CompareOrdinal(a, b);
                case StringComparison.OrdinalIgnoreCase:
                    return OrdinalCasing.Compare(a, b);
                default:
                    return CompareInfoFor(comparisonType).Compare(a, b, OptionsFor(comparisonType));
            }
        }

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
    }
}
