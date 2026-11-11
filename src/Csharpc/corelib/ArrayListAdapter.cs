// ArrayList.Adapter (after dotnet/runtime's ArrayList.IListWrapper): the IList behind the ArrayList API. Every operation is the
// list's, so changes go through to it; System.ComponentModel's TypeDescriptor sorts its descriptor arrays this way.
#nullable disable
using System;
using System.Collections;

namespace CsharpcCoreLib
{
    public partial class ArrayListImpl
    {
        public static ArrayList Adapter(IList list)
        {
            if (list == null) throw new ArgumentNullException("list");
            return (ArrayList)(object)new IListWrapper(list);
        }

        private const string RangeMessage = "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";

        private sealed class IListWrapper : ArrayListImpl
        {
            private readonly IList _list;
            internal IListWrapper(IList list) { _list = list; }

            public override int Capacity
            {
                get => _list.Count;
                set { if (value < _list.Count) throw new ArgumentOutOfRangeException("value", "capacity was less than the current size."); }
            }
            public override int Count => _list.Count;
            public override bool IsReadOnly => _list.IsReadOnly;
            public override bool IsFixedSize => _list.IsFixedSize;
            public override bool IsSynchronized => _list.IsSynchronized;
            public override object this[int index] { get => _list[index]; set => _list[index] = value; }
            public override object SyncRoot => _list.SyncRoot;

            public override int Add(object obj) { _list.Add(obj); return Count - 1; }

            public override void AddRange(ICollection c)
            {
                if (c == null) throw new ArgumentNullException("c");
                if (_list.IsFixedSize) throw new NotSupportedException("Collection was of a fixed size.");
                foreach (object item in c) _list.Add(item);
            }

            public override int BinarySearch(int index, int count, object value, IComparer comparer)
            {
                CheckRange(index, count, _list.Count);
                comparer ??= Comparer.Default;
                int lo = index, hi = index + count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    int order = comparer.Compare(value, _list[mid]);
                    if (order == 0) return mid;
                    if (order < 0) hi = mid - 1; else lo = mid + 1;
                }
                return ~lo;
            }

            public override void Clear() => _list.Clear();
            public override object Clone() => new IListWrapper(_list);
            public override bool Contains(object obj) => _list.Contains(obj);
            public override void CopyTo(Array array, int index) => _list.CopyTo(array, index);

            public override void CopyTo(int index, Array array, int arrayIndex, int count)
            {
                if (array == null) throw new ArgumentNullException("array");
                if (index < 0 || arrayIndex < 0) throw new ArgumentOutOfRangeException(index < 0 ? "index" : "arrayIndex", "Non-negative number required.");
                if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
                if (array.Length - arrayIndex < count || _list.Count - index < count) throw new ArgumentException(RangeMessage);
                if (array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
                for (int i = index; i < index + count; i++) array.SetValue(_list[i], arrayIndex++);
            }

            public override IEnumerator GetEnumerator() => _list.GetEnumerator();

            public override IEnumerator GetEnumerator(int index, int count)
            {
                CheckRange(index, count, _list.Count);
                return Range(index, count);
            }

            private IEnumerator Range(int index, int count)
            {
                for (int i = index; i < index + count; i++) yield return _list[i];
            }

            public override int IndexOf(object value) => _list.IndexOf(value);
            public override int IndexOf(object value, int startIndex) => IndexOf(value, startIndex, _list.Count - startIndex);

            public override int IndexOf(object value, int startIndex, int count)
            {
                if (startIndex < 0 || startIndex > _list.Count) throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
                if (count < 0 || startIndex > _list.Count - count) throw new ArgumentOutOfRangeException("count", "Count must be positive and count must refer to a location within the string/array/collection.");
                for (int i = startIndex; i < startIndex + count; i++)
                    if (value == null ? _list[i] == null : _list[i] != null && _list[i].Equals(value)) return i;
                return -1;
            }

            public override int LastIndexOf(object value)
            {
                for (int i = _list.Count - 1; i >= 0; i--)
                    if (value == null ? _list[i] == null : _list[i] != null && _list[i].Equals(value)) return i;
                return -1;
            }

            public override void Insert(int index, object obj) => _list.Insert(index, obj);

            public override void InsertRange(int index, ICollection c)
            {
                if (c == null) throw new ArgumentNullException("c");
                if (index < 0 || index > _list.Count) throw new ArgumentOutOfRangeException("index", "Index must be within the bounds of the List.");
                if (c.Count == 0) return;
                if (_list.IsFixedSize) throw new NotSupportedException("Collection was of a fixed size.");
                foreach (object item in c) _list.Insert(index++, item);
            }

            public override void Remove(object value)
            {
                int index = _list.IndexOf(value);
                if (index >= 0) RemoveAt(index);
            }

            public override void RemoveAt(int index) => _list.RemoveAt(index);

            public override void RemoveRange(int index, int count)
            {
                CheckRange(index, count, _list.Count);
                for (int i = 0; i < count; i++) _list.RemoveAt(index);
            }

            public override void Reverse(int index, int count)
            {
                CheckRange(index, count, _list.Count);
                int i = index, j = index + count - 1;
                while (i < j) { object tmp = _list[i]; _list[i++] = _list[j]; _list[j--] = tmp; }
            }

            public override void SetRange(int index, ICollection c)
            {
                if (c == null) throw new ArgumentNullException("c");
                if (index < 0 || index > _list.Count - c.Count) throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
                int i = index;
                foreach (object item in c) _list[i++] = item;
            }

            public override ArrayList GetRange(int index, int count)
            {
                CheckRange(index, count, _list.Count);
                var range = new ArrayListImpl(count);
                for (int i = 0; i < count; i++) range.Add(_list[index + i]);
                return (ArrayList)(object)range;
            }

            // The elements are sorted in an ArrayList of their own (a failing comparer is wrapped as there), then written back.
            public override void Sort(int index, int count, IComparer comparer)
            {
                CheckRange(index, count, _list.Count);
                var sorted = new ArrayListImpl(count);
                for (int i = 0; i < count; i++) sorted.Add(_list[index + i]);
                sorted.Sort(0, count, comparer);
                for (int i = 0; i < count; i++) _list[index + i] = sorted[i];
            }

            public override object[] ToArray()
            {
                object[] array = new object[_list.Count];
                _list.CopyTo(array, 0);
                return array;
            }

            public override void TrimToSize() { }
        }
    }
}
