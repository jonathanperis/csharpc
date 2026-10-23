// Static System.String members that need generics (the rest of string is src/runtime/strings.c intrinsics).
#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace CsharpcCoreLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal sealed class ShadowMembersAttribute : Attribute
    {
        public ShadowMembersAttribute(string name) { }
    }

    [ShadowMembers("System.String")]
    internal static class StringExtras
    {
        public static string Join<T>(string separator, IEnumerable<T> values)
        {
            if (values == null) ThrowHelper.ArgumentNull("values");
            var sb = new StringBuilder();
            bool first = true;
            foreach (T value in values)
            {
                if (!first) sb.Append(separator);
                first = false;
                if (value != null) sb.Append(value.ToString());
            }
            return sb.ToString();
        }

        public static string Join(string separator, IEnumerable<string> values)
        {
            if (values == null) ThrowHelper.ArgumentNull("values");
            var sb = new StringBuilder();
            bool first = true;
            foreach (string value in values)
            {
                if (!first) sb.Append(separator);
                first = false;
                sb.Append(value);
            }
            return sb.ToString();
        }

        public static string Join(char separator, string[] value) => string.Join(separator.ToString(), value);
        public static string Join(char separator, IEnumerable<string> values) => Join(separator.ToString(), values);
        public static string Join<T>(char separator, IEnumerable<T> values) => Join(separator.ToString(), values);
        public static string Join(char separator, object[] values) => Join(separator.ToString(), values);

        public static string Join(string separator, object[] values)
        {
            if (values == null) ThrowHelper.ArgumentNull("values");
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(separator);
                if (values[i] != null) sb.Append(values[i].ToString());
            }
            return sb.ToString();
        }

        public static string Concat<T>(IEnumerable<T> values)
        {
            var sb = new StringBuilder();
            foreach (T value in values)
                if (value != null) sb.Append(value.ToString());
            return sb.ToString();
        }

        public static string Concat(IEnumerable<string> values)
        {
            var sb = new StringBuilder();
            foreach (string value in values) sb.Append(value);
            return sb.ToString();
        }

        public static string Concat(string[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++) sb.Append(values[i]);
            return sb.ToString();
        }

        public static int Compare(string strA, string strB) => CoreIntrinsics.Compare(strA, strB);

        public static string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1) => ConcatSpans(str0, str1, default, default);
        public static string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1, ReadOnlySpan<char> str2) => ConcatSpans(str0, str1, str2, default);
        public static string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1, ReadOnlySpan<char> str2, ReadOnlySpan<char> str3) =>
            ConcatSpans(str0, str1, str2, str3);

        private static string ConcatSpans(ReadOnlySpan<char> a, ReadOnlySpan<char> b, ReadOnlySpan<char> c, ReadOnlySpan<char> d)
        {
            long length = (long)a.Length + b.Length + c.Length + d.Length;
            if (length == 0) return "";
            if (length > int.MaxValue) throw new OutOfMemoryException();
            string result = CoreIntrinsics.FastAllocateString((int)length);
            Span<char> dst = CoreIntrinsics.StringChars(result);
            a.CopyTo(dst);
            b.CopyTo(dst.Slice(a.Length));
            c.CopyTo(dst.Slice(a.Length + b.Length));
            d.CopyTo(dst.Slice(a.Length + b.Length + c.Length));
            return result;
        }

        public static int GetHashCode(ReadOnlySpan<char> value) => value.ToString().GetHashCode();

        public static int Compare(string strA, string strB, System.Globalization.CultureInfo culture, System.Globalization.CompareOptions options)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));
            return ((CsharpcCoreLib.Globalization.CultureInfo)(object)culture).CompareInfo.Compare(strA, strB, options);
        }

        public static int Compare(string strA, string strB, bool ignoreCase, System.Globalization.CultureInfo culture)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));
            return ((CsharpcCoreLib.Globalization.CultureInfo)(object)culture).CompareInfo.Compare(strA, strB,
                ignoreCase ? System.Globalization.CompareOptions.IgnoreCase : System.Globalization.CompareOptions.None);
        }

        public static int Compare(string strA, int indexA, string strB, int indexB, int length, System.Globalization.CultureInfo culture, System.Globalization.CompareOptions options)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));
            int lengthA = length, lengthB = length;
            if (strA != null) lengthA = Math.Min(lengthA, strA.Length - indexA);
            if (strB != null) lengthB = Math.Min(lengthB, strB.Length - indexB);
            return ((CsharpcCoreLib.Globalization.CultureInfo)(object)culture).CompareInfo.Compare(strA, indexA, lengthA, strB, indexB, lengthB, options);
        }

        public static int Compare(string strA, int indexA, string strB, int indexB, int length, bool ignoreCase, System.Globalization.CultureInfo culture) =>
            Compare(strA, indexA, strB, indexB, length, culture, ignoreCase ? System.Globalization.CompareOptions.IgnoreCase : System.Globalization.CompareOptions.None);

        // string.Create(provider, $"..."): the handler (InterpolatedStringHandler.cs) formatted with the provider.
        public static string Create(IFormatProvider provider, ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        public static string Create(IFormatProvider provider, Span<char> initialBuffer, ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        // string.Format with an IFormatProvider (the provider-less forms are codegen intrinsics).
        public static string Format(IFormatProvider provider, string format, object arg0) => Number.FormatCompositeWith(provider, format, new[] { arg0 });
        public static string Format(IFormatProvider provider, string format, object arg0, object arg1) => Number.FormatCompositeWith(provider, format, new[] { arg0, arg1 });
        public static string Format(IFormatProvider provider, string format, object arg0, object arg1, object arg2) =>
            Number.FormatCompositeWith(provider, format, new[] { arg0, arg1, arg2 });
        public static string Format(IFormatProvider provider, string format, params object[] args)
        {
            if (args == null) throw new ArgumentNullException(format == null ? "format" : "args");
            return Number.FormatCompositeWith(provider, format, args);
        }
        public static string Format(IFormatProvider provider, string format, params ReadOnlySpan<object> args) => Number.FormatCompositeWith(provider, format, args.ToArray());
    }
}

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Double")]
    internal static class DoubleExtras
    {
        public static bool IsNaN(double d) => d != d;
        public static bool IsInfinity(double d) => d == double.PositiveInfinity || d == double.NegativeInfinity;
        public static bool IsPositiveInfinity(double d) => d == double.PositiveInfinity;
        public static bool IsNegativeInfinity(double d) => d == double.NegativeInfinity;
        public static bool IsFinite(double d) => d == d && !IsInfinity(d);
        public static bool IsNegative(double d) => d < 0 || (d == 0 && 1 / d < 0);
        public static bool IsInteger(double d) => IsFinite(d) && d == Math.Floor(d);
    }

    [ShadowMembers("System.Single")]
    internal static class SingleExtras
    {
        public static bool IsNaN(float f) => f != f;
        public static bool IsInfinity(float f) => f == float.PositiveInfinity || f == float.NegativeInfinity;
        public static bool IsPositiveInfinity(float f) => f == float.PositiveInfinity;
        public static bool IsNegativeInfinity(float f) => f == float.NegativeInfinity;
        public static bool IsFinite(float f) => f == f && !IsInfinity(f);
    }

    [ShadowMembers("System.Math")]
    internal static class MathExtras
    {
        public static (int Quotient, int Remainder) DivRem(int left, int right)
        {
            int quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        public static (long Quotient, long Remainder) DivRem(long left, long right)
        {
            long quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        public static double CopySign(double x, double y) =>
            CoreIntrinsics.BitsToDouble((CoreIntrinsics.DoubleToBits(x) & long.MaxValue) | (CoreIntrinsics.DoubleToBits(y) & long.MinValue));

        /// <summary>x - y * round-half-even(x / y), with .NET's algorithm (its sign and NaN rules).</summary>
        public static double IEEERemainder(double x, double y)
        {
            if (double.IsNaN(x)) return x;
            if (double.IsNaN(y)) return y;
            double regularMod = x % y;
            if (double.IsNaN(regularMod)) return double.NaN;
            if (regularMod == 0 && double.IsNegative(x)) return -0.0;
            double alternativeResult = regularMod - (Math.Abs(y) * Math.Sign(x));
            if (Math.Abs(alternativeResult) == Math.Abs(regularMod))
            {
                double divisionResult = x / y;
                double roundedResult = Math.Round(divisionResult);
                return Math.Abs(roundedResult) > Math.Abs(divisionResult) ? alternativeResult : regularMod;
            }
            return Math.Abs(alternativeResult) < Math.Abs(regularMod) ? alternativeResult : regularMod;
        }
    }
}

namespace CsharpcCoreLib
{
    using System;
    using System.Collections.Generic;

    // .NET 11's span aggregates. With C# 14 first-class span conversions, `array.Min()` / `array.Max()`
    // bind to these instead of System.Linq.Enumerable.
    [ShadowMembers("System.MemoryExtensions")]
    internal static class MemoryExtensionsExtras
    {
        public static int SequenceCompareTo<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> other) where T : IComparable<T>
        {
            int n = span.Length < other.Length ? span.Length : other.Length;
            // .NET's SpanHelpers.SequenceCompareTo: chars and bytes return the difference of the first
            // differing elements, then of the lengths; other types their CompareTo.
            bool difference = typeof(T) == typeof(char) || typeof(T) == typeof(byte);
            for (int i = 0; i < n; i++)
            {
                int c = difference
                    ? (typeof(T) == typeof(char) ? (char)(object)span[i] - (char)(object)other[i] : (byte)(object)span[i] - (byte)(object)other[i])
                    : CoreIntrinsics.Compare(span[i], other[i]);
                if (c != 0) return c;
            }
            return difference ? span.Length - other.Length : span.Length.CompareTo(other.Length);
        }

        public static bool ContainsAnyExcept<T>(ReadOnlySpan<T> span, T value) where T : IEquatable<T>
        {
            for (int i = 0; i < span.Length; i++)
                if (!CoreIntrinsics.AreEqual(span[i], value)) return true;
            return false;
        }

        public static bool ContainsAny<T>(ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T>
        {
            for (int i = 0; i < span.Length; i++)
                if (CoreIntrinsics.AreEqual(span[i], value0) || CoreIntrinsics.AreEqual(span[i], value1)) return true;
            return false;
        }

        public static T Min<T>(ReadOnlySpan<T> span) => MinMax(span, null, 1);
        public static T Max<T>(ReadOnlySpan<T> span) => MinMax(span, null, -1);
        public static T Min<T>(ReadOnlySpan<T> span, IComparer<T> comparer) => MinMax(span, comparer, 1);
        public static T Max<T>(ReadOnlySpan<T> span, IComparer<T> comparer) => MinMax(span, comparer, -1);

        private static T MinMax<T>(ReadOnlySpan<T> span, IComparer<T> comparer, int sign)
        {
            // MemoryExtensions' wording ends with a period (Enumerable's doesn't).
            if (span.Length == 0) throw new InvalidOperationException("Sequence contains no elements.");
            T best = span[0];
            for (int i = 1; i < span.Length; i++)
            {
                T v = span[i];
                int c = comparer != null ? comparer.Compare(best, v) : CoreIntrinsics.Compare(best, v);
                if (c * sign > 0) best = v;
            }
            return best;
        }

        public static int Count<T>(ReadOnlySpan<T> span, T value)
        {
            int n = 0;
            for (int i = 0; i < span.Length; i++) if (CoreIntrinsics.AreEqual(span[i], value)) n++;
            return n;
        }

        public static int Count<T>(Span<T> span, T value)
        {
            int n = 0;
            for (int i = 0; i < span.Length; i++) if (CoreIntrinsics.AreEqual(span[i], value)) n++;
            return n;
        }

        public static int Count<T>(ReadOnlySpan<T> span, T value, IEqualityComparer<T> comparer)
        {
            int n = 0;
            for (int i = 0; i < span.Length; i++)
                if (comparer != null ? comparer.Equals(span[i], value) : CoreIntrinsics.AreEqual(span[i], value)) n++;
            return n;
        }

        // Searching. Element equality is EqualityComparer<T>.Default's (IEquatable<T>), ranges compare with
        // Comparer<T>.Default (IComparable<T>), as in .NET.
        public static int IndexOfAny<T>(ReadOnlySpan<T> span, T value0, T value1)
        {
            for (int i = 0; i < span.Length; i++)
                if (CoreIntrinsics.AreEqual(span[i], value0) || CoreIntrinsics.AreEqual(span[i], value1)) return i;
            return -1;
        }

        public static int IndexOfAny<T>(ReadOnlySpan<T> span, T value0, T value1, T value2)
        {
            for (int i = 0; i < span.Length; i++)
                if (CoreIntrinsics.AreEqual(span[i], value0) || CoreIntrinsics.AreEqual(span[i], value1) || CoreIntrinsics.AreEqual(span[i], value2)) return i;
            return -1;
        }

        public static int IndexOfAny<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> values)
        {
            for (int i = 0; i < span.Length; i++)
                if (Contains(values, span[i])) return i;
            return -1;
        }

        public static int LastIndexOfAny<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> values)
        {
            for (int i = span.Length - 1; i >= 0; i--)
                if (Contains(values, span[i])) return i;
            return -1;
        }

        public static int IndexOfAnyExcept<T>(ReadOnlySpan<T> span, T value)
        {
            for (int i = 0; i < span.Length; i++)
                if (!CoreIntrinsics.AreEqual(span[i], value)) return i;
            return -1;
        }

        public static int IndexOfAnyExcept<T>(ReadOnlySpan<T> span, T value0, T value1)
        {
            for (int i = 0; i < span.Length; i++)
                if (!CoreIntrinsics.AreEqual(span[i], value0) && !CoreIntrinsics.AreEqual(span[i], value1)) return i;
            return -1;
        }

        public static int IndexOfAnyExcept<T>(ReadOnlySpan<T> span, T value0, T value1, T value2)
        {
            for (int i = 0; i < span.Length; i++)
                if (!CoreIntrinsics.AreEqual(span[i], value0) && !CoreIntrinsics.AreEqual(span[i], value1) && !CoreIntrinsics.AreEqual(span[i], value2)) return i;
            return -1;
        }

        public static int IndexOfAnyExcept<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> values)
        {
            for (int i = 0; i < span.Length; i++)
                if (!Contains(values, span[i])) return i;
            return -1;
        }

        public static int IndexOfAnyInRange<T>(ReadOnlySpan<T> span, T lowInclusive, T highInclusive)
        {
            for (int i = 0; i < span.Length; i++)
                if (CoreIntrinsics.Compare(span[i], lowInclusive) >= 0 && CoreIntrinsics.Compare(span[i], highInclusive) <= 0) return i;
            return -1;
        }

        public static int IndexOfAnyExceptInRange<T>(ReadOnlySpan<T> span, T lowInclusive, T highInclusive)
        {
            for (int i = 0; i < span.Length; i++)
                if (CoreIntrinsics.Compare(span[i], lowInclusive) < 0 || CoreIntrinsics.Compare(span[i], highInclusive) > 0) return i;
            return -1;
        }

        public static int LastIndexOf<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value)
        {
            if (value.Length == 0) return span.Length;
            for (int i = span.Length - value.Length; i >= 0; i--)
                if (SequenceEqualAt(span, i, value)) return i;
            return -1;
        }

        public static int CommonPrefixLength<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> other)
        {
            int length = Math.Min(span.Length, other.Length);
            for (int i = 0; i < length; i++)
                if (!CoreIntrinsics.AreEqual(span[i], other[i])) return i;
            return length;
        }

        public static int IndexOf(ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparisonType)
        {
            if (comparisonType == StringComparison.Ordinal) return StringOps.IndexOfOrdinal(span, value, 0);
            if (comparisonType == StringComparison.OrdinalIgnoreCase)
            {
                if (value.Length == 0) return 0;
                for (int i = 0; i <= span.Length - value.Length; i++)
                    if (EqualsOrdinalIgnoreCase(span.Slice(i, value.Length), value)) return i;
                return -1;
            }
            return span.ToString().IndexOf(value.ToString(), comparisonType);
        }

        public static bool StartsWith(ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparisonType)
        {
            if (comparisonType == StringComparison.Ordinal)
                return value.Length <= span.Length && SequenceEqualAt(span, 0, value);
            if (comparisonType == StringComparison.OrdinalIgnoreCase)
                return value.Length <= span.Length && EqualsOrdinalIgnoreCase(span.Slice(0, value.Length), value);
            return span.ToString().StartsWith(value.ToString(), comparisonType);
        }

        private static bool Contains<T>(ReadOnlySpan<T> values, T value)
        {
            for (int j = 0; j < values.Length; j++)
                if (CoreIntrinsics.AreEqual(values[j], value)) return true;
            return false;
        }

        private static bool SequenceEqualAt<T>(ReadOnlySpan<T> span, int start, ReadOnlySpan<T> value)
        {
            for (int j = 0; j < value.Length; j++)
                if (!CoreIntrinsics.AreEqual(span[start + j], value[j])) return false;
            return true;
        }

        // OrdinalIgnoreCase: simple invariant uppercase mapping per UTF-16 unit, like .NET.
        private static bool EqualsOrdinalIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            for (int j = 0; j < a.Length; j++)
                if (a[j] != b[j] && char.ToUpperInvariant(a[j]) != char.ToUpperInvariant(b[j])) return false;
            return true;
        }
    }

    [ShadowMembers("System.Text.Ascii")]
    internal static class AsciiExtras
    {
        public static bool IsValid(ReadOnlySpan<char> value)
        {
            for (int i = 0; i < value.Length; i++)
                if (value[i] > 0x7F) return false;
            return true;
        }

        public static bool IsValid(ReadOnlySpan<byte> value)
        {
            for (int i = 0; i < value.Length; i++)
                if (value[i] > 0x7F) return false;
            return true;
        }

        public static bool IsValid(char value) => value <= 0x7F;

        public static bool IsValid(byte value) => value <= 0x7F;
    }

    [ShadowMembers("System.Globalization.CharUnicodeInfo")]
    internal static class CharUnicodeInfoExtras
    {
        public static System.Globalization.UnicodeCategory GetUnicodeCategory(char ch) => CharOps.GetUnicodeCategory(ch);
    }
}

