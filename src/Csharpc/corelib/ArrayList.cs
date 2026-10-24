// System.Collections.ArrayList, after dotnet/runtime's ArrayList.cs: an object[] grown like List<T>
// (0, then 4, then doubling), .NET's argument checks and messages, version-checked enumeration, and
// sorting with the ported introsort and Comparer.Default's semantics (ObjectComparison). The wrapper
// factories (Adapter, FixedSize, ReadOnly, Synchronized) are not implemented (CSLLVM0002).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.ArrayList")]
    public class ArrayListImpl : IList, ICloneable
    {
        private object[] _items;
        private int _size;
        private int _version;
        private const int DefaultCapacity = 4;

        public ArrayListImpl() { _items = Array.Empty<object>(); }

        public ArrayListImpl(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException("capacity", "'capacity' must be non-negative.");
            _items = capacity == 0 ? Array.Empty<object>() : new object[capacity];
        }

        public ArrayListImpl(ICollection c)
        {
            if (c == null) throw new ArgumentNullException("c");
            int count = c.Count;
            _items = count == 0 ? Array.Empty<object>() : new object[count];
            if (count != 0) { c.CopyTo(_items, 0); _size = count; }
        }

        public virtual int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size) throw new ArgumentOutOfRangeException("value", "capacity was less than the current size.");
                if (value == _items.Length) return;
                object[] items = value > 0 ? new object[value] : Array.Empty<object>();
                if (_size > 0) Array.Copy(_items, items, _size);
                _items = items;
            }
        }

        public virtual int Count => _size;
        public virtual bool IsFixedSize => false;
        public virtual bool IsReadOnly => false;
        public virtual bool IsSynchronized => false;
        public virtual object SyncRoot => this;

        public virtual object this[int index]
        {
            get
            {
                if (index < 0 || index >= _size) throw IndexOutOfRange("index");
                return _items[index];
            }
            set
            {
                if (index < 0 || index >= _size) throw IndexOutOfRange("index");
                _items[index] = value;
                _version++;
            }
        }

        private static ArgumentOutOfRangeException IndexOutOfRange(string name) =>
            new ArgumentOutOfRangeException(name, "Index was out of range. Must be non-negative and less than the size of the collection.");

        private static void CheckRange(int index, int count, int size)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index, "index");
            ArgumentOutOfRangeException.ThrowIfNegative(count, "count");
            if (size - index < count) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }

        private void EnsureCapacity(int min)
        {
            if (_items.Length >= min) return;
            int newCapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;
            if ((uint)newCapacity > (uint)Array.MaxLength) newCapacity = Array.MaxLength;
            if (newCapacity < min) newCapacity = min;
            Capacity = newCapacity;
        }

        public virtual int Add(object value)
        {
            if (_size == _items.Length) EnsureCapacity(_size + 1);
            _items[_size] = value;
            _version++;
            return _size++;
        }

        public virtual void AddRange(ICollection c) => InsertRange(_size, c);

        public virtual void Insert(int index, object value)
        {
            if (index < 0 || index > _size) throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (_size == _items.Length) EnsureCapacity(_size + 1);
            if (index < _size) Array.Copy(_items, index, _items, index + 1, _size - index);
            _items[index] = value;
            _size++;
            _version++;
        }

        public virtual void InsertRange(int index, ICollection c)
        {
            if (c == null) throw new ArgumentNullException("c");
            if (index < 0 || index > _size) throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            int count = c.Count;
            if (count > 0)
            {
                EnsureCapacity(_size + count);
                if (index < _size) Array.Copy(_items, index, _items, index + count, _size - index);
                object[] copy = new object[count];
                c.CopyTo(copy, 0);
                copy.CopyTo(_items, index);
                _size += count;
                _version++;
            }
        }

        public virtual void Remove(object obj)
        {
            int index = IndexOf(obj);
            if (index >= 0) RemoveAt(index);
        }

        public virtual void RemoveAt(int index)
        {
            if (index < 0 || index >= _size) throw IndexOutOfRange("index");
            _size--;
            if (index < _size) Array.Copy(_items, index + 1, _items, index, _size - index);
            _items[_size] = null;
            _version++;
        }

        public virtual void RemoveRange(int index, int count)
        {
            CheckRange(index, count, _size);
            if (count > 0)
            {
                _size -= count;
                if (index < _size) Array.Copy(_items, index + count, _items, index, _size - index);
                Array.Clear(_items, _size, count);
                _version++;
            }
        }

        public virtual void Clear()
        {
            if (_size > 0) { Array.Clear(_items, 0, _size); _size = 0; }
            _version++;
        }

        public virtual bool Contains(object item)
        {
            for (int i = 0; i < _size; i++)
                if (item == null ? _items[i] == null : _items[i] != null && _items[i].Equals(item)) return true;
            return false;
        }

        public virtual int IndexOf(object value) => IndexOf(value, 0, _size);

        public virtual int IndexOf(object value, int startIndex)
        {
            if (startIndex > _size) throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            return IndexOf(value, startIndex, _size - startIndex);
        }

        public virtual int IndexOf(object value, int startIndex, int count)
        {
            if (startIndex > _size) throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (count < 0 || startIndex > _size - count) throw new ArgumentOutOfRangeException("count", "Count must be positive and count must refer to a location within the string/array/collection.");
            return Array.IndexOf(_items, value, startIndex, count);
        }

        public virtual int LastIndexOf(object value) => _size == 0 ? -1 : Array.LastIndexOf(_items, value, _size - 1, _size);

        public virtual void Reverse() => Reverse(0, Count);

        public virtual void Reverse(int index, int count)
        {
            CheckRange(index, count, _size);
            Array.Reverse(_items, index, count);
            _version++;
        }

        public virtual void Sort() => Sort(0, Count, null);
        public virtual void Sort(IComparer comparer) => Sort(0, Count, comparer);

        public virtual void Sort(int index, int count, IComparer comparer)
        {
            CheckRange(index, count, _size);
            // ArraySortHelper wraps what the comparer throws.
            try
            {
                if (comparer == null) SortHelper<object>.SortDefault(_items, index, count);
                else SortHelper<object>.Sort(_items, index, count, (a, b) => comparer.Compare(a, b));
            }
            catch (Exception e) { throw new InvalidOperationException("Failed to compare two elements in the array.", e); }
            _version++;
        }

        public virtual int BinarySearch(object value) => BinarySearch(0, Count, value, null);
        public virtual int BinarySearch(object value, IComparer comparer) => BinarySearch(0, Count, value, comparer);

        public virtual int BinarySearch(int index, int count, object value, IComparer comparer)
        {
            CheckRange(index, count, _size);
            int lo = index, hi = index + count - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                int order = comparer == null ? ObjectComparison.Compare(_items[i], value) : comparer.Compare(_items[i], value);
                if (order == 0) return i;
                if (order < 0) lo = i + 1; else hi = i - 1;
            }
            return ~lo;
        }

        public virtual object[] ToArray()
        {
            if (_size == 0) return Array.Empty<object>();
            object[] array = new object[_size];
            Array.Copy(_items, array, _size);
            return array;
        }

        public virtual ArrayList GetRange(int index, int count)
        {
            CheckRange(index, count, _size);
            var range = new ArrayListImpl(count);
            Array.Copy(_items, index, range._items, 0, count);
            range._size = count;
            return (ArrayList)(object)range;
        }

        public virtual void SetRange(int index, ICollection c)
        {
            if (c == null) throw new ArgumentNullException("c");
            int count = c.Count;
            if (index < 0 || index > _size - count) throw IndexOutOfRange("index");
            if (count > 0) { c.CopyTo(_items, index); _version++; }
        }

        public virtual void CopyTo(Array array) => CopyTo(array, 0);

        public virtual void CopyTo(Array array, int arrayIndex)
        {
            if (array != null && array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
            Array.Copy(_items, 0, array, arrayIndex, _size);
        }

        public virtual void CopyTo(int index, Array array, int arrayIndex, int count)
        {
            if (_size - index < count) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            if (array != null && array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
            Array.Copy(_items, index, array, arrayIndex, count);
        }

        public virtual void TrimToSize() => Capacity = _size;

        public virtual object Clone()
        {
            var clone = new ArrayListImpl(_size);
            clone._size = _size;
            clone._version = _version;
            Array.Copy(_items, clone._items, _size);
            return clone;
        }

        public static ArrayList Repeat(object value, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count, "count");
            var list = new ArrayListImpl(count > DefaultCapacity ? count : DefaultCapacity);
            for (int i = 0; i < count; i++) list.Add(value);
            return (ArrayList)(object)list;
        }

        public virtual IEnumerator GetEnumerator() => new Enumerator(this, 0, _size);

        public virtual IEnumerator GetEnumerator(int index, int count)
        {
            CheckRange(index, count, _size);
            return new Enumerator(this, index, count);
        }

        private sealed class Enumerator : IEnumerator
        {
            private readonly ArrayListImpl _list;
            private readonly int _start, _end, _version;
            private int _index;
            private object _current;

            internal Enumerator(ArrayListImpl list, int index, int count)
            {
                _list = list;
                _start = index;
                _end = index + count;
                _index = index - 1;
                _version = list._version;
            }


            public bool MoveNext()
            {
                if (_version != _list._version) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                if (_index < _end - 1) { _index++; _current = _list._items[_index]; return true; }
                _index = _end;
                return false;
            }

            public object Current
            {
                get
                {
                    if (_index < _start) throw new InvalidOperationException("Enumeration has not started. Call MoveNext.");
                    if (_index >= _end) throw new InvalidOperationException("Enumeration already finished.");
                    return _current;
                }
            }

            public void Reset()
            {
                if (_version != _list._version) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
                _index = _start - 1;
            }
        }
    }
}
