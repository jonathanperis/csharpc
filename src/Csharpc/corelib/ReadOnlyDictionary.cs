// System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>: a read-only view over an
// IDictionary<TKey, TValue> (changes to the dictionary show through), as .NET's.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.ObjectModel.ReadOnlyDictionary`2")]
    public class ReadOnlyDictionaryImpl<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    {
        private readonly IDictionary<TKey, TValue> _dictionary;
        private KeyCollection _keys;
        private ValueCollection _values;

        public ReadOnlyDictionaryImpl(IDictionary<TKey, TValue> dictionary)
        {
            ArgumentNullException.ThrowIfNull(dictionary);
            _dictionary = dictionary;
        }

        public static System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue> Empty { get; } =
            (System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>)(object)new ReadOnlyDictionaryImpl<TKey, TValue>(new Dictionary<TKey, TValue>());

        protected IDictionary<TKey, TValue> Dictionary => _dictionary;
        public KeyCollection Keys => _keys ??= new KeyCollection(_dictionary.Keys);
        public ValueCollection Values => _values ??= new ValueCollection(_dictionary.Values);
        public int Count => _dictionary.Count;
        public TValue this[TKey key] => _dictionary[key];
        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dictionary.GetEnumerator();

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
        TValue IDictionary<TKey, TValue>.this[TKey key] { get => _dictionary[key]; set => throw ReadOnly(); }
        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => throw ReadOnly();
        bool IDictionary<TKey, TValue>.Remove(TKey key) => throw ReadOnly();
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => throw ReadOnly();
        void ICollection<KeyValuePair<TKey, TValue>>.Clear() => throw ReadOnly();
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) => _dictionary.Contains(item);
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _dictionary.CopyTo(array, arrayIndex);
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => throw ReadOnly();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_dictionary).GetEnumerator();

        private static NotSupportedException ReadOnly() => new NotSupportedException("Collection is read-only.");

        [Shadows("System.Collections.ObjectModel.ReadOnlyDictionary`2+KeyCollection")]
        public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
        {
            private readonly ICollection<TKey> _collection;
            internal KeyCollection(ICollection<TKey> collection) { _collection = collection; }
            public int Count => _collection.Count;
            public bool Contains(TKey item) => _collection.Contains(item);
            public void CopyTo(TKey[] array, int arrayIndex) => _collection.CopyTo(array, arrayIndex);
            public IEnumerator<TKey> GetEnumerator() => _collection.GetEnumerator();
            bool ICollection<TKey>.IsReadOnly => true;
            void ICollection<TKey>.Add(TKey item) => throw ReadOnly();
            void ICollection<TKey>.Clear() => throw ReadOnly();
            bool ICollection<TKey>.Remove(TKey item) => throw ReadOnly();
            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_collection).GetEnumerator();
        }

        [Shadows("System.Collections.ObjectModel.ReadOnlyDictionary`2+ValueCollection")]
        public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
        {
            private readonly ICollection<TValue> _collection;
            internal ValueCollection(ICollection<TValue> collection) { _collection = collection; }
            public int Count => _collection.Count;
            public void CopyTo(TValue[] array, int arrayIndex) => _collection.CopyTo(array, arrayIndex);
            public IEnumerator<TValue> GetEnumerator() => _collection.GetEnumerator();
            bool ICollection<TValue>.IsReadOnly => true;
            void ICollection<TValue>.Add(TValue item) => throw ReadOnly();
            void ICollection<TValue>.Clear() => throw ReadOnly();
            bool ICollection<TValue>.Contains(TValue item) => _collection.Contains(item);
            bool ICollection<TValue>.Remove(TValue item) => throw ReadOnly();
            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_collection).GetEnumerator();
        }
    }
}
