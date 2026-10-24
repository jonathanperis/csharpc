// Lazy LINQ operators as hand-written iterator classes (deferred execution like .NET's Enumerable;
// no `yield` dependency). Each iterator is its own first enumerator, like .NET's Iterator<T>.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CsharpcCoreLib
{
    internal abstract class Iterator<T> : IEnumerable<T>, IEnumerator<T>
    {
        /// <summary>
        /// .NET's Iterator&lt;T&gt;.GetCount(onlyIfCheap: true) for TryGetNonEnumeratedCount: the count when it is
        /// known without enumerating, else -1. Operators whose .NET iterator csharpc doesn't mirror closely
        /// enough to answer exactly say so instead of guessing.
        /// </summary>
        internal virtual int CheapCount() =>
            throw new NotSupportedException("Enumerable.TryGetNonEnumeratedCount over this LINQ operator is not supported by csharpc yet.");

        protected int _state;      // 0 = not enumerated yet, 1 = start, 2+ = running, -1 = done
        protected T _current;

        public abstract Iterator<T> Clone();
        public abstract bool MoveNext();

        public IEnumerator<T> GetEnumerator()
        {
            Iterator<T> enumerator = _state == 0 ? this : Clone();
            enumerator._state = 1;
            return enumerator;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public T Current => _current;
        object IEnumerator.Current => CoreIntrinsics.Box(_current);
        public virtual void Dispose() { _current = default(T); _state = -1; }
        public void Reset() => throw new NotSupportedException("Specified method is not supported.");
    }

    internal sealed class WhereIterator<T> : Iterator<T>
    {
        internal override int CheapCount() => -1;

        private readonly IEnumerable<T> _source;
        private readonly Func<T, bool> _predicate;
        private readonly Func<T, int, bool> _indexed;
        private IEnumerator<T> _e;
        private int _index;

        public WhereIterator(IEnumerable<T> source, Func<T, bool> predicate, Func<T, int, bool> indexed)
        { _source = source; _predicate = predicate; _indexed = indexed; }

        public override Iterator<T> Clone() => new WhereIterator<T>(_source, _predicate, _indexed);

        public override bool MoveNext()
        {
            if (_state == 1) { _e = _source.GetEnumerator(); _index = -1; _state = 2; }
            if (_state == 2)
            {
                while (_e.MoveNext())
                {
                    T item = _e.Current;
                    _index = checked(_index + 1);
                    if (_predicate != null ? _predicate(item) : _indexed(item, _index)) { _current = item; return true; }
                }
                Dispose();
            }
            return false;
        }

        public override void Dispose() { if (_e != null) { _e.Dispose(); _e = null; } base.Dispose(); }
    }

    internal sealed class SelectIterator<TSource, TResult> : Iterator<TResult>
    {
        // .NET never counts a Select without running its selector (which may have side effects).
        internal override int CheapCount() => -1;

        private readonly IEnumerable<TSource> _source;
        private readonly Func<TSource, TResult> _selector;
        private readonly Func<TSource, int, TResult> _indexed;
        private IEnumerator<TSource> _e;
        private int _index;

        public SelectIterator(IEnumerable<TSource> source, Func<TSource, TResult> selector, Func<TSource, int, TResult> indexed)
        { _source = source; _selector = selector; _indexed = indexed; }

        public override Iterator<TResult> Clone() => new SelectIterator<TSource, TResult>(_source, _selector, _indexed);

        public override bool MoveNext()
        {
            if (_state == 1) { _e = _source.GetEnumerator(); _index = -1; _state = 2; }
            if (_state == 2)
            {
                if (_e.MoveNext())
                {
                    _index = checked(_index + 1);
                    _current = _selector != null ? _selector(_e.Current) : _indexed(_e.Current, _index);
                    return true;
                }
                Dispose();
            }
            return false;
        }

        public override void Dispose() { if (_e != null) { _e.Dispose(); _e = null; } base.Dispose(); }
    }

    internal sealed class SelectManyIterator<TSource, TCollection, TResult> : Iterator<TResult>
    {
        internal override int CheapCount() => -1;

        private readonly IEnumerable<TSource> _source;
        private readonly Func<TSource, IEnumerable<TCollection>> _collection;
        private readonly Func<TSource, int, IEnumerable<TCollection>> _indexed;
        private readonly Func<TSource, TCollection, TResult> _result;   // null: TCollection == TResult
        private IEnumerator<TSource> _outer;
        private IEnumerator<TCollection> _inner;
        private TSource _item;
        private int _index;

        public SelectManyIterator(IEnumerable<TSource> source, Func<TSource, IEnumerable<TCollection>> collection,
            Func<TSource, int, IEnumerable<TCollection>> indexed, Func<TSource, TCollection, TResult> result)
        { _source = source; _collection = collection; _indexed = indexed; _result = result; }

        public override Iterator<TResult> Clone() => new SelectManyIterator<TSource, TCollection, TResult>(_source, _collection, _indexed, _result);

        public override bool MoveNext()
        {
            if (_state == 1) { _outer = _source.GetEnumerator(); _index = -1; _state = 2; }
            while (_state == 2 || _state == 3)
            {
                if (_state == 2)
                {
                    if (!_outer.MoveNext()) break;
                    _item = _outer.Current;
                    _index = checked(_index + 1);
                    _inner = (_collection != null ? _collection(_item) : _indexed(_item, _index)).GetEnumerator();
                    _state = 3;
                }
                if (_inner.MoveNext())
                {
                    TCollection element = _inner.Current;
                    _current = _result != null ? _result(_item, element) : (TResult)(object)element;
                    return true;
                }
                _inner.Dispose();
                _inner = null;
                _state = 2;
            }
            Dispose();
            return false;
        }

        public override void Dispose()
        {
            if (_inner != null) { _inner.Dispose(); _inner = null; }
            if (_outer != null) { _outer.Dispose(); _outer = null; }
            base.Dispose();
        }
    }

    /// <summary>Take / Skip / TakeWhile / SkipWhile in one iterator.</summary>
    internal sealed class PartitionIterator<T> : Iterator<T>
    {
        // Take/Skip over a list are .NET's ListPartition (count known); TakeWhile/SkipWhile are plain iterators.
        internal override int CheapCount()
        {
            if (_kind >= 2) return -1;
            if (_source is IList<T> list)
                return _kind == 0 ? Math.Min(list.Count, Math.Max(0, _count)) : Math.Max(0, list.Count - Math.Max(0, _count));
            return base.CheapCount();
        }

        private readonly IEnumerable<T> _source;
        private readonly int _kind;        // 0 Take(n), 1 Skip(n), 2 TakeWhile, 3 SkipWhile
        private readonly int _count;
        private readonly Func<T, bool> _predicate;
        private readonly Func<T, int, bool> _indexed;
        private IEnumerator<T> _e;
        private int _taken;
        private bool _yielding;

        public PartitionIterator(IEnumerable<T> source, int kind, int count, Func<T, bool> predicate, Func<T, int, bool> indexed)
        { _source = source; _kind = kind; _count = count; _predicate = predicate; _indexed = indexed; }

        public override Iterator<T> Clone() => new PartitionIterator<T>(_source, _kind, _count, _predicate, _indexed);

        private bool Test(T item, int index) => _predicate != null ? _predicate(item) : _indexed(item, index);

        public override bool MoveNext()
        {
            if (_state == 1)
            {
                if (_kind == 0 && _count <= 0) { Dispose(); return false; }
                _e = _source.GetEnumerator();
                _taken = 0;
                _yielding = false;
                _state = 2;
            }
            if (_state == 2)
            {
                switch (_kind)
                {
                    case 0:
                        if (_taken < _count && _e.MoveNext()) { _taken++; _current = _e.Current; return true; }
                        break;
                    case 1:
                        while (_taken < _count && _e.MoveNext()) _taken++;
                        _taken = int.MaxValue;
                        if (_e.MoveNext()) { _current = _e.Current; return true; }
                        break;
                    case 2:
                        if (_e.MoveNext())
                        {
                            T item = _e.Current;
                            if (Test(item, _taken++)) { _current = item; return true; }
                        }
                        break;
                    default:
                        while (_e.MoveNext())
                        {
                            T item = _e.Current;
                            if (_yielding || !Test(item, _taken++)) { _yielding = true; _current = item; return true; }
                        }
                        break;
                }
                Dispose();
            }
            return false;
        }

        public override void Dispose() { if (_e != null) { _e.Dispose(); _e = null; } base.Dispose(); }
    }

    internal sealed class ConcatIterator<T> : Iterator<T>
    {
        internal override int CheapCount() =>
            EnumerableImpl.TryGetNonEnumeratedCount(_first, out int first) && EnumerableImpl.TryGetNonEnumeratedCount(_second, out int second)
                ? checked(first + second) : -1;

        private readonly IEnumerable<T> _first;
        private readonly IEnumerable<T> _second;
        private IEnumerator<T> _e;

        public ConcatIterator(IEnumerable<T> first, IEnumerable<T> second) { _first = first; _second = second; }

        public override Iterator<T> Clone() => new ConcatIterator<T>(_first, _second);

        public override bool MoveNext()
        {
            if (_state == 1) { _e = _first.GetEnumerator(); _state = 2; }
            if (_state == 2)
            {
                if (_e.MoveNext()) { _current = _e.Current; return true; }
                _e.Dispose();
                _e = _second.GetEnumerator();
                _state = 3;
            }
            if (_state == 3)
            {
                if (_e.MoveNext()) { _current = _e.Current; return true; }
                Dispose();
            }
            return false;
        }

        public override void Dispose() { if (_e != null) { _e.Dispose(); _e = null; } base.Dispose(); }
    }

    /// <summary>Distinct / DistinctBy / Union / Intersect / Except, streaming with a HashSet.</summary>
    internal sealed class SetIterator<T, TKey> : Iterator<T>
    {
        internal override int CheapCount() => -1;

        private readonly IEnumerable<T> _first;
        private readonly IEnumerable<T> _second;
        private readonly int _kind;   // 0 distinct(first), 1 union, 2 intersect, 3 except
        private readonly Func<T, TKey> _key;
        private readonly IEqualityComparer<TKey> _comparer;
        private HashSet<TKey> _set;
        private IEnumerator<T> _e;

        public SetIterator(IEnumerable<T> first, IEnumerable<T> second, int kind, Func<T, TKey> key, IEqualityComparer<TKey> comparer)
        { _first = first; _second = second; _kind = kind; _key = key; _comparer = comparer; }

        public override Iterator<T> Clone() => new SetIterator<T, TKey>(_first, _second, _kind, _key, _comparer);

        private TKey KeyOf(T item) => _key != null ? _key(item) : (TKey)(object)item;

        public override bool MoveNext()
        {
            if (_state == 1)
            {
                _set = new HashSet<TKey>(_comparer);
                if (_kind >= 2) foreach (T item in _second) _set.Add(KeyOf(item));
                _e = _first.GetEnumerator();
                _state = 2;
            }
            while (_state == 2 || _state == 3)
            {
                while (_e.MoveNext())
                {
                    T item = _e.Current;
                    TKey key = KeyOf(item);
                    bool yield = _kind == 2 ? _set.Remove(key) : _set.Add(key);
                    if (yield) { _current = item; return true; }
                }
                if (_kind == 1 && _state == 2)
                {
                    _e.Dispose();
                    _e = _second.GetEnumerator();
                    _state = 3;
                    continue;
                }
                break;
            }
            Dispose();
            return false;
        }

        public override void Dispose() { if (_e != null) { _e.Dispose(); _e = null; } base.Dispose(); }
    }

    internal sealed class ZipIterator<TFirst, TSecond, TResult> : Iterator<TResult>
    {
        internal override int CheapCount() => -1;

        private readonly IEnumerable<TFirst> _first;
        private readonly IEnumerable<TSecond> _second;
        private readonly Func<TFirst, TSecond, TResult> _selector;
        private IEnumerator<TFirst> _e1;
        private IEnumerator<TSecond> _e2;

        public ZipIterator(IEnumerable<TFirst> first, IEnumerable<TSecond> second, Func<TFirst, TSecond, TResult> selector)
        { _first = first; _second = second; _selector = selector; }

        public override Iterator<TResult> Clone() => new ZipIterator<TFirst, TSecond, TResult>(_first, _second, _selector);

        public override bool MoveNext()
        {
            if (_state == 1) { _e1 = _first.GetEnumerator(); _e2 = _second.GetEnumerator(); _state = 2; }
            if (_state == 2)
            {
                if (_e1.MoveNext() && _e2.MoveNext()) { _current = _selector(_e1.Current, _e2.Current); return true; }
                Dispose();
            }
            return false;
        }

        public override void Dispose()
        {
            if (_e1 != null) { _e1.Dispose(); _e1 = null; }
            if (_e2 != null) { _e2.Dispose(); _e2 = null; }
            base.Dispose();
        }
    }

    /// <summary>Operators that buffer the whole source when enumeration starts (Reverse, TakeLast, SkipLast, Chunk...).</summary>
    internal sealed class BufferedIterator<TSource, TResult> : Iterator<TResult>
    {
        private readonly IEnumerable<TSource> _source;
        private readonly Func<List<TSource>, List<TResult>> _transform;
        private List<TResult> _items;
        private int _index;

        public BufferedIterator(IEnumerable<TSource> source, Func<List<TSource>, List<TResult>> transform) { _source = source; _transform = transform; }

        public override Iterator<TResult> Clone() => new BufferedIterator<TSource, TResult>(_source, _transform);

        public override bool MoveNext()
        {
            if (_state == 1) { _items = _transform(new List<TSource>(_source)); _index = 0; _state = 2; }
            if (_state == 2)
            {
                if (_index < _items.Count) { _current = _items[_index++]; return true; }
                Dispose();
            }
            return false;
        }
    }

    internal sealed class RangeIterator : Iterator<int>
    {
        internal override int CheapCount() => _count;

        private readonly int _start;
        private readonly int _count;
        private int _i;

        public RangeIterator(int start, int count) { _start = start; _count = count; }
        public override Iterator<int> Clone() => new RangeIterator(_start, _count);

        public override bool MoveNext()
        {
            if (_state == 1) { _i = 0; _state = 2; }
            if (_state == 2)
            {
                if (_i < _count) { _current = _start + _i; _i++; return true; }
                Dispose();
            }
            return false;
        }
    }

    internal sealed class RepeatIterator<T> : Iterator<T>
    {
        internal override int CheapCount() => _count;

        private readonly T _element;
        private readonly int _count;
        private int _i;

        public RepeatIterator(T element, int count) { _element = element; _count = count; }
        public override Iterator<T> Clone() => new RepeatIterator<T>(_element, _count);

        public override bool MoveNext()
        {
            if (_state == 1) { _i = 0; _state = 2; }
            if (_state == 2)
            {
                if (_i < _count) { _current = _element; _i++; return true; }
                Dispose();
            }
            return false;
        }
    }

    internal sealed class AppendIterator<T> : Iterator<T>
    {
        internal override int CheapCount()
        {
            if (_source is Iterator<T> iterator)
            {
                int count = iterator.CheapCount();
                return count == -1 ? -1 : count + 1;
            }
            return _source is ICollection<T> collection ? collection.Count + 1 : -1;
        }

        private readonly IEnumerable<T> _source;
        private readonly T _item;
        private readonly bool _append;
        private IEnumerator<T> _e;

        public AppendIterator(IEnumerable<T> source, T item, bool append) { _source = source; _item = item; _append = append; }
        public override Iterator<T> Clone() => new AppendIterator<T>(_source, _item, _append);

        public override bool MoveNext()
        {
            if (_state == 1)
            {
                _e = _source.GetEnumerator();
                _state = 2;
                if (!_append) { _current = _item; return true; }
            }
            if (_state == 2)
            {
                if (_e.MoveNext()) { _current = _e.Current; return true; }
                _state = 3;
                if (_append) { _current = _item; return true; }
            }
            Dispose();
            return false;
        }

        public override void Dispose() { if (_e != null) { _e.Dispose(); _e = null; } base.Dispose(); }
    }

    internal sealed class CastIterator<TResult> : Iterator<TResult>
    {
        internal override int CheapCount() => -1;

        private readonly IEnumerable _source;
        private readonly bool _ofType;
        private IEnumerator _e;

        public CastIterator(IEnumerable source, bool ofType) { _source = source; _ofType = ofType; }
        public override Iterator<TResult> Clone() => new CastIterator<TResult>(_source, _ofType);

        public override bool MoveNext()
        {
            if (_state == 1) { _e = _source.GetEnumerator(); _state = 2; }
            if (_state == 2)
            {
                while (_e.MoveNext())
                {
                    object item = _e.Current;
                    if (!_ofType) { _current = (TResult)item; return true; }
                    if (item is TResult typed) { _current = typed; return true; }
                }
                Dispose();
            }
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Ordering: stable sort of an index map with a chain of key comparers (like EnumerableSorter).
    // ---------------------------------------------------------------------------------------------

    internal abstract class EnumerableSorter<TElement>
    {
        internal abstract void ComputeKeys(TElement[] elements, int count);
        internal abstract int CompareAnyKeys(int index1, int index2);

        internal int[] Sort(TElement[] elements, int count)
        {
            ComputeKeys(elements, count);
            int[] map = new int[count];
            for (int i = 0; i < count; i++) map[i] = i;
            int[] tmp = new int[count];
            MergeSort(map, tmp, 0, count);
            return map;
        }

        private void MergeSort(int[] a, int[] tmp, int lo, int hi)
        {
            if (hi - lo < 2) return;
            if (hi - lo <= 12)
            {
                for (int i = lo + 1; i < hi; i++)
                {
                    int x = a[i];
                    int j = i - 1;
                    while (j >= lo && CompareAnyKeys(a[j], x) > 0) { a[j + 1] = a[j]; j--; }
                    a[j + 1] = x;
                }
                return;
            }
            int mid = (lo + hi) >> 1;
            MergeSort(a, tmp, lo, mid);
            MergeSort(a, tmp, mid, hi);
            int l = lo, r = mid, k = lo;
            while (l < mid && r < hi) tmp[k++] = CompareAnyKeys(a[l], a[r]) <= 0 ? a[l++] : a[r++];
            while (l < mid) tmp[k++] = a[l++];
            while (r < hi) tmp[k++] = a[r++];
            for (int i = lo; i < hi; i++) a[i] = tmp[i];
        }
    }

    internal sealed class EnumerableSorter<TElement, TKey> : EnumerableSorter<TElement>
    {
        private readonly Func<TElement, TKey> _keySelector;
        private readonly IComparer<TKey> _comparer;
        private readonly bool _descending;
        private readonly EnumerableSorter<TElement> _next;
        private TKey[] _keys;

        public EnumerableSorter(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending, EnumerableSorter<TElement> next)
        {
            _keySelector = keySelector;
            _comparer = comparer != null && (object)comparer != (object)Comparer<TKey>.Default ? comparer : null;
            _descending = descending;
            _next = next;
        }

        internal override void ComputeKeys(TElement[] elements, int count)
        {
            _keys = new TKey[count];
            for (int i = 0; i < count; i++) _keys[i] = _keySelector(elements[i]);
            if (_next != null) _next.ComputeKeys(elements, count);
        }

        internal override int CompareAnyKeys(int index1, int index2)
        {
            int c = _comparer == null ? CoreIntrinsics.Compare(_keys[index1], _keys[index2]) : _comparer.Compare(_keys[index1], _keys[index2]);
            if (c == 0)
            {
                if (_next == null) return index1 - index2;
                return _next.CompareAnyKeys(index1, index2);
            }
            return (_descending != (c > 0)) ? 1 : -1;
        }
    }

    internal abstract class OrderedEnumerable<TElement> : IOrderedEnumerable<TElement>
    {
        internal IEnumerable<TElement> _source;

        internal abstract EnumerableSorter<TElement> GetEnumerableSorter(EnumerableSorter<TElement> next);

        public IEnumerator<TElement> GetEnumerator()
        {
            TElement[] buffer = new List<TElement>(_source).ToArray();
            int[] map = GetEnumerableSorter(null).Sort(buffer, buffer.Length);
            TElement[] sorted = new TElement[buffer.Length];
            for (int i = 0; i < map.Length; i++) sorted[i] = buffer[map[i]];
            return new SimpleEnumerator<TElement>(sorted);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IOrderedEnumerable<TElement> CreateOrderedEnumerable<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending) =>
            ThenByCore(keySelector, comparer, descending);

        /// <summary>Non-virtual entry used by ThenBy (generic virtual/interface methods aren't dispatchable).</summary>
        internal IOrderedEnumerable<TElement> ThenByCore<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending) =>
            new OrderedEnumerable<TElement, TKey>(_source, keySelector, comparer, descending, this);
    }

    internal sealed class OrderedEnumerable<TElement, TKey> : OrderedEnumerable<TElement>
    {
        private readonly OrderedEnumerable<TElement> _parent;
        private readonly Func<TElement, TKey> _keySelector;
        private readonly IComparer<TKey> _comparer;
        private readonly bool _descending;

        public OrderedEnumerable(IEnumerable<TElement> source, Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending,
            OrderedEnumerable<TElement> parent)
        {
            _source = source;
            _keySelector = keySelector;
            _comparer = comparer;
            _descending = descending;
            _parent = parent;
        }

        internal override EnumerableSorter<TElement> GetEnumerableSorter(EnumerableSorter<TElement> next)
        {
            EnumerableSorter<TElement> sorter = new EnumerableSorter<TElement, TKey>(_keySelector, _comparer, _descending, next);
            if (_parent != null) sorter = _parent.GetEnumerableSorter(sorter);
            return sorter;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Grouping / Lookup (groups in order of first key appearance, null keys allowed).
    // ---------------------------------------------------------------------------------------------

    internal sealed class Grouping<TKey, TElement> : IGrouping<TKey, TElement>, IList<TElement>, IReadOnlyList<TElement>
    {
        internal readonly TKey _key;
        internal readonly List<TElement> _elements = new List<TElement>();

        public Grouping(TKey key) { _key = key; }

        public TKey Key => _key;
        public int Count => _elements.Count;
        public bool IsReadOnly => true;
        public TElement this[int index] { get => _elements[index]; set => ThrowHelper.NotSupported(); }
        public IEnumerator<TElement> GetEnumerator() => ((IEnumerable<TElement>)_elements).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public bool Contains(TElement item) => _elements.Contains(item);
        public int IndexOf(TElement item) => _elements.IndexOf(item);
        public void CopyTo(TElement[] array, int arrayIndex) => _elements.CopyTo(array, arrayIndex);
        public void Add(TElement item) => ThrowHelper.NotSupported();
        public void Clear() => ThrowHelper.NotSupported();
        public bool Remove(TElement item) { ThrowHelper.NotSupported(); return false; }
        public void Insert(int index, TElement item) => ThrowHelper.NotSupported();
        public void RemoveAt(int index) => ThrowHelper.NotSupported();
    }

    internal sealed class Lookup<TKey, TElement> : ILookup<TKey, TElement>
    {
        private readonly Dictionary<TKey, Grouping<TKey, TElement>> _groups;
        private readonly List<Grouping<TKey, TElement>> _ordered = new List<Grouping<TKey, TElement>>();
        private Grouping<TKey, TElement> _nullGroup;

        public Lookup(IEqualityComparer<TKey> comparer) { _groups = new Dictionary<TKey, Grouping<TKey, TElement>>(comparer); }

        internal Grouping<TKey, TElement> GetGrouping(TKey key, bool create)
        {
            if (key == null)
            {
                if (_nullGroup == null && create) { _nullGroup = new Grouping<TKey, TElement>(key); _ordered.Add(_nullGroup); }
                return _nullGroup;
            }
            if (_groups.TryGetValue(key, out var g)) return g;
            if (!create) return null;
            g = new Grouping<TKey, TElement>(key);
            _groups.Add(key, g);
            _ordered.Add(g);
            return g;
        }

        internal List<Grouping<TKey, TElement>> Groupings => _ordered;

        public int Count => _ordered.Count;

        public IEnumerable<TElement> this[TKey key]
        {
            get
            {
                var g = GetGrouping(key, false);
                return g != null ? (IEnumerable<TElement>)g : new TElement[0];
            }
        }

        public bool Contains(TKey key) => GetGrouping(key, false) != null;

        public IEnumerator<IGrouping<TKey, TElement>> GetEnumerator()
        {
            var items = new IGrouping<TKey, TElement>[_ordered.Count];
            for (int i = 0; i < items.Length; i++) items[i] = _ordered[i];
            return new SimpleEnumerator<IGrouping<TKey, TElement>>(items);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Re-evaluated on every enumeration (deferred), like Enumerable.GroupBy.</summary>
    internal sealed class DeferredEnumerable<T> : IEnumerable<T>
    {
        private readonly Func<IEnumerable<T>> _factory;
        public DeferredEnumerable(Func<IEnumerable<T>> factory) { _factory = factory; }
        public IEnumerator<T> GetEnumerator() => _factory().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
