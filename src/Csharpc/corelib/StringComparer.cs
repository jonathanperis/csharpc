// System.StringComparer and the span-based MemoryExtensions aggregates .NET 11 binds for int[].Max().
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.StringComparer")]
    public abstract class StringComparerImpl : IComparer<string>, IEqualityComparer<string>, System.Collections.IComparer, System.Collections.IEqualityComparer
    {
        private static StringComparer s_ordinal, s_ordinalIgnoreCase, s_invariant, s_invariantIgnoreCase;

        public static StringComparer Ordinal => s_ordinal ??= (StringComparer)(object)new StringComparisonComparer(StringComparison.Ordinal);
        public static StringComparer OrdinalIgnoreCase => s_ordinalIgnoreCase ??= (StringComparer)(object)new StringComparisonComparer(StringComparison.OrdinalIgnoreCase);
        public static StringComparer InvariantCulture => s_invariant ??= (StringComparer)(object)new StringComparisonComparer(StringComparison.InvariantCulture);
        public static StringComparer InvariantCultureIgnoreCase => s_invariantIgnoreCase ??= (StringComparer)(object)new StringComparisonComparer(StringComparison.InvariantCultureIgnoreCase);
        public static StringComparer CurrentCulture => InvariantCulture;
        public static StringComparer CurrentCultureIgnoreCase => InvariantCultureIgnoreCase;

        public static StringComparer FromComparison(StringComparison comparison) =>
            (StringComparer)(object)new StringComparisonComparer(comparison);

        public abstract int Compare(string x, string y);
        public abstract bool Equals(string x, string y);
        public abstract int GetHashCode(string obj);

        int System.Collections.IComparer.Compare(object x, object y) => Compare((string)x, (string)y);
        bool System.Collections.IEqualityComparer.Equals(object x, object y) => Equals((string)x, (string)y);
        int System.Collections.IEqualityComparer.GetHashCode(object obj) => obj == null ? 0 : GetHashCode((string)obj);
    }

    internal sealed class StringComparisonComparer : StringComparerImpl
    {
        private readonly StringComparison _comparison;
        public StringComparisonComparer(StringComparison comparison) { _comparison = comparison; }

        public override int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            return string.Compare(x, y, _comparison);
        }

        public override bool Equals(string x, string y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            return string.Compare(x, y, _comparison) == 0;
        }

        public override int GetHashCode(string obj)
        {
            if (obj == null) ThrowHelper.ArgumentNull("obj");
            bool ignoreCase = _comparison == StringComparison.OrdinalIgnoreCase || _comparison == StringComparison.InvariantCultureIgnoreCase
                || _comparison == StringComparison.CurrentCultureIgnoreCase;
            return (ignoreCase ? obj.ToUpperInvariant() : obj).GetHashCode();
        }
    }

    [ShadowMembers("System.MemoryExtensions")]
    internal static class MemoryExtensionsAggregates
    {
        public static T Max<T>(ReadOnlySpan<T> span) => Max(span, null);
        public static T Min<T>(ReadOnlySpan<T> span) => Min(span, null);

        public static T Max<T>(ReadOnlySpan<T> span, IComparer<T> comparer)
        {
            if (span.Length == 0) throw new InvalidOperationException("Sequence contains no elements."); // MemoryExtensions' wording
            comparer ??= Comparer<T>.Default;
            T m = span[0];
            for (int i = 1; i < span.Length; i++) if (comparer.Compare(span[i], m) > 0) m = span[i];
            return m;
        }

        public static T Min<T>(ReadOnlySpan<T> span, IComparer<T> comparer)
        {
            if (span.Length == 0) throw new InvalidOperationException("Sequence contains no elements."); // MemoryExtensions' wording
            comparer ??= Comparer<T>.Default;
            T m = span[0];
            for (int i = 1; i < span.Length; i++) if (comparer.Compare(span[i], m) < 0) m = span[i];
            return m;
        }
    }
}
