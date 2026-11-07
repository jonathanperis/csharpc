// System.Collections.ObjectModel.ReadOnlyCollection<T>: a read-only view over an IList<T> (changes
// to the list show through), as .NET's.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.ObjectModel.ReadOnlyCollection`1")]
    public class ReadOnlyCollectionImpl<T> : IList<T>, IList, IReadOnlyList<T>
    {
        private readonly IList<T> _list;

        public ReadOnlyCollectionImpl(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException("list");
            _list = list;
        }

        public static System.Collections.ObjectModel.ReadOnlyCollection<T> Empty { get; } =
            (System.Collections.ObjectModel.ReadOnlyCollection<T>)(object)new ReadOnlyCollectionImpl<T>(new T[0]);

        protected IList<T> Items => _list;
        public int Count => _list.Count;
        public T this[int index] => _list[index];
        public bool Contains(T value) => _list.Contains(value);
        public void CopyTo(T[] array, int index) => _list.CopyTo(array, index);
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        public int IndexOf(T value) => _list.IndexOf(value);

        T IList<T>.this[int index] { get => _list[index]; set => throw ReadOnly(); }
        bool ICollection<T>.IsReadOnly => true;
        void ICollection<T>.Add(T value) => throw ReadOnly();
        void ICollection<T>.Clear() => throw ReadOnly();
        void IList<T>.Insert(int index, T value) => throw ReadOnly();
        bool ICollection<T>.Remove(T value) => throw ReadOnly();
        void IList<T>.RemoveAt(int index) => throw ReadOnly();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_list).GetEnumerator();

        // The non-generic IList/ICollection views, as .NET's: every mutation is NotSupported, SyncRoot is the list's.
        private object _syncRoot;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot
        {
            get
            {
                if (_syncRoot == null)
                {
                    if (_list is ICollection c) _syncRoot = c.SyncRoot;
                    else System.Threading.Interlocked.CompareExchange<object>(ref _syncRoot, new object(), null);
                }
                return _syncRoot;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null) throw new ArgumentNullException("array");
            if (array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.");
            if (array.GetLowerBound(0) != 0) throw new ArgumentException("The lower bound of target array must be zero.");
            if (index < 0) throw new ArgumentOutOfRangeException("index", "Non-negative number required.");
            if (array.Length - index < Count)
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            if (array is T[] typed)
            {
                _list.CopyTo(typed, index);
                return;
            }
            Type targetType = array.GetType().GetElementType();
            Type sourceType = typeof(T);
            if (!(targetType.IsAssignableFrom(sourceType) || sourceType.IsAssignableFrom(targetType)) || !(array is object[] objects))
                throw InvalidArrayType();
            try
            {
                int count = _list.Count;
                for (int i = 0; i < count; i++) objects[index++] = _list[i];
            }
            catch (ArrayTypeMismatchException)
            {
                throw InvalidArrayType();
            }
        }

        object IList.this[int index]
        {
            get => _list[index];
            set => throw ReadOnly();
        }

        bool IList.IsFixedSize => true;
        bool IList.IsReadOnly => true;
        int IList.Add(object value) => throw ReadOnly();
        void IList.Clear() => throw ReadOnly();
        bool IList.Contains(object value) => IsCompatibleObject(value) && Contains((T)value);
        int IList.IndexOf(object value) => IsCompatibleObject(value) ? IndexOf((T)value) : -1;
        void IList.Insert(int index, object value) => throw ReadOnly();
        void IList.Remove(object value) => throw ReadOnly();
        void IList.RemoveAt(int index) => throw ReadOnly();

        private static bool IsCompatibleObject(object value) => value is T || (value == null && default(T) == null);

        private static ArgumentException InvalidArrayType() =>
            new ArgumentException("Target array type is not compatible with the type of items in the collection.");

        private static NotSupportedException ReadOnly() => new NotSupportedException("Collection is read-only.");
    }
}
