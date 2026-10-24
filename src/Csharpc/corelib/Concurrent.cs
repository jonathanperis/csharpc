// System.Collections.Concurrent.ConcurrentDictionary: a Dictionary behind one lock. Lookups, adds,
// updates and removals have .NET's semantics (factories run outside the lock, as in .NET, so GetOrAdd
// may call valueFactory more than once under contention and keeps the first stored value).
// Enumeration is not supported yet: .NET enumerates in bucket order, which depends on its striped-lock
// table (sized from Environment.ProcessorCount) and growth policy; csharpc throws instead of guessing.
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Concurrent.ConcurrentDictionary`2")]
    public class ConcurrentDictionaryImpl<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> _items;
        private readonly object _lock = new object();

        public ConcurrentDictionaryImpl() { _items = new Dictionary<TKey, TValue>(); }
        public ConcurrentDictionaryImpl(IEqualityComparer<TKey> comparer) { _items = new Dictionary<TKey, TValue>(comparer); }

        public ConcurrentDictionaryImpl(int concurrencyLevel, int capacity)
        {
            if (concurrencyLevel <= 0) throw new ArgumentOutOfRangeException("concurrencyLevel", "The concurrencyLevel argument must be positive.");
            if (capacity < 0) throw new ArgumentOutOfRangeException("capacity", "The capacity argument must be greater than or equal to zero.");
            _items = new Dictionary<TKey, TValue>();
        }

        public ConcurrentDictionaryImpl(int concurrencyLevel, int capacity, IEqualityComparer<TKey> comparer) : this(concurrencyLevel, capacity)
        {
            _items = new Dictionary<TKey, TValue>(comparer);
        }

        public ConcurrentDictionaryImpl(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        public ConcurrentDictionaryImpl(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
        {
            if (collection == null) throw new ArgumentNullException("collection");
            _items = new Dictionary<TKey, TValue>(comparer);
            foreach (var pair in collection)
            {
                if (pair.Key == null) throw new ArgumentNullException("key");
                if (_items.ContainsKey(pair.Key)) throw new ArgumentException("The source argument contains duplicate keys.");
                _items[pair.Key] = pair.Value;
            }
        }

        private static void CheckKey(TKey key)
        {
            if (key == null) throw new ArgumentNullException("key");
        }

        public IEqualityComparer<TKey> Comparer => _items.Comparer;

        public int Count { get { lock (_lock) return _items.Count; } }
        public bool IsEmpty { get { lock (_lock) return _items.Count == 0; } }

        public TValue this[TKey key]
        {
            get
            {
                CheckKey(key);
                lock (_lock)
                    if (_items.TryGetValue(key, out var value)) return value;
                throw new KeyNotFoundException("The given key '" + key + "' was not present in the dictionary.");
            }
            set
            {
                CheckKey(key);
                lock (_lock) _items[key] = value;
            }
        }

        public bool ContainsKey(TKey key)
        {
            CheckKey(key);
            lock (_lock) return _items.ContainsKey(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            CheckKey(key);
            lock (_lock) return _items.TryGetValue(key, out value);
        }

        public bool TryAdd(TKey key, TValue value)
        {
            CheckKey(key);
            lock (_lock) return _items.TryAdd(key, value);
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            CheckKey(key);
            lock (_lock) return _items.Remove(key, out value);
        }

        public bool TryRemove(KeyValuePair<TKey, TValue> item)
        {
            CheckKey(item.Key);
            lock (_lock)
            {
                if (!_items.TryGetValue(item.Key, out var current) || !EqualityComparer<TValue>.Default.Equals(current, item.Value)) return false;
                _items.Remove(item.Key);
                return true;
            }
        }

        public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
        {
            CheckKey(key);
            lock (_lock)
            {
                if (!_items.TryGetValue(key, out var current) || !EqualityComparer<TValue>.Default.Equals(current, comparisonValue)) return false;
                _items[key] = newValue;
                return true;
            }
        }

        public TValue GetOrAdd(TKey key, TValue value)
        {
            CheckKey(key);
            lock (_lock)
            {
                if (_items.TryGetValue(key, out var existing)) return existing;
                _items[key] = value;
                return value;
            }
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            CheckKey(key);
            if (valueFactory == null) throw new ArgumentNullException("valueFactory");
            lock (_lock)
                if (_items.TryGetValue(key, out var existing)) return existing;
            return GetOrAdd(key, valueFactory(key));
        }

        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
        {
            CheckKey(key);
            if (valueFactory == null) throw new ArgumentNullException("valueFactory");
            lock (_lock)
                if (_items.TryGetValue(key, out var existing)) return existing;
            return GetOrAdd(key, valueFactory(key, factoryArgument));
        }

        public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
        {
            CheckKey(key);
            if (updateValueFactory == null) throw new ArgumentNullException("updateValueFactory");
            while (true)
            {
                TValue current;
                lock (_lock)
                {
                    if (!_items.TryGetValue(key, out current))
                    {
                        _items[key] = addValue;
                        return addValue;
                    }
                }
                var updated = updateValueFactory(key, current);
                if (TryUpdate(key, updated, current)) return updated;
            }
        }

        public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
        {
            CheckKey(key);
            if (addValueFactory == null) throw new ArgumentNullException("addValueFactory");
            if (updateValueFactory == null) throw new ArgumentNullException("updateValueFactory");
            while (true)
            {
                TValue current;
                bool exists;
                lock (_lock) exists = _items.TryGetValue(key, out current);
                if (!exists)
                {
                    var added = addValueFactory(key);
                    if (TryAdd(key, added)) return added;
                    continue;
                }
                var updated = updateValueFactory(key, current);
                if (TryUpdate(key, updated, current)) return updated;
            }
        }

        public void Clear()
        {
            lock (_lock) _items.Clear();
        }

        private static Exception EnumerationNotSupported() =>
            new NotSupportedException("Enumerating a ConcurrentDictionary is not supported by csharpc yet (.NET's order depends on its bucket layout).");

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => throw EnumerationNotSupported();
        public ICollection<TKey> Keys => throw EnumerationNotSupported();
        public ICollection<TValue> Values => throw EnumerationNotSupported();
        public KeyValuePair<TKey, TValue>[] ToArray() => throw EnumerationNotSupported();
    }
}
