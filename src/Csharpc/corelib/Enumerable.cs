// System.Linq.Enumerable — the common operator surface with .NET semantics (deferred execution,
// exception types/messages, checked Sum, NaN rules of Min/Max, stable OrderBy, group order).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CsharpcCoreLib
{
    [Shadows("System.Linq.Enumerable")]
    public static class EnumerableImpl
    {
        private static void Check(object argument, string name) { if (argument == null) ThrowHelper.ArgumentNull(name); }

        // ---- filtering / projection ------------------------------------------------------------

        public static IEnumerable<TSource> Where<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        { Check(source, "source"); Check(predicate, "predicate"); return new WhereIterator<TSource>(source, predicate, null); }

        public static IEnumerable<TSource> Where<TSource>(IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        { Check(source, "source"); Check(predicate, "predicate"); return new WhereIterator<TSource>(source, null, predicate); }

        public static IEnumerable<TResult> Select<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> selector)
        { Check(source, "source"); Check(selector, "selector"); return new SelectIterator<TSource, TResult>(source, selector, null); }

        public static IEnumerable<TResult> Select<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, int, TResult> selector)
        { Check(source, "source"); Check(selector, "selector"); return new SelectIterator<TSource, TResult>(source, null, selector); }

        public static IEnumerable<TResult> SelectMany<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, IEnumerable<TResult>> selector)
        { Check(source, "source"); Check(selector, "selector"); return new SelectManyIterator<TSource, TResult, TResult>(source, selector, null, null); }

        public static IEnumerable<TResult> SelectMany<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, int, IEnumerable<TResult>> selector)
        { Check(source, "source"); Check(selector, "selector"); return new SelectManyIterator<TSource, TResult, TResult>(source, null, selector, null); }

        public static IEnumerable<TResult> SelectMany<TSource, TCollection, TResult>(IEnumerable<TSource> source,
            Func<TSource, IEnumerable<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)
        {
            Check(source, "source"); Check(collectionSelector, "collectionSelector"); Check(resultSelector, "resultSelector");
            return new SelectManyIterator<TSource, TCollection, TResult>(source, collectionSelector, null, resultSelector);
        }

        public static IEnumerable<TResult> SelectMany<TSource, TCollection, TResult>(IEnumerable<TSource> source,
            Func<TSource, int, IEnumerable<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)
        {
            Check(source, "source"); Check(collectionSelector, "collectionSelector"); Check(resultSelector, "resultSelector");
            return new SelectManyIterator<TSource, TCollection, TResult>(source, null, collectionSelector, resultSelector);
        }

        public static IEnumerable<TResult> Cast<TResult>(IEnumerable source)
        {
            Check(source, "source");
            if (source is IEnumerable<TResult> typed) return typed;
            return new CastIterator<TResult>(source, false);
        }

        public static IEnumerable<TResult> OfType<TResult>(IEnumerable source) { Check(source, "source"); return new CastIterator<TResult>(source, true); }

        public static IEnumerable<(int Index, TSource Item)> Index<TSource>(IEnumerable<TSource> source) =>
            Select(source, (TSource item, int index) => (index, item));

        // ---- partitioning ------------------------------------------------------------------

        public static IEnumerable<TSource> Take<TSource>(IEnumerable<TSource> source, int count)
        { Check(source, "source"); return new PartitionIterator<TSource>(source, 0, count, null, null); }

        public static IEnumerable<TSource> Skip<TSource>(IEnumerable<TSource> source, int count)
        { Check(source, "source"); return new PartitionIterator<TSource>(source, 1, count, null, null); }

        public static IEnumerable<TSource> TakeWhile<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        { Check(source, "source"); Check(predicate, "predicate"); return new PartitionIterator<TSource>(source, 2, 0, predicate, null); }

        public static IEnumerable<TSource> TakeWhile<TSource>(IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        { Check(source, "source"); Check(predicate, "predicate"); return new PartitionIterator<TSource>(source, 2, 0, null, predicate); }

        public static IEnumerable<TSource> SkipWhile<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        { Check(source, "source"); Check(predicate, "predicate"); return new PartitionIterator<TSource>(source, 3, 0, predicate, null); }

        public static IEnumerable<TSource> SkipWhile<TSource>(IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        { Check(source, "source"); Check(predicate, "predicate"); return new PartitionIterator<TSource>(source, 3, 0, null, predicate); }

        public static IEnumerable<TSource> TakeLast<TSource>(IEnumerable<TSource> source, int count)
        {
            Check(source, "source");
            return new BufferedIterator<TSource, TSource>(source, list =>
            {
                int n = Math.Max(0, Math.Min(count, list.Count));
                return list.GetRange(list.Count - n, n);
            });
        }

        public static IEnumerable<TSource> SkipLast<TSource>(IEnumerable<TSource> source, int count)
        {
            Check(source, "source");
            return new BufferedIterator<TSource, TSource>(source, list => list.GetRange(0, Math.Max(0, list.Count - Math.Max(0, count))));
        }

        public static IEnumerable<TSource[]> Chunk<TSource>(IEnumerable<TSource> source, int size)
        {
            Check(source, "source");
            if (size < 1) ThrowHelper.ArgumentOutOfRange("size", "size must be greater than or equal to 1.");
            return new BufferedIterator<TSource, TSource[]>(source, list =>
            {
                var chunks = new List<TSource[]>();
                for (int i = 0; i < list.Count; i += size) chunks.Add(list.GetRange(i, Math.Min(size, list.Count - i)).ToArray());
                return chunks;
            });
        }

        // ---- combining ---------------------------------------------------------------------

        public static IEnumerable<TSource> Concat<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second)
        { Check(first, "first"); Check(second, "second"); return new ConcatIterator<TSource>(first, second); }

        public static IEnumerable<TSource> Append<TSource>(IEnumerable<TSource> source, TSource element)
        { Check(source, "source"); return new AppendIterator<TSource>(source, element, true); }

        public static IEnumerable<TSource> Prepend<TSource>(IEnumerable<TSource> source, TSource element)
        { Check(source, "source"); return new AppendIterator<TSource>(source, element, false); }

        public static IEnumerable<TSource> Reverse<TSource>(IEnumerable<TSource> source)
        {
            Check(source, "source");
            return new BufferedIterator<TSource, TSource>(source, list => { list.Reverse(); return list; });
        }

        public static IEnumerable<TSource> Reverse<TSource>(TSource[] array)
        {
            Check(array, "array");
            return new BufferedIterator<TSource, TSource>(array, list => { list.Reverse(); return list; });
        }

        public static IEnumerable<TResult> Zip<TFirst, TSecond, TResult>(IEnumerable<TFirst> first, IEnumerable<TSecond> second,
            Func<TFirst, TSecond, TResult> resultSelector)
        {
            Check(first, "first"); Check(second, "second"); Check(resultSelector, "resultSelector");
            return new ZipIterator<TFirst, TSecond, TResult>(first, second, resultSelector);
        }

        public static IEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(IEnumerable<TFirst> first, IEnumerable<TSecond> second)
        {
            Check(first, "first"); Check(second, "second");
            return new ZipIterator<TFirst, TSecond, (TFirst, TSecond)>(first, second, (a, b) => (a, b));
        }

        public static IEnumerable<TSource> DefaultIfEmpty<TSource>(IEnumerable<TSource> source) => DefaultIfEmpty(source, default(TSource));

        public static IEnumerable<TSource> DefaultIfEmpty<TSource>(IEnumerable<TSource> source, TSource defaultValue)
        {
            Check(source, "source");
            return new BufferedIterator<TSource, TSource>(source, list =>
            {
                if (list.Count == 0) list.Add(defaultValue);
                return list;
            });
        }

        // ---- sets ----------------------------------------------------------------------------

        public static IEnumerable<TSource> Distinct<TSource>(IEnumerable<TSource> source) => Distinct(source, null);

        public static IEnumerable<TSource> Distinct<TSource>(IEnumerable<TSource> source, IEqualityComparer<TSource> comparer)
        { Check(source, "source"); return new SetIterator<TSource, TSource>(source, null, 0, null, comparer); }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            DistinctBy(source, keySelector, null);

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        { Check(source, "source"); Check(keySelector, "keySelector"); return new SetIterator<TSource, TKey>(source, null, 0, keySelector, comparer); }

        public static IEnumerable<TSource> Union<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second) => Union(first, second, null);

        public static IEnumerable<TSource> Union<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        { Check(first, "first"); Check(second, "second"); return new SetIterator<TSource, TSource>(first, second, 1, null, comparer); }

        public static IEnumerable<TSource> Intersect<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second) => Intersect(first, second, null);

        public static IEnumerable<TSource> Intersect<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        { Check(first, "first"); Check(second, "second"); return new SetIterator<TSource, TSource>(first, second, 2, null, comparer); }

        public static IEnumerable<TSource> Except<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second) => Except(first, second, null);

        public static IEnumerable<TSource> Except<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        { Check(first, "first"); Check(second, "second"); return new SetIterator<TSource, TSource>(first, second, 3, null, comparer); }

        // ---- ordering ------------------------------------------------------------------------

        public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            OrderBy(source, keySelector, null);

        public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        { Check(source, "source"); Check(keySelector, "keySelector"); return new OrderedEnumerable<TSource, TKey>(source, keySelector, comparer, false, null); }

        public static IOrderedEnumerable<TSource> OrderByDescending<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            OrderByDescending(source, keySelector, null);

        public static IOrderedEnumerable<TSource> OrderByDescending<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        { Check(source, "source"); Check(keySelector, "keySelector"); return new OrderedEnumerable<TSource, TKey>(source, keySelector, comparer, true, null); }

        public static IOrderedEnumerable<TSource> ThenBy<TSource, TKey>(IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            ThenBy(source, keySelector, null);

        public static IOrderedEnumerable<TSource> ThenBy<TSource, TKey>(IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        { Check(source, "source"); Check(keySelector, "keySelector"); return ThenByImpl(source, keySelector, comparer, false); }

        public static IOrderedEnumerable<TSource> ThenByDescending<TSource, TKey>(IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            ThenByDescending(source, keySelector, null);

        public static IOrderedEnumerable<TSource> ThenByDescending<TSource, TKey>(IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        { Check(source, "source"); Check(keySelector, "keySelector"); return ThenByImpl(source, keySelector, comparer, true); }

        private static IOrderedEnumerable<TSource> ThenByImpl<TSource, TKey>(IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IComparer<TKey> comparer, bool descending)
        {
            if (source is OrderedEnumerable<TSource> ordered) return ordered.ThenByCore(keySelector, comparer, descending);
            ThrowHelper.InvalidOperation("ThenBy on a user-defined IOrderedEnumerable<T> is not supported by csharpc yet.");
            return null;
        }

        public static IOrderedEnumerable<T> Order<T>(IEnumerable<T> source) => OrderBy(source, (T x) => x, null);
        public static IOrderedEnumerable<T> Order<T>(IEnumerable<T> source, IComparer<T> comparer) => OrderBy(source, (T x) => x, comparer);
        public static IOrderedEnumerable<T> OrderDescending<T>(IEnumerable<T> source) => OrderByDescending(source, (T x) => x, null);
        public static IOrderedEnumerable<T> OrderDescending<T>(IEnumerable<T> source, IComparer<T> comparer) => OrderByDescending(source, (T x) => x, comparer);

        // ---- grouping / joins ------------------------------------------------------------------

        private static Lookup<TKey, TElement> BuildLookup<TSource, TKey, TElement>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        {
            var lookup = new Lookup<TKey, TElement>(comparer);
            foreach (TSource item in source) lookup.GetGrouping(keySelector(item), true)._elements.Add(elementSelector(item));
            return lookup;
        }

        public static IEnumerable<IGrouping<TKey, TSource>> GroupBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            GroupBy(source, keySelector, (IEqualityComparer<TKey>)null);

        public static IEnumerable<IGrouping<TKey, TSource>> GroupBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            Check(source, "source"); Check(keySelector, "keySelector");
            return new DeferredEnumerable<IGrouping<TKey, TSource>>(() => BuildLookup(source, keySelector, (TSource x) => x, comparer));
        }

        public static IEnumerable<IGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector) => GroupBy(source, keySelector, elementSelector, (IEqualityComparer<TKey>)null);

        public static IEnumerable<IGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        {
            Check(source, "source"); Check(keySelector, "keySelector"); Check(elementSelector, "elementSelector");
            return new DeferredEnumerable<IGrouping<TKey, TElement>>(() => BuildLookup(source, keySelector, elementSelector, comparer));
        }

        public static IEnumerable<TResult> GroupBy<TSource, TKey, TResult>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TKey, IEnumerable<TSource>, TResult> resultSelector) => GroupBy(source, keySelector, resultSelector, (IEqualityComparer<TKey>)null);

        public static IEnumerable<TResult> GroupBy<TSource, TKey, TResult>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TKey, IEnumerable<TSource>, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            Check(source, "source"); Check(keySelector, "keySelector"); Check(resultSelector, "resultSelector");
            return new DeferredEnumerable<TResult>(() =>
            {
                var lookup = BuildLookup(source, keySelector, (TSource x) => x, comparer);
                var results = new List<TResult>();
                foreach (var g in lookup.Groupings) results.Add(resultSelector(g._key, g));
                return results;
            });
        }

        public static IEnumerable<TResult> GroupBy<TSource, TKey, TElement, TResult>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector, Func<TKey, IEnumerable<TElement>, TResult> resultSelector) =>
            GroupBy(source, keySelector, elementSelector, resultSelector, null);

        public static IEnumerable<TResult> GroupBy<TSource, TKey, TElement, TResult>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector, Func<TKey, IEnumerable<TElement>, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            Check(source, "source"); Check(keySelector, "keySelector"); Check(elementSelector, "elementSelector"); Check(resultSelector, "resultSelector");
            return new DeferredEnumerable<TResult>(() =>
            {
                var lookup = BuildLookup(source, keySelector, elementSelector, comparer);
                var results = new List<TResult>();
                foreach (var g in lookup.Groupings) results.Add(resultSelector(g._key, g));
                return results;
            });
        }

        public static ILookup<TKey, TSource> ToLookup<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            ToLookup(source, keySelector, (IEqualityComparer<TKey>)null);

        public static ILookup<TKey, TSource> ToLookup<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        { Check(source, "source"); Check(keySelector, "keySelector"); return BuildLookup(source, keySelector, (TSource x) => x, comparer); }

        public static ILookup<TKey, TElement> ToLookup<TSource, TKey, TElement>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector) => ToLookup(source, keySelector, elementSelector, null);

        public static ILookup<TKey, TElement> ToLookup<TSource, TKey, TElement>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        { Check(source, "source"); Check(keySelector, "keySelector"); return BuildLookup(source, keySelector, elementSelector, comparer); }

        public static IEnumerable<KeyValuePair<TKey, int>> CountBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> keyComparer)
        {
            Check(source, "source"); Check(keySelector, "keySelector");
            return new DeferredEnumerable<KeyValuePair<TKey, int>>(() =>
            {
                var counts = new Dictionary<TKey, int>(keyComparer);
                foreach (TSource item in source)
                {
                    TKey key = keySelector(item);
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                return counts;
            });
        }

        public static IEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(IEnumerable<TOuter> outer, IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, TInner, TResult> resultSelector) =>
            Join(outer, inner, outerKeySelector, innerKeySelector, resultSelector, null);

        public static IEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(IEnumerable<TOuter> outer, IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, TInner, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            Check(outer, "outer"); Check(inner, "inner");
            return new DeferredEnumerable<TResult>(() =>
            {
                var lookup = BuildLookup(inner, innerKeySelector, (TInner x) => x, comparer);
                var results = new List<TResult>();
                foreach (TOuter item in outer)
                {
                    TKey key = outerKeySelector(item);
                    if (key == null) continue;
                    var g = lookup.GetGrouping(key, false);
                    if (g != null) foreach (TInner match in g._elements) results.Add(resultSelector(item, match));
                }
                return results;
            });
        }

        public static IEnumerable<TResult> GroupJoin<TOuter, TInner, TKey, TResult>(IEnumerable<TOuter> outer, IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, IEnumerable<TInner>, TResult> resultSelector) =>
            GroupJoin(outer, inner, outerKeySelector, innerKeySelector, resultSelector, null);

        public static IEnumerable<TResult> GroupJoin<TOuter, TInner, TKey, TResult>(IEnumerable<TOuter> outer, IEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, IEnumerable<TInner>, TResult> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            Check(outer, "outer"); Check(inner, "inner");
            return new DeferredEnumerable<TResult>(() =>
            {
                var lookup = BuildLookup(inner, innerKeySelector, (TInner x) => x, comparer);
                var results = new List<TResult>();
                foreach (TOuter item in outer) results.Add(resultSelector(item, lookup[outerKeySelector(item)]));
                return results;
            });
        }

        // ---- conversions -----------------------------------------------------------------------

        public static List<TSource> ToList<TSource>(IEnumerable<TSource> source) { Check(source, "source"); return new List<TSource>(source); }

        public static TSource[] ToArray<TSource>(IEnumerable<TSource> source) { Check(source, "source"); return new List<TSource>(source).ToArray(); }

        public static HashSet<TSource> ToHashSet<TSource>(IEnumerable<TSource> source) => ToHashSet(source, null);

        public static HashSet<TSource> ToHashSet<TSource>(IEnumerable<TSource> source, IEqualityComparer<TSource> comparer)
        { Check(source, "source"); return new HashSet<TSource>(source, comparer); }

        public static Dictionary<TKey, TSource> ToDictionary<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            ToDictionary(source, keySelector, (IEqualityComparer<TKey>)null);

        public static Dictionary<TKey, TSource> ToDictionary<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            Check(source, "source"); Check(keySelector, "keySelector");
            var d = new Dictionary<TKey, TSource>(comparer);
            foreach (TSource item in source) d.Add(keySelector(item), item);
            return d;
        }

        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector) => ToDictionary(source, keySelector, elementSelector, null);

        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        {
            Check(source, "source"); Check(keySelector, "keySelector"); Check(elementSelector, "elementSelector");
            var d = new Dictionary<TKey, TElement>(comparer);
            foreach (TSource item in source) d.Add(keySelector(item), elementSelector(item));
            return d;
        }

        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> source) =>
            new Dictionary<TKey, TValue>(source);

        public static IEnumerable<TSource> AsEnumerable<TSource>(IEnumerable<TSource> source) => source;

        // ---- generation ------------------------------------------------------------------------

        public static IEnumerable<int> Range(int start, int count)
        {
            if (count < 0 || (long)start + count - 1 > int.MaxValue) ThrowHelper.ArgumentOutOfRange("count", "Specified argument was out of the range of valid values.");
            return new RangeIterator(start, count);
        }

        public static IEnumerable<TResult> Repeat<TResult>(TResult element, int count)
        {
            if (count < 0) ThrowHelper.ArgumentOutOfRange("count", "Specified argument was out of the range of valid values.");
            return new RepeatIterator<TResult>(element, count);
        }

        public static IEnumerable<TResult> Empty<TResult>() => new TResult[0];

        // ---- quantifiers / counting --------------------------------------------------------------

        public static bool TryGetNonEnumeratedCount<TSource>(IEnumerable<TSource> source, out int count)
        {
            Check(source, "source");
            if (source is ICollection<TSource> collectionOfT) { count = collectionOfT.Count; return true; }
            if (source is Iterator<TSource> iterator)
            {
                int c = iterator.CheapCount();
                if (c >= 0) { count = c; return true; }
            }
            else if (source is OrderedEnumerable<TSource> || source is DeferredEnumerable<TSource>)
                throw new NotSupportedException("Enumerable.TryGetNonEnumeratedCount over this LINQ operator is not supported by csharpc yet.");
            if (source is ICollection collection) { count = collection.Count; return true; }
            count = 0;
            return false;
        }

        public static int Count<TSource>(IEnumerable<TSource> source)
        {
            Check(source, "source");
            if (source is ICollection<TSource> c) return c.Count;
            int count = 0;
            using (IEnumerator<TSource> e = source.GetEnumerator())
                while (e.MoveNext()) count = checked(count + 1);
            return count;
        }

        public static int Count<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, "source"); Check(predicate, "predicate");
            int count = 0;
            foreach (TSource item in source) if (predicate(item)) count = checked(count + 1);
            return count;
        }

        public static long LongCount<TSource>(IEnumerable<TSource> source)
        {
            Check(source, "source");
            long count = 0;
            foreach (TSource item in source) count++;
            return count;
        }

        public static long LongCount<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            long count = 0;
            foreach (TSource item in source) if (predicate(item)) count++;
            return count;
        }

        public static bool Any<TSource>(IEnumerable<TSource> source)
        {
            Check(source, "source");
            if (source is ICollection<TSource> c) return c.Count != 0;
            using (IEnumerator<TSource> e = source.GetEnumerator()) return e.MoveNext();
        }

        public static bool Any<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, "source"); Check(predicate, "predicate");
            foreach (TSource item in source) if (predicate(item)) return true;
            return false;
        }

        public static bool All<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, "source"); Check(predicate, "predicate");
            foreach (TSource item in source) if (!predicate(item)) return false;
            return true;
        }

        public static bool Contains<TSource>(IEnumerable<TSource> source, TSource value)
        {
            Check(source, "source");
            if (source is ICollection<TSource> c) return c.Contains(value);
            foreach (TSource item in source) if (CoreIntrinsics.AreEqual(item, value)) return true;
            return false;
        }

        public static bool Contains<TSource>(IEnumerable<TSource> source, TSource value, IEqualityComparer<TSource> comparer)
        {
            Check(source, "source");
            if (comparer == null) return Contains(source, value);
            foreach (TSource item in source) if (comparer.Equals(item, value)) return true;
            return false;
        }

        public static bool SequenceEqual<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second) => SequenceEqual(first, second, null);

        public static bool SequenceEqual<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            Check(first, "first"); Check(second, "second");
            using (IEnumerator<TSource> e1 = first.GetEnumerator())
            using (IEnumerator<TSource> e2 = second.GetEnumerator())
            {
                while (e1.MoveNext())
                {
                    if (!e2.MoveNext()) return false;
                    bool equal = comparer == null ? CoreIntrinsics.AreEqual(e1.Current, e2.Current) : comparer.Equals(e1.Current, e2.Current);
                    if (!equal) return false;
                }
                return !e2.MoveNext();
            }
        }

        // ---- element operators -------------------------------------------------------------------

        private static bool TryFirst<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, out TSource result)
        {
            Check(source, "source");
            foreach (TSource item in source)
                if (predicate == null || predicate(item)) { result = item; return true; }
            result = default(TSource);
            return false;
        }

        private static bool TryLast<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, out TSource result)
        {
            Check(source, "source");
            bool found = false;
            result = default(TSource);
            foreach (TSource item in source)
                if (predicate == null || predicate(item)) { result = item; found = true; }
            return found;
        }

        private static int TrySingle<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, out TSource result)
        {
            Check(source, "source");
            int found = 0;
            result = default(TSource);
            foreach (TSource item in source)
            {
                if (predicate == null || predicate(item))
                {
                    if (++found > 1) return 2;
                    result = item;
                }
            }
            return found;
        }

        public static TSource First<TSource>(IEnumerable<TSource> source)
        {
            if (!TryFirst(source, null, out TSource r))
                throw CoreIntrinsics.LibraryFrames(ThrowHelper.NoElementsException(), "System.Linq.ThrowHelper.ThrowNoElementsException()\nSystem.Linq.Enumerable.First[TSource](IEnumerable`1 source)");
            return r;
        }

        public static TSource First<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(predicate, "predicate");
            if (!TryFirst(source, predicate, out TSource r))
                throw CoreIntrinsics.LibraryFrames(ThrowHelper.NoMatchException(), "System.Linq.ThrowHelper.ThrowNoMatchException()\nSystem.Linq.Enumerable.First[TSource](IEnumerable`1 source, Func`2 predicate)");
            return r;
        }

        public static TSource FirstOrDefault<TSource>(IEnumerable<TSource> source) { TryFirst(source, null, out TSource r); return r; }
        public static TSource FirstOrDefault<TSource>(IEnumerable<TSource> source, TSource defaultValue) => TryFirst(source, null, out TSource r) ? r : defaultValue;
        public static TSource FirstOrDefault<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate) { TryFirst(source, predicate, out TSource r); return r; }
        public static TSource FirstOrDefault<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource defaultValue) =>
            TryFirst(source, predicate, out TSource r) ? r : defaultValue;

        public static TSource Last<TSource>(IEnumerable<TSource> source)
        {
            if (!TryLast(source, null, out TSource r))
                throw CoreIntrinsics.LibraryFrames(ThrowHelper.NoElementsException(), "System.Linq.ThrowHelper.ThrowNoElementsException()\nSystem.Linq.Enumerable.Last[TSource](IEnumerable`1 source)");
            return r;
        }

        public static TSource Last<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        { Check(predicate, "predicate"); if (!TryLast(source, predicate, out TSource r)) ThrowHelper.NoMatch(); return r; }

        public static TSource LastOrDefault<TSource>(IEnumerable<TSource> source) { TryLast(source, null, out TSource r); return r; }
        public static TSource LastOrDefault<TSource>(IEnumerable<TSource> source, TSource defaultValue) => TryLast(source, null, out TSource r) ? r : defaultValue;
        public static TSource LastOrDefault<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate) { TryLast(source, predicate, out TSource r); return r; }
        public static TSource LastOrDefault<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource defaultValue) =>
            TryLast(source, predicate, out TSource r) ? r : defaultValue;

        public static TSource Single<TSource>(IEnumerable<TSource> source)
        {
            int n = TrySingle(source, null, out TSource r);
            if (n == 0) ThrowHelper.NoElements();
            if (n > 1) ThrowHelper.MoreThanOne();
            return r;
        }

        public static TSource Single<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            int n = TrySingle(source, predicate, out TSource r);
            if (n == 0) ThrowHelper.NoMatch();
            if (n > 1) ThrowHelper.MoreThanOneMatch();
            return r;
        }

        public static TSource SingleOrDefault<TSource>(IEnumerable<TSource> source) => SingleOrDefault(source, default(TSource));

        public static TSource SingleOrDefault<TSource>(IEnumerable<TSource> source, TSource defaultValue)
        {
            int n = TrySingle(source, null, out TSource r);
            if (n > 1) ThrowHelper.MoreThanOne();
            return n == 1 ? r : defaultValue;
        }

        public static TSource SingleOrDefault<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate) =>
            SingleOrDefault(source, predicate, default(TSource));

        public static TSource SingleOrDefault<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource defaultValue)
        {
            int n = TrySingle(source, predicate, out TSource r);
            if (n > 1) ThrowHelper.MoreThanOneMatch();
            return n == 1 ? r : defaultValue;
        }

        public static TSource ElementAt<TSource>(IEnumerable<TSource> source, int index)
        {
            Check(source, "source");
            if (source is IList<TSource> list) return list[index];
            if (index >= 0)
                foreach (TSource item in source)
                    if (index-- == 0) return item;
            ThrowHelper.ArgumentOutOfRange("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
            return default(TSource);
        }

        public static TSource ElementAtOrDefault<TSource>(IEnumerable<TSource> source, int index)
        {
            Check(source, "source");
            if (index >= 0)
                foreach (TSource item in source)
                    if (index-- == 0) return item;
            return default(TSource);
        }

        // ---- aggregates ----------------------------------------------------------------------

        public static int Sum(IEnumerable<int> source) { Check(source, "source"); int sum = 0; foreach (int v in source) sum = checked(sum + v); return sum; }
        public static long Sum(IEnumerable<long> source) { Check(source, "source"); long sum = 0; foreach (long v in source) sum = checked(sum + v); return sum; }
        public static double Sum(IEnumerable<double> source) { Check(source, "source"); double sum = 0; foreach (double v in source) sum += v; return sum; }
        public static float Sum(IEnumerable<float> source) { Check(source, "source"); double sum = 0; foreach (float v in source) sum += v; return (float)sum; }

        public static int Sum<TSource>(IEnumerable<TSource> source, Func<TSource, int> selector)
        { Check(source, "source"); Check(selector, "selector"); int sum = 0; foreach (TSource v in source) sum = checked(sum + selector(v)); return sum; }

        public static long Sum<TSource>(IEnumerable<TSource> source, Func<TSource, long> selector)
        { Check(source, "source"); Check(selector, "selector"); long sum = 0; foreach (TSource v in source) sum = checked(sum + selector(v)); return sum; }

        public static double Sum<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector)
        { Check(source, "source"); Check(selector, "selector"); double sum = 0; foreach (TSource v in source) sum += selector(v); return sum; }

        public static float Sum<TSource>(IEnumerable<TSource> source, Func<TSource, float> selector)
        { Check(source, "source"); Check(selector, "selector"); double sum = 0; foreach (TSource v in source) sum += selector(v); return (float)sum; }

        // decimal and nullable aggregates (.NET: nulls are skipped; Sum of nothing is 0, Average/Min/Max of
        // no non-null values is null for nullable overloads).
        public static decimal Sum(IEnumerable<decimal> source) { Check(source, "source"); decimal sum = 0; foreach (decimal v in source) sum += v; return sum; }
        public static decimal Sum<TSource>(IEnumerable<TSource> source, Func<TSource, decimal> selector)
        { Check(source, "source"); Check(selector, "selector"); decimal sum = 0; foreach (TSource v in source) sum += selector(v); return sum; }
        public static decimal Average(IEnumerable<decimal> source)
        {
            Check(source, "source");
            decimal sum = 0; long n = 0;
            foreach (decimal v in source) { sum += v; n++; }
            if (n == 0) ThrowHelper.NoElements();
            return sum / n;
        }
        public static decimal Average<TSource>(IEnumerable<TSource> source, Func<TSource, decimal> selector) => Average(Select(source, selector));
        public static decimal Min(IEnumerable<decimal> source) => MinMax(source, null, 1);
        public static decimal Max(IEnumerable<decimal> source) => MinMax(source, null, -1);
        public static decimal Min<TSource>(IEnumerable<TSource> source, Func<TSource, decimal> selector) => MinMax(Select(source, selector), null, 1);
        public static decimal Max<TSource>(IEnumerable<TSource> source, Func<TSource, decimal> selector) => MinMax(Select(source, selector), null, -1);

        public static int? Sum(IEnumerable<int?> source) { Check(source, "source"); int sum = 0; foreach (int? v in source) if (v.HasValue) sum = checked(sum + v.Value); return sum; }
        public static long? Sum(IEnumerable<long?> source) { Check(source, "source"); long sum = 0; foreach (long? v in source) if (v.HasValue) sum = checked(sum + v.Value); return sum; }
        public static double? Sum(IEnumerable<double?> source) { Check(source, "source"); double sum = 0; foreach (double? v in source) if (v.HasValue) sum += v.Value; return sum; }
        public static decimal? Sum(IEnumerable<decimal?> source) { Check(source, "source"); decimal sum = 0; foreach (decimal? v in source) if (v.HasValue) sum += v.Value; return sum; }
        public static int? Sum<TSource>(IEnumerable<TSource> source, Func<TSource, int?> selector) => Sum(Select(source, selector));
        public static long? Sum<TSource>(IEnumerable<TSource> source, Func<TSource, long?> selector) => Sum(Select(source, selector));
        public static double? Sum<TSource>(IEnumerable<TSource> source, Func<TSource, double?> selector) => Sum(Select(source, selector));
        public static decimal? Sum<TSource>(IEnumerable<TSource> source, Func<TSource, decimal?> selector) => Sum(Select(source, selector));
        public static double? Average(IEnumerable<int?> source)
        {
            Check(source, "source");
            long sum = 0; long n = 0;
            foreach (int? v in source) if (v.HasValue) { sum += v.Value; n++; }
            return n == 0 ? null : (double)sum / n;
        }
        public static double? Average(IEnumerable<double?> source)
        {
            Check(source, "source");
            double sum = 0; long n = 0;
            foreach (double? v in source) if (v.HasValue) { sum += v.Value; n++; }
            return n == 0 ? null : sum / n;
        }
        public static double? Average<TSource>(IEnumerable<TSource> source, Func<TSource, int?> selector) => Average(Select(source, selector));
        public static double? Average<TSource>(IEnumerable<TSource> source, Func<TSource, double?> selector) => Average(Select(source, selector));
        public static int? Min(IEnumerable<int?> source) { Check(source, "source"); int? best = null; foreach (int? v in source) if (v.HasValue && (!best.HasValue || v.Value < best.Value)) best = v; return best; }
        public static int? Max(IEnumerable<int?> source) { Check(source, "source"); int? best = null; foreach (int? v in source) if (v.HasValue && (!best.HasValue || v.Value > best.Value)) best = v; return best; }
        public static int? Min<TSource>(IEnumerable<TSource> source, Func<TSource, int?> selector) => Min(Select(source, selector));
        public static int? Max<TSource>(IEnumerable<TSource> source, Func<TSource, int?> selector) => Max(Select(source, selector));

        public static double Average(IEnumerable<int> source)
        {
            Check(source, "source");
            long sum = 0; long count = 0;
            foreach (int v in source) { sum = checked(sum + v); count++; }
            if (count == 0) ThrowHelper.NoElements();
            return (double)sum / count;
        }

        public static double Average(IEnumerable<long> source)
        {
            Check(source, "source");
            long sum = 0; long count = 0;
            foreach (long v in source) { sum = checked(sum + v); count++; }
            if (count == 0) ThrowHelper.NoElements();
            return (double)sum / count;
        }

        public static double Average(IEnumerable<double> source)
        {
            Check(source, "source");
            double sum = 0; long count = 0;
            foreach (double v in source) { sum += v; count++; }
            if (count == 0) ThrowHelper.NoElements();
            return sum / count;
        }

        public static float Average(IEnumerable<float> source)
        {
            Check(source, "source");
            double sum = 0; long count = 0;
            foreach (float v in source) { sum += v; count++; }
            if (count == 0) ThrowHelper.NoElements();
            return (float)(sum / count);
        }

        public static double Average<TSource>(IEnumerable<TSource> source, Func<TSource, int> selector) => Average(Select(source, selector));
        public static double Average<TSource>(IEnumerable<TSource> source, Func<TSource, long> selector) => Average(Select(source, selector));
        public static double Average<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector) => Average(Select(source, selector));
        public static float Average<TSource>(IEnumerable<TSource> source, Func<TSource, float> selector) => Average(Select(source, selector));

        public static int Min(IEnumerable<int> source)
        {
            Check(source, "source");
            using (IEnumerator<int> e = source.GetEnumerator())
            {
                if (!e.MoveNext()) ThrowHelper.NoElements();
                int value = e.Current;
                while (e.MoveNext()) if (e.Current < value) value = e.Current;
                return value;
            }
        }

        public static int Max(IEnumerable<int> source)
        {
            Check(source, "source");
            using (IEnumerator<int> e = source.GetEnumerator())
            {
                if (!e.MoveNext()) ThrowHelper.NoElements();
                int value = e.Current;
                while (e.MoveNext()) if (e.Current > value) value = e.Current;
                return value;
            }
        }

        public static long Min(IEnumerable<long> source)
        {
            Check(source, "source");
            using (IEnumerator<long> e = source.GetEnumerator())
            {
                if (!e.MoveNext()) ThrowHelper.NoElements();
                long value = e.Current;
                while (e.MoveNext()) if (e.Current < value) value = e.Current;
                return value;
            }
        }

        public static long Max(IEnumerable<long> source)
        {
            Check(source, "source");
            using (IEnumerator<long> e = source.GetEnumerator())
            {
                if (!e.MoveNext()) ThrowHelper.NoElements();
                long value = e.Current;
                while (e.MoveNext()) if (e.Current > value) value = e.Current;
                return value;
            }
        }

        public static double Min(IEnumerable<double> source)
        {
            Check(source, "source");
            using (IEnumerator<double> e = source.GetEnumerator())
            {
                if (!e.MoveNext()) ThrowHelper.NoElements();
                double value = e.Current;
                if (value != value) return value;   // NaN is the minimum
                while (e.MoveNext())
                {
                    double x = e.Current;
                    if (x < value) value = x;
                    else if (x != x) return x;
                }
                return value;
            }
        }

        public static double Max(IEnumerable<double> source)
        {
            Check(source, "source");
            using (IEnumerator<double> e = source.GetEnumerator())
            {
                if (!e.MoveNext()) ThrowHelper.NoElements();
                double value = e.Current;
                while (value != value)
                {
                    if (!e.MoveNext()) return value;
                    value = e.Current;
                }
                while (e.MoveNext())
                {
                    double x = e.Current;
                    if (x > value) value = x;
                }
                return value;
            }
        }

        public static float Min(IEnumerable<float> source) => (float)Min(Select(source, (float x) => (double)x));
        public static float Max(IEnumerable<float> source) => (float)Max(Select(source, (float x) => (double)x));

        public static int Min<TSource>(IEnumerable<TSource> source, Func<TSource, int> selector) => Min(Select(source, selector));
        public static int Max<TSource>(IEnumerable<TSource> source, Func<TSource, int> selector) => Max(Select(source, selector));
        public static long Min<TSource>(IEnumerable<TSource> source, Func<TSource, long> selector) => Min(Select(source, selector));
        public static long Max<TSource>(IEnumerable<TSource> source, Func<TSource, long> selector) => Max(Select(source, selector));
        public static double Min<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector) => Min(Select(source, selector));
        public static double Max<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector) => Max(Select(source, selector));
        public static float Min<TSource>(IEnumerable<TSource> source, Func<TSource, float> selector) => Min(Select(source, selector));
        public static float Max<TSource>(IEnumerable<TSource> source, Func<TSource, float> selector) => Max(Select(source, selector));

        private static TSource MinMax<TSource>(IEnumerable<TSource> source, IComparer<TSource> comparer, int sign)
        {
            Check(source, "source");
            bool useDefault = comparer == null || (object)comparer == (object)Comparer<TSource>.Default;
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                TSource value = default(TSource);
                if (!CoreIntrinsics.IsValueType<TSource>())
                {
                    // Reference types: nulls are skipped; an empty (or all-null) sequence yields null.
                    do
                    {
                        if (!e.MoveNext()) return value;
                        value = e.Current;
                    } while (value == null);
                    while (e.MoveNext())
                    {
                        TSource next = e.Current;
                        if (next != null && sign * (useDefault ? CoreIntrinsics.Compare(next, value) : comparer.Compare(next, value)) < 0) value = next;
                    }
                    return value;
                }
                if (!e.MoveNext()) ThrowHelper.NoElements();
                value = e.Current;
                while (e.MoveNext())
                {
                    TSource next = e.Current;
                    if (sign * (useDefault ? CoreIntrinsics.Compare(next, value) : comparer.Compare(next, value)) < 0) value = next;
                }
                return value;
            }
        }

        public static TSource Min<TSource>(IEnumerable<TSource> source) => MinMax(source, null, 1);
        public static TSource Min<TSource>(IEnumerable<TSource> source, IComparer<TSource> comparer) => MinMax(source, comparer, 1);
        public static TSource Max<TSource>(IEnumerable<TSource> source) => MinMax(source, null, -1);
        public static TSource Max<TSource>(IEnumerable<TSource> source, IComparer<TSource> comparer) => MinMax(source, comparer, -1);
        public static TResult Min<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> selector) => MinMax(Select(source, selector), null, 1);
        public static TResult Max<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> selector) => MinMax(Select(source, selector), null, -1);

        private static TSource MinMaxBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer, int sign)
        {
            Check(source, "source"); Check(keySelector, "keySelector");
            bool useDefault = comparer == null || (object)comparer == (object)Comparer<TKey>.Default;
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                if (!e.MoveNext())
                {
                    if (CoreIntrinsics.IsValueType<TSource>()) ThrowHelper.NoElements();
                    return default(TSource);
                }
                TSource value = e.Current;
                TKey key = keySelector(value);
                while (e.MoveNext())
                {
                    TSource next = e.Current;
                    TKey nextKey = keySelector(next);
                    if (sign * (useDefault ? CoreIntrinsics.Compare(nextKey, key) : comparer.Compare(nextKey, key)) < 0) { key = nextKey; value = next; }
                }
                return value;
            }
        }

        public static TSource MinBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => MinMaxBy(source, keySelector, null, 1);
        public static TSource MinBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) => MinMaxBy(source, keySelector, comparer, 1);
        public static TSource MaxBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => MinMaxBy(source, keySelector, null, -1);
        public static TSource MaxBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) => MinMaxBy(source, keySelector, comparer, -1);

        public static TSource Aggregate<TSource>(IEnumerable<TSource> source, Func<TSource, TSource, TSource> func)
        {
            Check(source, "source"); Check(func, "func");
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                if (!e.MoveNext()) ThrowHelper.NoElements();
                TSource result = e.Current;
                while (e.MoveNext()) result = func(result, e.Current);
                return result;
            }
        }

        public static TAccumulate Aggregate<TSource, TAccumulate>(IEnumerable<TSource> source, TAccumulate seed, Func<TAccumulate, TSource, TAccumulate> func)
        {
            Check(source, "source"); Check(func, "func");
            TAccumulate result = seed;
            foreach (TSource element in source) result = func(result, element);
            return result;
        }

        public static TResult Aggregate<TSource, TAccumulate, TResult>(IEnumerable<TSource> source, TAccumulate seed,
            Func<TAccumulate, TSource, TAccumulate> func, Func<TAccumulate, TResult> resultSelector)
        {
            Check(source, "source"); Check(func, "func"); Check(resultSelector, "resultSelector");
            TAccumulate result = seed;
            foreach (TSource element in source) result = func(result, element);
            return resultSelector(result);
        }
    }
}
