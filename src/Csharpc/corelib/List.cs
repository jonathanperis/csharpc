// System.Collections.Generic.List<T> — mirrors .NET's implementation (growth policy, version checks,
// exceptions) so observable behavior (Capacity, iteration, sort order of equal keys) matches.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Generic.List`1")]
    public class ListImpl<T> : IList<T>, IReadOnlyList<T>, IList
    {
        internal T[] _items;
        internal int _size;
        internal int _version;

        public ListImpl() { _items = new T[0]; }

        public ListImpl(int capacity)
        {
            if (capacity < 0) ThrowHelper.ArgumentOutOfRange("capacity", "Non-negative number required.");
            _items = new T[capacity];
        }

        public ListImpl(IEnumerable<T> collection)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            if (collection is ICollection<T> c)
            {
                int count = c.Count;
                _items = new T[count];
                c.CopyTo(_items, 0);
                _size = count;
            }
            else
            {
                _items = new T[0];
                foreach (T item in collection) Add(item);
            }
        }

        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size) ThrowHelper.ArgumentOutOfRange("value", "capacity was less than the current size.");
                if (value != _items.Length)
                {
                    T[] items = new T[value];
                    for (int i = 0; i < _size; i++) items[i] = _items[i];
                    _items = items;
                }
            }
        }

        public int Count => _size;

        bool ICollection<T>.IsReadOnly => false;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_size) throw CoreIntrinsics.LibraryFrames(ThrowHelper.IndexOutOfRangeException(), "System.Collections.Generic.List`1.get_Item(Int32 index)");
                return _items[index];
            }
            set
            {
                if ((uint)index >= (uint)_size) throw CoreIntrinsics.LibraryFrames(ThrowHelper.IndexOutOfRangeException(), "System.Collections.Generic.List`1.set_Item(Int32 index, T value)");
                _items[index] = value;
                _version++;
            }
        }

        internal void GrowForMarshal(int capacity) => Grow(capacity);

        private void Grow(int capacity)
        {
            int newCapacity = _items.Length == 0 ? 4 : 2 * _items.Length;
            if ((uint)newCapacity > 0x7FFFFFC7u) newCapacity = 0x7FFFFFC7;
            if (newCapacity < capacity) newCapacity = capacity;
            Capacity = newCapacity;
        }

        public void Add(T item)
        {
            _version++;
            if (_size == _items.Length) Grow(_size + 1);
            _items[_size++] = item;
        }

        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            if (collection is ICollection<T> c)
            {
                int count = c.Count;
                if (count > 0)
                {
                    if (_items.Length - _size < count) Grow(_size + count);
                    if ((object)c == (object)this)
                    {
                        for (int i = 0; i < count; i++) _items[_size + i] = _items[i];
                    }
                    else
                    {
                        T[] tmp = new T[count];
                        c.CopyTo(tmp, 0);
                        for (int i = 0; i < count; i++) _items[_size + i] = tmp[i];
                    }
                    _size += count;
                    _version++;
                }
            }
            else
            {
                foreach (T item in collection) Add(item);
            }
        }

        public int BinarySearch(T item) => SortHelper<T>.BinarySearch(_items, 0, _size, item, null);
        public int BinarySearch(T item, IComparer<T> comparer) => SortHelper<T>.BinarySearch(_items, 0, _size, item, comparer);
        public int BinarySearch(int index, int count, T item, IComparer<T> comparer)
        {
            CheckRange(index, count);
            return SortHelper<T>.BinarySearch(_items, index, count, item, comparer);
        }

        public void Clear()
        {
            _version++;
            // .NET clears only slots that hold references; CollectionsMarshal.SetCount can expose the rest.
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                for (int i = 0; i < _size; i++) _items[i] = default(T);
            _size = 0;
        }

        public bool Contains(T item) => _size != 0 && IndexOf(item) >= 0;

        public List<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
        {
            if (converter == null) ThrowHelper.ArgumentNull("converter");
            var list = new List<TOutput>(_size);
            for (int i = 0; i < _size; i++) list.Add(converter(_items[i]));
            return list;
        }

        public void CopyTo(T[] array) => CopyTo(array, 0);

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (arrayIndex < 0 || array.Length - arrayIndex < _size)
                ThrowHelper.Argument("Destination array was not long enough. Check the destination index, length, and the array's lower bounds.");
            // .NET's is Array.Copy: a covariant destination (a string[] seen as an object[]) casts each element
            // (InvalidCastException, not the stores' ArrayTypeMismatchException). Reference arrays only.
            if (_items is object[] items) { CoreIntrinsics.csrt_array_copy_checked(items, 0, array, arrayIndex, _size, null); return; }
            for (int i = 0; i < _size; i++) array[arrayIndex + i] = _items[i];
        }

        public void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            CheckRange(index, count);
            for (int i = 0; i < count; i++) array[arrayIndex + i] = _items[index + i];
        }

        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0) ThrowHelper.ArgumentOutOfRange("capacity", "Non-negative number required.");
            if (_items.Length < capacity) Grow(capacity);
            return _items.Length;
        }

        public bool Exists(Predicate<T> match) => FindIndex(match) != -1;

        public T Find(Predicate<T> match)
        {
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = 0; i < _size; i++)
                if (match(_items[i])) return _items[i];
            return default(T);
        }

        public List<T> FindAll(Predicate<T> match)
        {
            if (match == null) ThrowHelper.ArgumentNull("match");
            var list = new List<T>();
            for (int i = 0; i < _size; i++)
                if (match(_items[i])) list.Add(_items[i]);
            return list;
        }

        public int FindIndex(Predicate<T> match) => FindIndex(0, _size, match);
        public int FindIndex(int startIndex, Predicate<T> match) => FindIndex(startIndex, _size - startIndex, match);

        public int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            if ((uint)startIndex > (uint)_size) ThrowHelper.ArgumentOutOfRange("startIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (count < 0 || startIndex > _size - count) ThrowHelper.ArgumentOutOfRange("count", "Count must be positive and count must refer to a location within the string/array/collection.");
            if (match == null) ThrowHelper.ArgumentNull("match");
            int end = startIndex + count;
            for (int i = startIndex; i < end; i++)
                if (match(_items[i])) return i;
            return -1;
        }

        public T FindLast(Predicate<T> match)
        {
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = _size - 1; i >= 0; i--)
                if (match(_items[i])) return _items[i];
            return default(T);
        }

        public int FindLastIndex(Predicate<T> match)
        {
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = _size - 1; i >= 0; i--)
                if (match(_items[i])) return i;
            return -1;
        }

        public void ForEach(Action<T> action)
        {
            if (action == null) ThrowHelper.ArgumentNull("action");
            int version = _version;
            for (int i = 0; i < _size; i++)
            {
                if (version != _version) break;
                action(_items[i]);
            }
            if (version != _version) ThrowHelper.VersionChanged();
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadOnly() =>
            new System.Collections.ObjectModel.ReadOnlyCollection<T>((IList<T>)(object)this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new ListEnumeratorObject<T>(this);

        IEnumerator IEnumerable.GetEnumerator() => new ListEnumeratorObject<T>(this);

        // ---- non-generic IList / ICollection (List.cs) ----

        bool IList.IsFixedSize => false;
        bool IList.IsReadOnly => false;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;

        private static bool IsCompatibleObject(object value) => value is T || (value == null && default(T) == null);

        private static void IfNullAndNullsAreIllegalThenThrow(object value, string argument)
        {
            if (!(default(T) == null) && value == null) throw new ArgumentNullException(argument);
        }

        private static ArgumentException WrongValueType(object value) =>
            new ArgumentException(string.Format("The value \"{0}\" is not of type \"{1}\" and cannot be used in this generic collection.", value, typeof(T)), "value");

        object IList.this[int index]
        {
            get => this[index];
            set
            {
                IfNullAndNullsAreIllegalThenThrow(value, "value");
                if (!IsCompatibleObject(value)) throw WrongValueType(value);
                this[index] = (T)value;
            }
        }

        int IList.Add(object item)
        {
            IfNullAndNullsAreIllegalThenThrow(item, "item");
            if (!IsCompatibleObject(item)) throw WrongValueType(item);
            Add((T)item);
            return Count - 1;
        }

        bool IList.Contains(object item) => IsCompatibleObject(item) && Contains((T)item);

        int IList.IndexOf(object item) => IsCompatibleObject(item) ? IndexOf((T)item) : -1;

        void IList.Insert(int index, object item)
        {
            IfNullAndNullsAreIllegalThenThrow(item, "item");
            if (!IsCompatibleObject(item)) throw WrongValueType(item);
            Insert(index, (T)item);
        }

        void IList.Remove(object item)
        {
            if (IsCompatibleObject(item)) Remove((T)item);
        }

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            if (array != null && array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.");
            try { Array.Copy(_items, 0, array, arrayIndex, _size); }
            catch (ArrayTypeMismatchException) { throw new ArgumentException("Target array type is not compatible with the type of items in the collection."); }
        }

        public List<T> GetRange(int index, int count)
        {
            CheckRange(index, count);
            var list = new List<T>(count);
            for (int i = 0; i < count; i++) list.Add(_items[index + i]);
            return list;
        }

        public List<T> Slice(int start, int length) => GetRange(start, length);

        public int IndexOf(T item) => IndexOf(item, 0, _size);

        public int IndexOf(T item, int index)
        {
            if ((uint)index > (uint)_size) ThrowHelper.ArgumentOutOfRange("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            return IndexOf(item, index, _size - index);
        }

        public int IndexOf(T item, int index, int count)
        {
            if ((uint)index > (uint)_size) ThrowHelper.ArgumentOutOfRange("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (count < 0 || index > _size - count) ThrowHelper.ArgumentOutOfRange("count", "Count must be positive and count must refer to a location within the string/array/collection.");
            int end = index + count;
            for (int i = index; i < end; i++)
                if (CoreIntrinsics.AreEqual(_items[i], item)) return i;
            return -1;
        }

        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)_size) ThrowHelper.ArgumentOutOfRange("index", "Index must be within the bounds of the List.");
            if (_size == _items.Length) Grow(_size + 1);
            for (int i = _size; i > index; i--) _items[i] = _items[i - 1];
            _items[index] = item;
            _size++;
            _version++;
        }

        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            if ((uint)index > (uint)_size) ThrowHelper.ArgumentOutOfRange("index", "Index must be within the bounds of the List.");
            var items = new List<T>(collection);
            int count = items.Count;
            if (count == 0) return;
            if (_items.Length - _size < count) Grow(_size + count);
            for (int i = _size - 1; i >= index; i--) _items[i + count] = _items[i];
            for (int i = 0; i < count; i++) _items[index + i] = items[i];
            _size += count;
            _version++;
        }

        public int LastIndexOf(T item)
        {
            for (int i = _size - 1; i >= 0; i--)
                if (CoreIntrinsics.AreEqual(_items[i], item)) return i;
            return -1;
        }

        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }

        public int RemoveAll(Predicate<T> match)
        {
            if (match == null) ThrowHelper.ArgumentNull("match");
            int freeIndex = 0;
            while (freeIndex < _size && !match(_items[freeIndex])) freeIndex++;
            if (freeIndex >= _size) return 0;
            int current = freeIndex + 1;
            while (current < _size)
            {
                while (current < _size && match(_items[current])) current++;
                if (current < _size) _items[freeIndex++] = _items[current++];
            }
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                for (int i = freeIndex; i < _size; i++) _items[i] = default(T);
            int result = _size - freeIndex;
            _size = freeIndex;
            _version++;
            return result;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size) throw CoreIntrinsics.LibraryFrames(ThrowHelper.IndexOutOfRangeException(), "System.Collections.Generic.List`1.RemoveAt(Int32 index)");
            _size--;
            for (int i = index; i < _size; i++) _items[i] = _items[i + 1];
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) _items[_size] = default(T);
            _version++;
        }

        public void RemoveRange(int index, int count)
        {
            CheckRange(index, count);
            if (count > 0)
            {
                _size -= count;
                for (int i = index; i < _size; i++) _items[i] = _items[i + count];
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                    for (int i = _size; i < _size + count; i++) _items[i] = default(T);
                _version++;
            }
        }

        public void Reverse() => Reverse(0, _size);

        public void Reverse(int index, int count)
        {
            CheckRange(index, count);
            int i = index, j = index + count - 1;
            while (i < j)
            {
                T t = _items[i]; _items[i] = _items[j]; _items[j] = t;
                i++; j--;
            }
            _version++;
        }

        public void Sort() => Sort(0, _size, null);

        public void Sort(IComparer<T> comparer) => Sort(0, _size, comparer);

        public void Sort(int index, int count, IComparer<T> comparer)
        {
            CheckRange(index, count);
            SortHelper<T>.Sort(_items, index, count, comparer);
            _version++;
        }

        public void Sort(Comparison<T> comparison)
        {
            if (comparison == null) ThrowHelper.ArgumentNull("comparison");
            SortHelper<T>.Sort(_items, 0, _size, comparison);
            _version++;
        }

        public T[] ToArray()
        {
            T[] array = new T[_size];
            for (int i = 0; i < _size; i++) array[i] = _items[i];
            return array;
        }

        public void TrimExcess()
        {
            if (_size < (int)(_items.Length * 0.9)) Capacity = _size;
        }

        public bool TrueForAll(Predicate<T> match)
        {
            if (match == null) ThrowHelper.ArgumentNull("match");
            for (int i = 0; i < _size; i++)
                if (!match(_items[i])) return false;
            return true;
        }

        private void CheckRange(int index, int count)
        {
            if (index < 0) ThrowHelper.ArgumentOutOfRange("index", "Non-negative number required.");
            if (count < 0) ThrowHelper.ArgumentOutOfRange("count", "Non-negative number required.");
            if (_size - index < count) ThrowHelper.Argument("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }

        [Shadows("System.Collections.Generic.List`1+Enumerator")]
        public struct Enumerator : IEnumerator<T>
        {
            private readonly ListImpl<T> _list;
            private int _index;
            private readonly int _version;
            private T _current;

            internal Enumerator(ListImpl<T> list)
            {
                _list = list;
                _index = 0;
                _version = list._version;
                _current = default(T);
            }

            public void Dispose() { }

            public bool MoveNext()
            {
                ListImpl<T> localList = _list;
                if (_version == localList._version && ((uint)_index < (uint)localList._size))
                {
                    _current = localList._items[_index];
                    _index++;
                    return true;
                }
                if (_version != _list._version) ThrowHelper.VersionChanged();
                _index = _list._size + 1;
                _current = default(T);
                return false;
            }

            public T Current => _current;

            object IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || _index == _list._size + 1) ThrowHelper.EnumOpCantHappen();
                    return CoreIntrinsics.Box(_current);
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _list._version) ThrowHelper.VersionChanged();
                _index = 0;
                _current = default(T);
            }
        }
    }

    /// <summary>Heap enumerator for List&lt;T&gt; used through IEnumerable&lt;T&gt; (LINQ, interfaces).</summary>
    internal sealed class ListEnumeratorObject<T> : IEnumerator<T>
    {
        private readonly ListImpl<T> _list;
        private int _index;
        private readonly int _version;
        private T _current;

        public ListEnumeratorObject(ListImpl<T> list) { _list = list; _version = list._version; }

        public bool MoveNext()
        {
            if (_version == _list._version && (uint)_index < (uint)_list._size)
            {
                _current = _list._items[_index];
                _index++;
                return true;
            }
            if (_version != _list._version) ThrowHelper.VersionChanged();
            _index = _list._size + 1;
            _current = default(T);
            return false;
        }

        public T Current => _current;
        object IEnumerator.Current => CoreIntrinsics.Box(_current);
        public void Reset() { _index = 0; _current = default(T); }
        public void Dispose() { }
    }

    /// <summary>CollectionsMarshal over List&lt;T&gt;'s storage (the span aliases the list's array, as in .NET).</summary>
    [Shadows("System.Runtime.InteropServices.CollectionsMarshal")]
    public static class CollectionsMarshalImpl
    {
        public static Span<T> AsSpan<T>(List<T> list)
        {
            if (list == null) return default(Span<T>);
            ListImpl<T> l = (ListImpl<T>)(object)list;
            return new Span<T>(l._items, 0, l._size);
        }

        // Dictionary<TKey, TValue>'s value slots, as .NET hands them out (corelib/Dictionary.cs).
        public static ref TValue GetValueRefOrAddDefault<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, out bool exists) =>
            ref ((DictionaryImpl<TKey, TValue>)(object)dictionary).GetValueRefOrAddDefault(key, out exists);

        public static ref TValue GetValueRefOrNullRef<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key) =>
            ref ((DictionaryImpl<TKey, TValue>)(object)dictionary).GetValueRefOrNullRef(key);

        public static void SetCount<T>(List<T> list, int count)
        {
            if (count < 0) ThrowHelper.ArgumentOutOfRange("count", "Non-negative number required.");
            ListImpl<T> l = (ListImpl<T>)(object)list;
            l._version++;
            if (count > l.Capacity) l.GrowForMarshal(count);
            else if (count < l._size && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                for (int i = count; i < l._size; i++) l._items[i] = default(T);
            l._size = count;
        }
    }
}
