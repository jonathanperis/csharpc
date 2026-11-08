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

        // MemoryExtensions.Sort(keys, items): the items follow their keys (Array.Sort(keys, items)'s introsort, so the order of
        // equal keys is as unspecified as in .NET; System.Text.Json sorts on a unique (key, index) pair).
        public static void Sort<TKey, TValue>(Span<TKey> keys, Span<TValue> items)
        {
            if (keys.Length != items.Length) throw new ArgumentException("Length of items must be at least the length of keys.", "items");
            if (keys.Length < 2) return;
            TKey[] keyCopy = keys.ToArray();
            TValue[] itemCopy = items.ToArray();
            Array.Sort(keyCopy, itemCopy);
            keyCopy.AsSpan().CopyTo(keys);
            itemCopy.AsSpan().CopyTo(items);
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