namespace CsharpcCoreLib
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    // Task combinators over IEnumerable: materialize, then use the runtime's array overloads.
    [ShadowMembers("System.Threading.Tasks.Task")]
    internal static class TaskExtras
    {
        public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks) => Task.WhenAll(Enumerable.ToArray(tasks));
        public static Task WhenAll(IEnumerable<Task> tasks) => Task.WhenAll(Enumerable.ToArray(tasks));
        public static Task<Task> WhenAny(IEnumerable<Task> tasks) => Task.WhenAny(Enumerable.ToArray(tasks));
        public static Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks) => Task.WhenAny(Enumerable.ToArray(tasks));
    }
}

namespace CsharpcCoreLib
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>System.StringComparer: the six standard comparers (culture = the invariant culture).</summary>
    [Shadows("System.StringComparer")]
    public abstract class StringComparerImpl : IComparer<string>, IEqualityComparer<string>, IComparer, IEqualityComparer
    {
        // Lazily created (no static initializers in the corelib).
        private static StringComparerImpl s_ordinal, s_ordinalIgnoreCase, s_invariant, s_invariantIgnoreCase;
        public static StringComparerImpl Ordinal => s_ordinal ??= new Standard(StringComparison.Ordinal);
        public static StringComparerImpl OrdinalIgnoreCase => s_ordinalIgnoreCase ??= new Standard(StringComparison.OrdinalIgnoreCase);
        public static StringComparerImpl InvariantCulture => s_invariant ??=
            new CultureAware(CsharpcCoreLib.Globalization.CompareInfo.Invariant, System.Globalization.CompareOptions.None);
        public static StringComparerImpl InvariantCultureIgnoreCase => s_invariantIgnoreCase ??=
            new CultureAware(CsharpcCoreLib.Globalization.CompareInfo.Invariant, System.Globalization.CompareOptions.IgnoreCase);
        // .NET creates these on each access, for the culture current at that moment.
        public static StringComparerImpl CurrentCulture =>
            new CultureAware(CsharpcCoreLib.Globalization.CultureInfo.CurrentCulture.CompareInfo, System.Globalization.CompareOptions.None);
        public static StringComparerImpl CurrentCultureIgnoreCase =>
            new CultureAware(CsharpcCoreLib.Globalization.CultureInfo.CurrentCulture.CompareInfo, System.Globalization.CompareOptions.IgnoreCase);

        public static StringComparerImpl Create(System.Globalization.CultureInfo culture, bool ignoreCase)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));
            return new CultureAware(((CsharpcCoreLib.Globalization.CultureInfo)(object)culture).CompareInfo,
                ignoreCase ? System.Globalization.CompareOptions.IgnoreCase : System.Globalization.CompareOptions.None);
        }

        public static StringComparerImpl Create(System.Globalization.CultureInfo culture, System.Globalization.CompareOptions options)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));
            return new CultureAware(((CsharpcCoreLib.Globalization.CultureInfo)(object)culture).CompareInfo, options);
        }

        public static StringComparerImpl FromComparison(StringComparison comparisonType) => comparisonType switch
        {
            StringComparison.CurrentCulture => CurrentCulture,
            StringComparison.CurrentCultureIgnoreCase => CurrentCultureIgnoreCase,
            StringComparison.InvariantCulture => InvariantCulture,
            StringComparison.InvariantCultureIgnoreCase => InvariantCultureIgnoreCase,
            StringComparison.Ordinal => Ordinal,
            StringComparison.OrdinalIgnoreCase => OrdinalIgnoreCase,
            _ => throw new ArgumentException("The string comparison type passed in is currently not supported.", nameof(comparisonType)),
        };

        public abstract int Compare(string x, string y);
        public abstract bool Equals(string x, string y);
        public abstract int GetHashCode(string obj);

        public int Compare(object x, object y) => x == y ? 0 : x == null ? -1 : y == null ? 1 : Compare((string)x, (string)y);
        public new bool Equals(object x, object y) => x == y || (x is string a && y is string b && Equals(a, b));
        public int GetHashCode(object obj) => obj is string s ? GetHashCode(s) : obj.GetHashCode();

        private sealed class Standard(StringComparison comparison) : StringComparerImpl
        {
            private bool IgnoreCase => comparison is StringComparison.OrdinalIgnoreCase or StringComparison.InvariantCultureIgnoreCase
                or StringComparison.CurrentCultureIgnoreCase;

            public override int Compare(string x, string y) => string.Compare(x, y, comparison);
            public override bool Equals(string x, string y) => string.Equals(x, y, comparison);
            public override int GetHashCode(string obj)
            {
                if (obj == null) throw new ArgumentNullException(nameof(obj));
                if (comparison == StringComparison.OrdinalIgnoreCase) return OrdinalCasing.ToUpperOrdinal(obj).GetHashCode();
                return obj.GetHashCode();
            }
        }

        /// <summary>System.CultureAwareComparer: a CompareInfo and CompareOptions.</summary>
        private sealed class CultureAware : StringComparerImpl
        {
            private readonly CsharpcCoreLib.Globalization.CompareInfo _compareInfo;
            private readonly System.Globalization.CompareOptions _options;

            internal CultureAware(CsharpcCoreLib.Globalization.CompareInfo compareInfo, System.Globalization.CompareOptions options)
            {
                _compareInfo = compareInfo;
                _options = options;
            }

            public override int Compare(string x, string y)
            {
                if (object.ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                return _compareInfo.Compare(x, y, _options);
            }

            public override bool Equals(string x, string y)
            {
                if (object.ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return _compareInfo.Compare(x, y, _options) == 0;
            }

            public override int GetHashCode(string obj)
            {
                if (obj == null) throw new ArgumentNullException(nameof(obj));
                return _compareInfo.GetHashCode(obj, _options);
            }
        }
    }
}
