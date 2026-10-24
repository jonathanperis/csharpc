// System.Collections.Generic.Dictionary<TKey,TValue> and KeyValuePair<TKey,TValue> — a port of .NET's
// buckets + entries + free-list design, so enumeration order matches .NET exactly (insertion order,
// with removed slots reused LIFO by later additions).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CsharpcCoreLib
{
    internal static class HashHelpers
    {
        private static int[] s_primes;

        private static int[] Primes
        {
            get
            {
                if (s_primes == null)
                    s_primes = new int[]
                    {
                        3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
                        1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
                        17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
                        187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
                        1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369,
                    };
                return s_primes;
            }
        }

        private static bool IsPrime(int candidate)
        {
            if ((candidate & 1) != 0)
            {
                int limit = (int)Math.Sqrt(candidate);
                for (int divisor = 3; divisor <= limit; divisor += 2)
                    if ((candidate % divisor) == 0) return false;
                return true;
            }
            return candidate == 2;
        }

        public static int GetPrime(int min)
        {
            int[] primes = Primes;
            for (int i = 0; i < primes.Length; i++)
                if (primes[i] >= min) return primes[i];
            for (int i = (min | 1); i < int.MaxValue; i += 2)
                if (IsPrime(i) && ((i - 1) % 101 != 0)) return i;
            return min;
        }

        /// <summary>.NET's FastMod: value % divisor via 64-bit multiply-high (no hardware divide).</summary>
        public static ulong GetFastModMultiplier(uint divisor) => ulong.MaxValue / divisor + 1;

        public static uint FastMod(uint value, uint divisor, ulong multiplier) =>
            (uint)(((((multiplier * value) >> 32) + 1) * divisor) >> 32);

        public static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;
            if ((uint)newSize > 0x7FFFFFC3u && 0x7FFFFFC3 > oldSize) return 0x7FFFFFC3;
            return GetPrime(newSize);
        }
    }

    [Shadows("System.Collections.Generic.KeyValuePair`2")]
    public struct KeyValuePairImpl<TKey, TValue>
    {
        private readonly TKey key;
        private readonly TValue value;

        public KeyValuePairImpl(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
        }

        public TKey Key => key;
        public TValue Value => value;

        public void Deconstruct(out TKey key, out TValue value)
        {
            key = this.key;
            value = this.value;
        }

        public override string ToString()
        {
            var s = new StringBuilder();
            s.Append('[');
            if (key != null) s.Append(key.ToString());
            s.Append(", ");
            if (value != null) s.Append(value.ToString());
            s.Append(']');
            return s.ToString();
        }
    }

    [Shadows("System.Collections.DictionaryEntry")]
    public struct DictionaryEntryImpl
    {
        private object _key;
        private object _value;

        public DictionaryEntryImpl(object key, object value)
        {
            _key = key;
            _value = value;
        }

        public object Key { get => _key; set => _key = value; }
        public object Value { get => _value; set => _value = value; }

        public void Deconstruct(out object key, out object value)
        {
            key = _key;
            value = _value;
        }

        public override string ToString()
        {
            var s = new StringBuilder();
            s.Append('[');
            if (_key != null) s.Append(_key.ToString());
            s.Append(", ");
            if (_value != null) s.Append(_value.ToString());
            s.Append(']');
            return s.ToString();
        }
    }

    [Shadows("System.Collections.Generic.KeyValuePair")]
    public static class KeyValuePairStatics
    {
        public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value) => new KeyValuePair<TKey, TValue>(key, value);
    }

    [Shadows("System.Collections.Generic.Dictionary`2")]
    public class DictionaryImpl<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    {
        internal struct Entry
        {
            public uint hashCode;
            public int next;   // index of next entry in the chain, -1 = end; free entries: StartOfFreeList - nextFree
            public TKey key;
            public TValue value;
        }

        private const int StartOfFreeList = -3;

        private int[] _buckets;
        private ulong _fastModMultiplier;
        internal Entry[] _entries;
        internal int _count;
        private int _freeList;
        private int _freeCount;
        internal int _version;
        private IEqualityComparer<TKey> _comparer;   // null = default (inlined CoreIntrinsics)
        private KeyCollection _keys;
        private ValueCollection _values;

        public DictionaryImpl() : this(0, null) { }
        public DictionaryImpl(int capacity) : this(capacity, null) { }
        public DictionaryImpl(IEqualityComparer<TKey> comparer) : this(0, comparer) { }

        public DictionaryImpl(int capacity, IEqualityComparer<TKey> comparer)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException("capacity");
            if (capacity > 0) Initialize(capacity);
            if (comparer != null && (object)comparer != (object)EqualityComparer<TKey>.Default) _comparer = comparer;
        }

        public DictionaryImpl(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

        public DictionaryImpl(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
            : this(dictionary != null ? dictionary.Count : 0, comparer)
        {
            if (dictionary == null) ThrowHelper.ArgumentNull("dictionary");
            foreach (KeyValuePair<TKey, TValue> pair in dictionary) Add(pair.Key, pair.Value);
        }

        public DictionaryImpl(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        public DictionaryImpl(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer) : this(0, comparer)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            foreach (KeyValuePair<TKey, TValue> pair in collection) Add(pair.Key, pair.Value);
        }

        public IEqualityComparer<TKey> Comparer => _comparer ?? EqualityComparer<TKey>.Default;

        public int Count => _count - _freeCount;

        public int Capacity => _entries == null ? 0 : _entries.Length;

        public KeyCollection Keys => _keys ?? (_keys = new KeyCollection(this));
        public ValueCollection Values => _values ?? (_values = new ValueCollection(this));
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public TValue this[TKey key]
        {
            get
            {
                int i = FindEntry(key);
                if (i >= 0) return _entries[i].value;
                throw CoreIntrinsics.LibraryFrames(ThrowHelper.KeyNotFoundException(key.ToString()), "System.Collections.Generic.Dictionary`2.get_Item(TKey key)");
            }
            set { TryInsert(key, value, false); }
        }

        private bool KeysEqual(TKey a, TKey b) => _comparer == null ? CoreIntrinsics.AreEqual(a, b) : _comparer.Equals(a, b);
        private uint Hash(TKey key) => (uint)(_comparer == null ? CoreIntrinsics.HashOf(key) : _comparer.GetHashCode(key));

        private int Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            _freeList = -1;
            _buckets = new int[size];
            _entries = new Entry[size];
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
            return size;
        }

        internal int FindEntry(TKey key)
        {
            if (key == null) ThrowHelper.ArgumentNull("key");
            if (_buckets == null) return -1;
            uint hashCode = Hash(key);
            int i = _buckets[(int)HashHelpers.FastMod(hashCode, (uint)_buckets.Length, _fastModMultiplier)] - 1;
            Entry[] entries = _entries;
            while ((uint)i < (uint)entries.Length)
            {
                if (entries[i].hashCode == hashCode && KeysEqual(entries[i].key, key)) return i;
                i = entries[i].next;
            }
            return -1;
        }

        /// <summary>throwOnExisting: Add semantics; else indexer-set semantics. Returns false if the key existed (Add/TryAdd).</summary>
        private bool TryInsert(TKey key, TValue value, bool add, bool throwOnExisting = true)
        {
            if (key == null) ThrowHelper.ArgumentNull("key");
            if (_buckets == null) Initialize(0);
            uint hashCode = Hash(key);
            int bucket = (int)HashHelpers.FastMod(hashCode, (uint)_buckets.Length, _fastModMultiplier);
            int i = _buckets[bucket] - 1;
            while ((uint)i < (uint)_entries.Length)
            {
                if (_entries[i].hashCode == hashCode && KeysEqual(_entries[i].key, key))
                {
                    if (!add)
                    {
                        _entries[i].value = value;
                        return true;
                    }
                    if (throwOnExisting) ThrowHelper.DuplicateKey(key.ToString());
                    return false;
                }
                i = _entries[i].next;
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeList = StartOfFreeList - _entries[_freeList].next;
                _freeCount--;
            }
            else
            {
                if (_count == _entries.Length)
                {
                    Resize(HashHelpers.ExpandPrime(_count));
                    bucket = (int)HashHelpers.FastMod(hashCode, (uint)_buckets.Length, _fastModMultiplier);
                }
                index = _count;
                _count++;
            }
            _entries[index].hashCode = hashCode;
            _entries[index].next = _buckets[bucket] - 1;
            _entries[index].key = key;
            _entries[index].value = value;
            _buckets[bucket] = index + 1;
            _version++;
            return true;
        }

        private void Resize(int newSize)
        {
            Entry[] entries = new Entry[newSize];
            int count = _count;
            for (int i = 0; i < count; i++) entries[i] = _entries[i];
            _buckets = new int[newSize];
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
            for (int i = 0; i < count; i++)
            {
                if (entries[i].next >= -1)
                {
                    int bucket = (int)HashHelpers.FastMod(entries[i].hashCode, (uint)newSize, _fastModMultiplier);
                    entries[i].next = _buckets[bucket] - 1;
                    _buckets[bucket] = i + 1;
                }
            }
            _entries = entries;
        }

        public void Add(TKey key, TValue value) => TryInsert(key, value, true);

        public bool TryAdd(TKey key, TValue value) => TryInsert(key, value, true, false);

        public void Clear()
        {
            int count = _count;
            if (count > 0)
            {
                for (int i = 0; i < _buckets.Length; i++) _buckets[i] = 0;
                _count = 0;
                _freeList = -1;
                _freeCount = 0;
                for (int i = 0; i < count; i++) _entries[i] = default(Entry);
            }
        }

        public bool ContainsKey(TKey key) => FindEntry(key) >= 0;

        public bool ContainsValue(TValue value)
        {
            for (int i = 0; i < _count; i++)
                if (_entries[i].next >= -1 && CoreIntrinsics.AreEqual(_entries[i].value, value)) return true;
            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            int i = FindEntry(key);
            if (i >= 0)
            {
                value = _entries[i].value;
                return true;
            }
            value = default(TValue);
            return false;
        }

        public TValue GetValueOrDefault(TKey key) => TryGetValue(key, out var v) ? v : default(TValue);

        public bool Remove(TKey key) => Remove(key, out _);

        public bool Remove(TKey key, out TValue value)
        {
            if (key == null) ThrowHelper.ArgumentNull("key");
            if (_buckets != null)
            {
                uint hashCode = Hash(key);
                int bucket = (int)HashHelpers.FastMod(hashCode, (uint)_buckets.Length, _fastModMultiplier);
                int last = -1;
                int i = _buckets[bucket] - 1;
                while (i >= 0)
                {
                    if (_entries[i].hashCode == hashCode && KeysEqual(_entries[i].key, key))
                    {
                        if (last < 0) _buckets[bucket] = _entries[i].next + 1;
                        else _entries[last].next = _entries[i].next;
                        value = _entries[i].value;
                        _entries[i].next = StartOfFreeList - _freeList;
                        _entries[i].key = default(TKey);
                        _entries[i].value = default(TValue);
                        _freeList = i;
                        _freeCount++;
                        return true;
                    }
                    last = i;
                    i = _entries[i].next;
                }
            }
            value = default(TValue);
            return false;
        }

        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException("capacity");
            int current = Capacity;
            if (current >= capacity) return current;
            _version++;
            if (_buckets == null) return Initialize(capacity);
            int newSize = HashHelpers.GetPrime(capacity);
            Resize(newSize);
            return newSize;
        }

        public void TrimExcess() { }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => new EnumeratorObject(this);
        IEnumerator IEnumerable.GetEnumerator() => new EnumeratorObject(this);

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            int i = FindEntry(item.Key);
            return i >= 0 && CoreIntrinsics.AreEqual(_entries[i].value, item.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            int i = FindEntry(item.Key);
            if (i >= 0 && CoreIntrinsics.AreEqual(_entries[i].value, item.Value)) { Remove(item.Key); return true; }
            return false;
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            for (int i = 0; i < _count; i++)
                if (_entries[i].next >= -1) array[index++] = new KeyValuePair<TKey, TValue>(_entries[i].key, _entries[i].value);
        }

        [Shadows("System.Collections.Generic.Dictionary`2+Enumerator")]
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private readonly DictionaryImpl<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;

            internal Enumerator(DictionaryImpl<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _current = default(KeyValuePair<TKey, TValue>);
            }

            public bool MoveNext()
            {
                if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                while ((uint)_index < (uint)_dictionary._count)
                {
                    int i = _index++;
                    if (_dictionary._entries[i].next >= -1)
                    {
                        _current = new KeyValuePair<TKey, TValue>(_dictionary._entries[i].key, _dictionary._entries[i].value);
                        return true;
                    }
                }
                _index = _dictionary._count + 1;
                _current = default(KeyValuePair<TKey, TValue>);
                return false;
            }

            public KeyValuePair<TKey, TValue> Current => _current;
            public void Dispose() { }

            object IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || _index == _dictionary._count + 1) ThrowHelper.EnumOpCantHappen();
                    return CoreIntrinsics.Box(_current);
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                _index = 0;
                _current = default(KeyValuePair<TKey, TValue>);
            }

            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (_index == 0 || _index == _dictionary._count + 1) ThrowHelper.EnumOpCantHappen();
                    return new DictionaryEntry(CoreIntrinsics.Box(_current.Key), CoreIntrinsics.Box(_current.Value));
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (_index == 0 || _index == _dictionary._count + 1) ThrowHelper.EnumOpCantHappen();
                    return CoreIntrinsics.Box(_current.Key);
                }
            }

            object IDictionaryEnumerator.Value
            {
                get
                {
                    if (_index == 0 || _index == _dictionary._count + 1) ThrowHelper.EnumOpCantHappen();
                    return CoreIntrinsics.Box(_current.Value);
                }
            }
        }

        internal sealed class EnumeratorObject : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly DictionaryImpl<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;

            public EnumeratorObject(DictionaryImpl<TKey, TValue> dictionary) { _dictionary = dictionary; _version = dictionary._version; }

            public bool MoveNext()
            {
                if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                while ((uint)_index < (uint)_dictionary._count)
                {
                    int i = _index++;
                    if (_dictionary._entries[i].next >= -1)
                    {
                        _current = new KeyValuePair<TKey, TValue>(_dictionary._entries[i].key, _dictionary._entries[i].value);
                        return true;
                    }
                }
                _index = _dictionary._count + 1;
                return false;
            }

            public KeyValuePair<TKey, TValue> Current => _current;
            object IEnumerator.Current => CoreIntrinsics.Box(_current);
            public void Reset() { _index = 0; }
            public void Dispose() { }
        }

        [Shadows("System.Collections.Generic.Dictionary`2+KeyCollection")]
        public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
        {
            private readonly DictionaryImpl<TKey, TValue> _dictionary;

            public KeyCollection(DictionaryImpl<TKey, TValue> dictionary) { _dictionary = dictionary; }

            public int Count => _dictionary.Count;
            bool ICollection<TKey>.IsReadOnly => true;
            public Enumerator GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => new KeyEnumeratorObject(_dictionary);
            IEnumerator IEnumerable.GetEnumerator() => new KeyEnumeratorObject(_dictionary);
            public bool Contains(TKey item) => _dictionary.ContainsKey(item);
            void ICollection<TKey>.Add(TKey item) => ThrowHelper.NotSupported();
            void ICollection<TKey>.Clear() => ThrowHelper.NotSupported();
            bool ICollection<TKey>.Remove(TKey item) { ThrowHelper.NotSupported(); return false; }

            public void CopyTo(TKey[] array, int index)
            {
                for (int i = 0; i < _dictionary._count; i++)
                    if (_dictionary._entries[i].next >= -1) array[index++] = _dictionary._entries[i].key;
            }

            [Shadows("System.Collections.Generic.Dictionary`2+KeyCollection+Enumerator")]
            public struct Enumerator : IEnumerator<TKey>
            {
                private readonly DictionaryImpl<TKey, TValue> _dictionary;
                private readonly int _version;
                private int _index;
                private TKey _current;

                internal Enumerator(DictionaryImpl<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _index = 0;
                    _current = default(TKey);
                }

                public bool MoveNext()
                {
                    if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                    while ((uint)_index < (uint)_dictionary._count)
                    {
                        int i = _index++;
                        if (_dictionary._entries[i].next >= -1)
                        {
                            _current = _dictionary._entries[i].key;
                            return true;
                        }
                    }
                    _index = _dictionary._count + 1;
                    _current = default(TKey);
                    return false;
                }

                public TKey Current => _current;
                public void Dispose() { }

                object IEnumerator.Current
                {
                    get
                    {
                        if (_index == 0 || _index == _dictionary._count + 1) ThrowHelper.EnumOpCantHappen();
                        return CoreIntrinsics.Box(_current);
                    }
                }

                void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                    _index = 0;
                    _current = default(TKey);
                }
            }
        }

        internal sealed class KeyEnumeratorObject : IEnumerator<TKey>
        {
            private readonly DictionaryImpl<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private TKey _current;

            public KeyEnumeratorObject(DictionaryImpl<TKey, TValue> dictionary) { _dictionary = dictionary; _version = dictionary._version; }

            public bool MoveNext()
            {
                if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                while ((uint)_index < (uint)_dictionary._count)
                {
                    int i = _index++;
                    if (_dictionary._entries[i].next >= -1) { _current = _dictionary._entries[i].key; return true; }
                }
                _index = _dictionary._count + 1;
                return false;
            }

            public TKey Current => _current;
            object IEnumerator.Current => CoreIntrinsics.Box(_current);
            public void Reset() { _index = 0; }
            public void Dispose() { }
        }

        [Shadows("System.Collections.Generic.Dictionary`2+ValueCollection")]
        public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
        {
            private readonly DictionaryImpl<TKey, TValue> _dictionary;

            public ValueCollection(DictionaryImpl<TKey, TValue> dictionary) { _dictionary = dictionary; }

            public int Count => _dictionary.Count;
            bool ICollection<TValue>.IsReadOnly => true;
            public Enumerator GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => new ValueEnumeratorObject(_dictionary);
            IEnumerator IEnumerable.GetEnumerator() => new ValueEnumeratorObject(_dictionary);
            bool ICollection<TValue>.Contains(TValue item) => _dictionary.ContainsValue(item);
            void ICollection<TValue>.Add(TValue item) => ThrowHelper.NotSupported();
            void ICollection<TValue>.Clear() => ThrowHelper.NotSupported();
            bool ICollection<TValue>.Remove(TValue item) { ThrowHelper.NotSupported(); return false; }

            public void CopyTo(TValue[] array, int index)
            {
                for (int i = 0; i < _dictionary._count; i++)
                    if (_dictionary._entries[i].next >= -1) array[index++] = _dictionary._entries[i].value;
            }

            [Shadows("System.Collections.Generic.Dictionary`2+ValueCollection+Enumerator")]
            public struct Enumerator : IEnumerator<TValue>
            {
                private readonly DictionaryImpl<TKey, TValue> _dictionary;
                private readonly int _version;
                private int _index;
                private TValue _current;

                internal Enumerator(DictionaryImpl<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _index = 0;
                    _current = default(TValue);
                }

                public bool MoveNext()
                {
                    if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                    while ((uint)_index < (uint)_dictionary._count)
                    {
                        int i = _index++;
                        if (_dictionary._entries[i].next >= -1)
                        {
                            _current = _dictionary._entries[i].value;
                            return true;
                        }
                    }
                    _index = _dictionary._count + 1;
                    _current = default(TValue);
                    return false;
                }

                public TValue Current => _current;
                public void Dispose() { }

                object IEnumerator.Current
                {
                    get
                    {
                        if (_index == 0 || _index == _dictionary._count + 1) ThrowHelper.EnumOpCantHappen();
                        return CoreIntrinsics.Box(_current);
                    }
                }

                void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                    _index = 0;
                    _current = default(TValue);
                }
            }
        }

        internal sealed class ValueEnumeratorObject : IEnumerator<TValue>
        {
            private readonly DictionaryImpl<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private TValue _current;

            public ValueEnumeratorObject(DictionaryImpl<TKey, TValue> dictionary) { _dictionary = dictionary; _version = dictionary._version; }

            public bool MoveNext()
            {
                if (_version != _dictionary._version) ThrowHelper.VersionChanged();
                while ((uint)_index < (uint)_dictionary._count)
                {
                    int i = _index++;
                    if (_dictionary._entries[i].next >= -1) { _current = _dictionary._entries[i].value; return true; }
                }
                _index = _dictionary._count + 1;
                return false;
            }

            public TValue Current => _current;
            object IEnumerator.Current => CoreIntrinsics.Box(_current);
            public void Reset() { _index = 0; }
            public void Dispose() { }
        }
    }

    [Shadows("System.Collections.Generic.CollectionExtensions")]
    public static class CollectionExtensionsImpl
    {
        public static TValue GetValueOrDefault<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) =>
            dictionary.TryGetValue(key, out TValue value) ? value : default(TValue);

        public static TValue GetValueOrDefault<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue) =>
            dictionary.TryGetValue(key, out TValue value) ? value : defaultValue;

        public static bool TryAdd<TKey, TValue>(IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }

        public static bool Remove<TKey, TValue>(IDictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if (dictionary.TryGetValue(key, out value))
            {
                dictionary.Remove(key);
                return true;
            }
            value = default(TValue);
            return false;
        }
    }
}
