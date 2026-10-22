// The span-based MemoryExtensions aggregates .NET 11 binds for int[].Max() (StringComparer lives in StringExtras.cs).
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
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
