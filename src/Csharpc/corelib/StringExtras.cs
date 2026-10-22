// Static System.String members that need generics (the rest of string is runtime/strings.c intrinsics).
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
        public static T Min<T>(ReadOnlySpan<T> span) => MinMax(span, null, 1);
        public static T Max<T>(ReadOnlySpan<T> span) => MinMax(span, null, -1);
        public static T Min<T>(ReadOnlySpan<T> span, IComparer<T> comparer) => MinMax(span, comparer, 1);
        public static T Max<T>(ReadOnlySpan<T> span, IComparer<T> comparer) => MinMax(span, comparer, -1);

        private static T MinMax<T>(ReadOnlySpan<T> span, IComparer<T> comparer, int sign)
        {
            if (span.Length == 0) throw new InvalidOperationException("Sequence contains no elements");
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
        // Lazily created: corelib types get no static constructor run (only user types are statically initialized).
        private static StringComparerImpl s_ordinal, s_ordinalIgnoreCase, s_invariant, s_invariantIgnoreCase;
        public static StringComparerImpl Ordinal => s_ordinal ??= new Standard(StringComparison.Ordinal);
        public static StringComparerImpl OrdinalIgnoreCase => s_ordinalIgnoreCase ??= new Standard(StringComparison.OrdinalIgnoreCase);
        public static StringComparerImpl InvariantCulture => s_invariant ??= new Standard(StringComparison.InvariantCulture);
        public static StringComparerImpl InvariantCultureIgnoreCase => s_invariantIgnoreCase ??= new Standard(StringComparison.InvariantCultureIgnoreCase);
        public static StringComparerImpl CurrentCulture => InvariantCulture;
        public static StringComparerImpl CurrentCultureIgnoreCase => InvariantCultureIgnoreCase;

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
                return IgnoreCase ? obj.ToUpperInvariant().GetHashCode() : obj.GetHashCode();
            }
        }
    }
}
