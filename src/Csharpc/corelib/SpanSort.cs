// MemoryExtensions.Sort over spans: through a temporary array and the corelib's introsort (SortHelper<T>), which also
// wraps what a comparison throws in InvalidOperationException as .NET's ArraySortHelper does.
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.MemoryExtensions")]
    internal static class MemoryExtensionsSort
    {
        public static void Sort<T>(Span<T> span)
        {
            if (span.Length < 2) return;
            T[] copy = span.ToArray();
            SortHelper<T>.SortDefault(copy, 0, copy.Length);
            copy.AsSpan().CopyTo(span);
        }

        public static void Sort<T>(Span<T> span, Comparison<T> comparison)
        {
            if (comparison == null) throw new ArgumentNullException("comparison");
            if (span.Length < 2) return;
            T[] copy = span.ToArray();
            SortHelper<T>.Sort(copy, 0, copy.Length, Comparer<T>.Create(comparison));
            copy.AsSpan().CopyTo(span);
        }

        public static void Sort<T, TComparer>(Span<T> span, TComparer comparer) where TComparer : IComparer<T>
        {
            if (span.Length < 2) return;
            T[] copy = span.ToArray();
            SortHelper<T>.Sort(copy, 0, copy.Length, comparer);
            copy.AsSpan().CopyTo(span);
        }
    }
}
