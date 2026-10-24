// System.Collections.Generic.OrderedDictionary<TKey, TValue> (.NET 9): a dictionary whose entries keep
// their insertion order and can be addressed by index. Entries live in a list with a key -> index map
// (re-indexed after an insert or removal, which .NET also does in O(n)); Capacity follows .NET's prime
// sizes. The non-generic IDictionary/IList views are not implemented.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Generic.OrderedDictionary`2")]
    public class OrderedDictionaryImpl<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>,
        IList<KeyValuePair<TKey, TValue>>, IReadOnlyList<KeyValuePair<TKey, TValue>>
    {
        private readonly List<KeyValuePair<TKey, TValue>> _entries;
        private readonly Dictionary<TKey, int> _indices;
        private readonly IEqualityComparer<TKey> _comparer;
        private int _capacity;
        private int _version;
        private KeyCollection _keys;
        private ValueCollection _values;

        public OrderedDictionaryImpl() : this(0, null) { }
        public OrderedDictionaryImpl(int capacity) : this(capacity, null) { }
        public OrderedDictionaryImpl(IEqualityComparer<TKey> comparer) : this(0, comparer) { }

        public OrderedDictionaryImpl(int capacity, IEqualityComparer<TKey> comparer)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity, "capacity");
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            _entries = new List<KeyValuePair<TKey, TValue>>();
            _indices = new Dictionary<TKey, int>(comparer);
            if (capacity > 0) _capacity = HashHelpers.GetPrime(capacity);
        }

        public OrderedDictionaryImpl(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

        public OrderedDictionaryImpl(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
            : this(dictionary == null ? 0 : dictionary.Count, comparer)
        {
            if (dictionary == null) throw new ArgumentNullException("dictionary");
            foreach (var pair in dictionary) Add(pair.Key, pair.Value);
        }

        public OrderedDictionaryImpl(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        public OrderedDictionaryImpl(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
            : this((collection as ICollection<KeyValuePair<TKey, TValue>>)?.Count ?? 0, comparer)
        {
            if (collection == null) throw new ArgumentNullException("collection");
            foreach (var pair in collection) Add(pair.Key, pair.Value);
        }

        public int Capacity => _capacity;
        public IEqualityComparer<TKey> Comparer => _comparer;
        public int Count => _entries.Count;
        public KeyCollection Keys => _keys ??= new KeyCollection(this);
        public ValueCollection Values => _values ??= new ValueCollection(this);

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public TValue this[TKey key]
        {
            get
            {
                int index = IndexOf(key);
                if (index < 0) throw new KeyNotFoundException("The given key '" + key + "' was not present in the dictionary.");
                return _entries[index].Value;
            }
            set
            {
                int index = IndexOf(key);
                if (index >= 0) { _entries[index] = new KeyValuePair<TKey, TValue>(_entries[index].Key, value); _version++; }
                else Append(key, value);
            }
        }

        KeyValuePair<TKey, TValue> IList<KeyValuePair<TKey, TValue>>.this[int index]
        {
            get => GetAt(index);
            set => SetAt(index, value.Key, value.Value);
        }

        KeyValuePair<TKey, TValue> IReadOnlyList<KeyValuePair<TKey, TValue>>.this[int index] => GetAt(index);

        private static ArgumentException DuplicateKey(TKey key) =>
            new ArgumentException("An item with the same key has already been added. Key: " + key, "key");

        private void CheckIndex(int index)
        {
            if ((uint)index >= (uint)_entries.Count)
                throw new ArgumentOutOfRangeException("index");
        }

        private void Grow()
        {
            if (_entries.Count == _capacity) _capacity = _capacity == 0 ? HashHelpers.GetPrime(0) : HashHelpers.ExpandPrime(_entries.Count);
        }

        private void Append(TKey key, TValue value)
        {
            Grow();
            _indices.Add(key, _entries.Count);
            _entries.Add(new KeyValuePair<TKey, TValue>(key, value));
            _version++;
        }

        private void Reindex(int from)
        {
            for (int i = from; i < _entries.Count; i++) _indices[_entries[i].Key] = i;
        }

        public void Add(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (_indices.ContainsKey(key)) throw DuplicateKey(key);
            Append(key, value);
        }

        public bool TryAdd(TKey key, TValue value) => TryAdd(key, value, out _);

        public bool TryAdd(TKey key, TValue value, out int index)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (_indices.TryGetValue(key, out index)) return false;
            index = _entries.Count;
            Append(key, value);
            return true;
        }

        public void Insert(int index, TKey key, TValue value)
        {
            if ((uint)index > (uint)_entries.Count)
                throw new ArgumentOutOfRangeException("index");
            if (key == null) throw new ArgumentNullException("key");
            if (_indices.ContainsKey(key)) throw DuplicateKey(key);
            Grow();
            _entries.Insert(index, new KeyValuePair<TKey, TValue>(key, value));
            _indices.Add(key, index);
            Reindex(index + 1);
            _version++;
        }

        public void Clear()
        {
            _entries.Clear();
            _indices.Clear();
            _version++;
        }

        public bool ContainsKey(TKey key) => IndexOf(key) >= 0;

        public bool ContainsValue(TValue value)
        {
            var comparer = EqualityComparer<TValue>.Default;
            foreach (var entry in _entries) if (comparer.Equals(entry.Value, value)) return true;
            return false;
        }

        public KeyValuePair<TKey, TValue> GetAt(int index)
        {
            CheckIndex(index);
            return _entries[index];
        }

        public int IndexOf(TKey key)
        {
            if (key == null) throw new ArgumentNullException("key");
            return _indices.TryGetValue(key, out int index) ? index : -1;
        }

        public bool Remove(TKey key) => Remove(key, out _);

        public bool Remove(TKey key, out TValue value)
        {
            int index = IndexOf(key);
            if (index < 0) { value = default; return false; }
            value = _entries[index].Value;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            CheckIndex(index);
            _indices.Remove(_entries[index].Key);
            _entries.RemoveAt(index);
            Reindex(index);
            _version++;
        }

        public void SetAt(int index, TValue value)
        {
            CheckIndex(index);
            _entries[index] = new KeyValuePair<TKey, TValue>(_entries[index].Key, value);
            _version++;
        }

        public void SetAt(int index, TKey key, TValue value)
        {
            CheckIndex(index);
            if (key == null) throw new ArgumentNullException("key");
            TKey existing = _entries[index].Key;
            if (!_comparer.Equals(existing, key))
            {
                if (_indices.ContainsKey(key)) throw DuplicateKey(key);
                _indices.Remove(existing);
                _indices.Add(key, index);
            }
            else if (!ReferenceEquals(existing, key))
            {
                _indices.Remove(existing);
                _indices.Add(key, index);
            }
            _entries[index] = new KeyValuePair<TKey, TValue>(key, value);
            _version++;
        }

        public int EnsureCapacity(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity, "capacity");
            if (_capacity < capacity) { _capacity = HashHelpers.GetPrime(capacity); _version++; }
            return _capacity;
        }

        public void TrimExcess() => TrimExcess(_entries.Count);

        public void TrimExcess(int capacity)
        {
            if (capacity < _entries.Count) throw new ArgumentOutOfRangeException("capacity");
            int newSize = HashHelpers.GetPrime(capacity);
            if (newSize < _capacity) { _capacity = newSize; _version++; }
        }

        public bool TryGetValue(TKey key, out TValue value) => TryGetValue(key, out value, out _);

        public bool TryGetValue(TKey key, out TValue value, out int index)
        {
            index = IndexOf(key);
            if (index < 0) { value = default; return false; }
            value = _entries[index].Value;
            return true;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
            TryGetValue(item.Key, out TValue value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException("array");
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex, "arrayIndex");
            if (array.Length - arrayIndex < _entries.Count) throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            _entries.CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            int index = IndexOf(item.Key);
            if (index >= 0 && EqualityComparer<TValue>.Default.Equals(_entries[index].Value, item.Value)) { RemoveAt(index); return true; }
            return false;
        }

        int IList<KeyValuePair<TKey, TValue>>.IndexOf(KeyValuePair<TKey, TValue> item)
        {
            int index = IndexOf(item.Key);
            return index >= 0 && EqualityComparer<TValue>.Default.Equals(_entries[index].Value, item.Value) ? index : -1;
        }

        void IList<KeyValuePair<TKey, TValue>>.Insert(int index, KeyValuePair<TKey, TValue> item) => Insert(index, item.Key, item.Value);

        [Shadows("System.Collections.Generic.OrderedDictionary`2+Enumerator")]
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly OrderedDictionaryImpl<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;

            internal Enumerator(OrderedDictionaryImpl<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _current = default;
            }

            public KeyValuePair<TKey, TValue> Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _dictionary._version) throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                if (_index < _dictionary._entries.Count)
                {
                    _current = _dictionary._entries[_index++];
                    return true;
                }
                _current = default;
                return false;
            }

            public void Reset()
            {
                if (_version != _dictionary._version) throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
                _index = 0;
                _current = default;
            }

            public void Dispose() { }
        }

        [Shadows("System.Collections.Generic.OrderedDictionary`2+KeyCollection")]
        public sealed class KeyCollection : IList<TKey>, IReadOnlyList<TKey>
        {
            private readonly OrderedDictionaryImpl<TKey, TValue> _dictionary;
            internal KeyCollection(OrderedDictionaryImpl<TKey, TValue> dictionary) { _dictionary = dictionary; }

            public int Count => _dictionary.Count;
            bool ICollection<TKey>.IsReadOnly => true;
            public bool Contains(TKey key) => _dictionary.ContainsKey(key);
            public TKey this[int index] => _dictionary.GetAt(index).Key;

            TKey IList<TKey>.this[int index]
            {
                get => this[index];
                set => throw new NotSupportedException();
            }

            int IList<TKey>.IndexOf(TKey item) => _dictionary.IndexOf(item);

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                if (array == null) throw new ArgumentNullException("array");
                ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex, "arrayIndex");
                if (array.Length - arrayIndex < Count) throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
                for (int i = 0; i < Count; i++) array[arrayIndex + i] = this[i];
            }

            public Enumerator GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            void ICollection<TKey>.Add(TKey item) => throw new NotSupportedException();
            void ICollection<TKey>.Clear() => throw new NotSupportedException();
            bool ICollection<TKey>.Remove(TKey item) => throw new NotSupportedException();
            void IList<TKey>.Insert(int index, TKey item) => throw new NotSupportedException();
            void IList<TKey>.RemoveAt(int index) => throw new NotSupportedException();

            [Shadows("System.Collections.Generic.OrderedDictionary`2+KeyCollection+Enumerator")]
            public struct Enumerator : IEnumerator<TKey>
            {
                private OrderedDictionaryImpl<TKey, TValue>.Enumerator _inner;
                internal Enumerator(OrderedDictionaryImpl<TKey, TValue> dictionary) { _inner = new OrderedDictionaryImpl<TKey, TValue>.Enumerator(dictionary); }
                public TKey Current => _inner.Current.Key;
                object IEnumerator.Current => Current;
                public bool MoveNext() => _inner.MoveNext();
                public void Reset() => _inner.Reset();
                public void Dispose() { }
            }
        }

        [Shadows("System.Collections.Generic.OrderedDictionary`2+ValueCollection")]
        public sealed class ValueCollection : IList<TValue>, IReadOnlyList<TValue>
        {
            private readonly OrderedDictionaryImpl<TKey, TValue> _dictionary;
            internal ValueCollection(OrderedDictionaryImpl<TKey, TValue> dictionary) { _dictionary = dictionary; }

            public int Count => _dictionary.Count;
            bool ICollection<TValue>.IsReadOnly => true;
            public TValue this[int index] => _dictionary.GetAt(index).Value;

            TValue IList<TValue>.this[int index]
            {
                get => this[index];
                set => throw new NotSupportedException();
            }

            bool ICollection<TValue>.Contains(TValue item) => _dictionary.ContainsValue(item);

            int IList<TValue>.IndexOf(TValue item)
            {
                var comparer = EqualityComparer<TValue>.Default;
                for (int i = 0; i < Count; i++) if (comparer.Equals(this[i], item)) return i;
                return -1;
            }

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                if (array == null) throw new ArgumentNullException("array");
                ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex, "arrayIndex");
                if (array.Length - arrayIndex < Count) throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
                for (int i = 0; i < Count; i++) array[arrayIndex + i] = this[i];
            }

            public Enumerator GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            void ICollection<TValue>.Add(TValue item) => throw new NotSupportedException();
            void ICollection<TValue>.Clear() => throw new NotSupportedException();
            bool ICollection<TValue>.Remove(TValue item) => throw new NotSupportedException();
            void IList<TValue>.Insert(int index, TValue item) => throw new NotSupportedException();
            void IList<TValue>.RemoveAt(int index) => throw new NotSupportedException();

            [Shadows("System.Collections.Generic.OrderedDictionary`2+ValueCollection+Enumerator")]
            public struct Enumerator : IEnumerator<TValue>
            {
                private OrderedDictionaryImpl<TKey, TValue>.Enumerator _inner;
                internal Enumerator(OrderedDictionaryImpl<TKey, TValue> dictionary) { _inner = new OrderedDictionaryImpl<TKey, TValue>.Enumerator(dictionary); }
                public TValue Current => _inner.Current.Value;
                object IEnumerator.Current => Current;
                public bool MoveNext() => _inner.MoveNext();
                public void Reset() => _inner.Reset();
                public void Dispose() { }
            }
        }
    }
}
