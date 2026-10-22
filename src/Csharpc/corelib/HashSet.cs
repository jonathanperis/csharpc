// System.Collections.Generic.HashSet<T> — same buckets/entries/free-list design as .NET (and our
// Dictionary), so enumeration order matches .NET after any sequence of adds and removes.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Generic.HashSet`1")]
    public class HashSetImpl<T> : ISet<T>, IReadOnlySet<T>
    {
        internal struct Entry
        {
            public uint hashCode;
            public int next;
            public T value;
        }

        private const int StartOfFreeList = -3;

        private int[] _buckets;
        internal Entry[] _entries;
        internal int _count;
        private int _freeList;
        private int _freeCount;
        internal int _version;
        private IEqualityComparer<T> _comparer;

        public HashSetImpl() { }
        public HashSetImpl(IEqualityComparer<T> comparer) { SetComparer(comparer); }
        public HashSetImpl(int capacity) : this(capacity, null) { }

        public HashSetImpl(int capacity, IEqualityComparer<T> comparer)
        {
            if (capacity < 0) ThrowHelper.ArgumentOutOfRange("capacity", "Non-negative number required.");
            SetComparer(comparer);
            if (capacity > 0) Initialize(capacity);
        }

        public HashSetImpl(IEnumerable<T> collection) : this(collection, null) { }

        public HashSetImpl(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            SetComparer(comparer);
            if (collection is ICollection<T> c && c.Count > 0) Initialize(c.Count);
            UnionWith(collection);
        }

        private void SetComparer(IEqualityComparer<T> comparer)
        {
            if (comparer != null && (object)comparer != (object)EqualityComparer<T>.Default) _comparer = comparer;
        }

        public IEqualityComparer<T> Comparer => _comparer ?? EqualityComparer<T>.Default;
        public int Count => _count - _freeCount;
        public int Capacity => _entries == null ? 0 : _entries.Length;
        bool ICollection<T>.IsReadOnly => false;

        private bool ItemsEqual(T a, T b) => _comparer == null ? CoreIntrinsics.AreEqual(a, b) : _comparer.Equals(a, b);
        private uint Hash(T item) => item == null ? 0u : (uint)(_comparer == null ? CoreIntrinsics.HashOf(item) : _comparer.GetHashCode(item));

        private int Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            _freeList = -1;
            _buckets = new int[size];
            _entries = new Entry[size];
            return size;
        }

        internal int FindItemIndex(T item)
        {
            if (_buckets == null) return -1;
            uint hashCode = Hash(item);
            int i = _buckets[(int)(hashCode % (uint)_buckets.Length)] - 1;
            while ((uint)i < (uint)_entries.Length)
            {
                if (_entries[i].hashCode == hashCode && ItemsEqual(_entries[i].value, item)) return i;
                i = _entries[i].next;
            }
            return -1;
        }

        public bool Contains(T item) => FindItemIndex(item) >= 0;

        public bool Add(T item) => AddIfNotPresent(item, out _);

        void ICollection<T>.Add(T item) => AddIfNotPresent(item, out _);

        internal bool AddIfNotPresent(T value, out int location)
        {
            if (_buckets == null) Initialize(0);
            uint hashCode = Hash(value);
            int bucket = (int)(hashCode % (uint)_buckets.Length);
            int i = _buckets[bucket] - 1;
            while ((uint)i < (uint)_entries.Length)
            {
                if (_entries[i].hashCode == hashCode && ItemsEqual(_entries[i].value, value))
                {
                    location = i;
                    return false;
                }
                i = _entries[i].next;
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeCount--;
                _freeList = StartOfFreeList - _entries[_freeList].next;
            }
            else
            {
                if (_count == _entries.Length)
                {
                    Resize(HashHelpers.ExpandPrime(_count));
                    bucket = (int)(hashCode % (uint)_buckets.Length);
                }
                index = _count;
                _count++;
            }
            _entries[index].hashCode = hashCode;
            _entries[index].next = _buckets[bucket] - 1;
            _entries[index].value = value;
            _buckets[bucket] = index + 1;
            _version++;
            location = index;
            return true;
        }

        private void Resize(int newSize)
        {
            Entry[] entries = new Entry[newSize];
            for (int i = 0; i < _count; i++) entries[i] = _entries[i];
            _buckets = new int[newSize];
            for (int i = 0; i < _count; i++)
            {
                if (entries[i].next >= -1)
                {
                    int bucket = (int)(entries[i].hashCode % (uint)newSize);
                    entries[i].next = _buckets[bucket] - 1;
                    _buckets[bucket] = i + 1;
                }
            }
            _entries = entries;
        }

        public bool Remove(T item)
        {
            if (_buckets == null) return false;
            uint hashCode = Hash(item);
            int bucket = (int)(hashCode % (uint)_buckets.Length);
            int last = -1;
            int i = _buckets[bucket] - 1;
            while (i >= 0)
            {
                if (_entries[i].hashCode == hashCode && ItemsEqual(_entries[i].value, item))
                {
                    if (last < 0) _buckets[bucket] = _entries[i].next + 1;
                    else _entries[last].next = _entries[i].next;
                    _entries[i].next = StartOfFreeList - _freeList;
                    _entries[i].value = default(T);
                    _freeList = i;
                    _freeCount++;
                    return true;
                }
                last = i;
                i = _entries[i].next;
            }
            return false;
        }

        public void Clear()
        {
            int count = _count;
            if (count > 0)
            {
                for (int i = 0; i < _buckets.Length; i++) _buckets[i] = 0;
                for (int i = 0; i < count; i++) _entries[i] = default(Entry);
                _count = 0;
                _freeList = -1;
                _freeCount = 0;
            }
        }

        public bool TryGetValue(T equalValue, out T actualValue)
        {
            int i = FindItemIndex(equalValue);
            if (i >= 0) { actualValue = _entries[i].value; return true; }
            actualValue = default(T);
            return false;
        }

        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null) ThrowHelper.ArgumentNull("match");
            int removed = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].next >= -1)
                {
                    T value = _entries[i].value;
                    if (match(value) && Remove(value)) removed++;
                }
            }
            return removed;
        }

        public void CopyTo(T[] array) => CopyTo(array, 0, Count);
        public void CopyTo(T[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);

        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            for (int i = 0; i < _count && count > 0; i++)
            {
                if (_entries[i].next >= -1)
                {
                    array[arrayIndex++] = _entries[i].value;
                    count--;
                }
            }
        }

        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0) ThrowHelper.ArgumentOutOfRange("capacity", "Non-negative number required.");
            int current = Capacity;
            if (current >= capacity) return current;
            if (_buckets == null) return Initialize(capacity);
            int newSize = HashHelpers.GetPrime(capacity);
            Resize(newSize);
            return newSize;
        }

        public void TrimExcess()
        {
            int count = Count;
            int newSize = HashHelpers.GetPrime(count);
            Entry[] oldEntries = _entries;
            int oldCount = _count;
            _version++;
            Initialize(newSize);
            int newCount = 0;
            for (int i = 0; i < oldCount; i++)
            {
                if (oldEntries[i].next >= -1)
                {
                    int bucket = (int)(oldEntries[i].hashCode % (uint)newSize);
                    _entries[newCount] = oldEntries[i];
                    _entries[newCount].next = _buckets[bucket] - 1;
                    _buckets[bucket] = newCount + 1;
                    newCount++;
                }
            }
            _count = newCount;
            _freeCount = 0;
        }

        // ---- set operations ------------------------------------------------------------------

        private HashSet<T> AsSetWithSameComparer(IEnumerable<T> other)
        {
            if (other is HashSet<T> hs && (object)hs.Comparer == (object)Comparer) return hs;
            return null;
        }

        public void UnionWith(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            foreach (T item in other) AddIfNotPresent(item, out _);
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            if (Count == 0 || (object)other == (object)this) return;
            if (other is ICollection<T> c && c.Count == 0) { Clear(); return; }
            HashSet<T> set = AsSetWithSameComparer(other) ?? new HashSet<T>(other, Comparer);
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].next >= -1)
                {
                    T item = _entries[i].value;
                    if (!set.Contains(item)) Remove(item);
                }
            }
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            if (Count == 0) return;
            if ((object)other == (object)this) { Clear(); return; }
            foreach (T element in other) Remove(element);
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            if (Count == 0) { UnionWith(other); return; }
            if ((object)other == (object)this) { Clear(); return; }
            HashSet<T> unique = AsSetWithSameComparer(other);
            if (unique != null)
            {
                foreach (T item in unique)
                    if (!Remove(item)) AddIfNotPresent(item, out _);
                return;
            }
            // .NET's enumerable path: add first (marking), remove at the end.
            int originalCount = _count;
            var addedFromOther = new HashSet<int>();
            var toRemove = new HashSet<int>();
            foreach (T item in other)
            {
                if (AddIfNotPresent(item, out int location)) addedFromOther.Add(location);
                else if (location < originalCount && !addedFromOther.Contains(location)) toRemove.Add(location);
            }
            for (int i = 0; i < originalCount; i++)
                if (toRemove.Contains(i)) Remove(_entries[i].value);
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            if (Count == 0) return true;
            HashSet<T> set = AsSetWithSameComparer(other) ?? new HashSet<T>(other, Comparer);
            if (Count > set.Count) return false;
            foreach (T item in this) if (!set.Contains(item)) return false;
            return true;
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            HashSet<T> set = AsSetWithSameComparer(other) ?? new HashSet<T>(other, Comparer);
            return Count < set.Count && IsSubsetOf(set);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            foreach (T item in other) if (!Contains(item)) return false;
            return true;
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            HashSet<T> set = AsSetWithSameComparer(other) ?? new HashSet<T>(other, Comparer);
            return set.Count < Count && IsSupersetOf(set);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            if (Count == 0) return false;
            foreach (T item in other) if (Contains(item)) return true;
            return false;
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            if (other == null) ThrowHelper.ArgumentNull("other");
            HashSet<T> set = AsSetWithSameComparer(other) ?? new HashSet<T>(other, Comparer);
            if (Count != set.Count) return false;
            foreach (T item in set) if (!Contains(item)) return false;
            return true;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new EnumeratorObject(this);
        IEnumerator IEnumerable.GetEnumerator() => new EnumeratorObject(this);

        [Shadows("System.Collections.Generic.HashSet`1+Enumerator")]
        public struct Enumerator
        {
            private readonly HashSetImpl<T> _set;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(HashSetImpl<T> set)
            {
                _set = set;
                _version = set._version;
                _index = 0;
                _current = default(T);
            }

            public bool MoveNext()
            {
                if (_version != _set._version) ThrowHelper.VersionChanged();
                while ((uint)_index < (uint)_set._count)
                {
                    int i = _index++;
                    if (_set._entries[i].next >= -1)
                    {
                        _current = _set._entries[i].value;
                        return true;
                    }
                }
                _index = _set._count + 1;
                _current = default(T);
                return false;
            }

            public T Current => _current;
            public void Dispose() { }
        }

        internal sealed class EnumeratorObject : IEnumerator<T>
        {
            private readonly HashSetImpl<T> _set;
            private readonly int _version;
            private int _index;
            private T _current;

            public EnumeratorObject(HashSetImpl<T> set) { _set = set; _version = set._version; }

            public bool MoveNext()
            {
                if (_version != _set._version) ThrowHelper.VersionChanged();
                while ((uint)_index < (uint)_set._count)
                {
                    int i = _index++;
                    if (_set._entries[i].next >= -1) { _current = _set._entries[i].value; return true; }
                }
                _index = _set._count + 1;
                return false;
            }

            public T Current => _current;
            object IEnumerator.Current => _current;
            public void Reset() { _index = 0; }
            public void Dispose() { }
        }
    }
}
