// Exception.Data: .NET's ListDictionaryInternal (insertion-ordered, non-null keys), created on first use.
// csrt_Exception has no slot for it (src/runtime/csrt.h), so the dictionaries live in a
// ConditionalWeakTable keyed by the exception: only programs that read Data pay for it.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    internal static class ExceptionData
    {
        private static readonly ConditionalWeakTable<Exception, ListDictionaryInternal> s_data = new();

        public static IDictionary Get(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return s_data.GetValue(exception, static _ => new ListDictionaryInternal());
        }
    }

    internal sealed class ListDictionaryInternal : IDictionary
    {
        private readonly List<object> _keys = new();
        private readonly List<object> _values = new();

        public object this[object key]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(key);
                int i = IndexOf(key);
                return i < 0 ? null : _values[i];
            }
            set
            {
                ArgumentNullException.ThrowIfNull(key);
                int i = IndexOf(key);
                if (i < 0) { _keys.Add(key); _values.Add(value); }
                else _values[i] = value;
            }
        }

        private int IndexOf(object key)
        {
            for (int i = 0; i < _keys.Count; i++)
                if (_keys[i].Equals(key)) return i;
            return -1;
        }

        public int Count => _keys.Count;
        public ICollection Keys => _keys.ToArray();
        public ICollection Values => _values.ToArray();
        public bool IsReadOnly => false;
        public bool IsFixedSize => false;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public void Add(object key, object value)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (IndexOf(key) >= 0)
                throw new ArgumentException("An item with the same key has already been added. Key: " + key);
            _keys.Add(key);
            _values.Add(value);
        }

        public void Clear() { _keys.Clear(); _values.Clear(); }

        public bool Contains(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return IndexOf(key) >= 0;
        }

        public void Remove(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            int i = IndexOf(key);
            if (i >= 0) { _keys.RemoveAt(i); _values.RemoveAt(i); }
        }

        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            for (int i = 0; i < _keys.Count; i++) array.SetValue(new DictionaryEntry(_keys[i], _values[i]), index + i);
        }

        public IDictionaryEnumerator GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        private sealed class Enumerator : IDictionaryEnumerator
        {
            private readonly ListDictionaryInternal _owner;
            private int _index = -1;
            public Enumerator(ListDictionaryInternal owner) { _owner = owner; }
            public bool MoveNext() => ++_index < _owner._keys.Count;
            public void Reset() => _index = -1;
            public DictionaryEntry Entry => new DictionaryEntry(_owner._keys[_index], _owner._values[_index]);
            public object Key => _owner._keys[_index];
            public object Value => _owner._values[_index];
            public object Current => Entry;
        }
    }
}
