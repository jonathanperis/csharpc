// System.Collections.ObjectModel.Collection<T>: a list whose changes go through four protected
// virtuals (InsertItem, SetItem, RemoveItem, ClearItems), the base of many library collections
// (Microsoft.Net.Http.Headers' ObjectCollection<T>, ASP.NET Core's metadata collections). Behaves as
// dotnet/runtime's Collection.cs, with its exception types, messages and parameter names.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.ObjectModel.Collection`1")]
    public class CollectionImpl<T> : IList<T>, IList, IReadOnlyList<T>
    {
        private readonly IList<T> items;

        public CollectionImpl() => items = new List<T>();

        public CollectionImpl(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException("list");
            items = list;
        }

        public int Count => items.Count;

        protected IList<T> Items => items;

        public T this[int index]
        {
            get => items[index];
            set
            {
                if (items.IsReadOnly) throw ReadOnly();
                if ((uint)index >= (uint)items.Count) throw IndexMustBeLess();
                SetItem(index, value);
            }
        }

        public void Add(T item)
        {
            if (items.IsReadOnly) throw ReadOnly();
            InsertItem(items.Count, item);
        }

        public void Clear()
        {
            if (items.IsReadOnly) throw ReadOnly();
            ClearItems();
        }

        public void CopyTo(T[] array, int index) => items.CopyTo(array, index);

        public bool Contains(T item) => items.Contains(item);

        public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

        public int IndexOf(T item) => items.IndexOf(item);

        public void Insert(int index, T item)
        {
            if (items.IsReadOnly) throw ReadOnly();
            if ((uint)index > (uint)items.Count)
                throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            InsertItem(index, item);
        }

        public bool Remove(T item)
        {
            if (items.IsReadOnly) throw ReadOnly();
            int index = items.IndexOf(item);
            if (index < 0) return false;
            RemoveItem(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            if (items.IsReadOnly) throw ReadOnly();
            if ((uint)index >= (uint)items.Count) throw IndexMustBeLess();
            RemoveItem(index);
        }

        protected virtual void ClearItems() => items.Clear();

        protected virtual void InsertItem(int index, T item) => items.Insert(index, item);

        protected virtual void RemoveItem(int index) => items.RemoveAt(index);

        protected virtual void SetItem(int index, T item) => items[index] = item;

        bool ICollection<T>.IsReadOnly => items.IsReadOnly;

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)items).GetEnumerator();

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => items is ICollection c ? c.SyncRoot : this;

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null) throw new ArgumentNullException("array");
            if (array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
            if (index < 0) throw new ArgumentOutOfRangeException("index", "Non-negative number required.");
            if (array.Length - index < Count)
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            if (array is T[] typed)
            {
                items.CopyTo(typed, index);
                return;
            }
            if (array is object[] objects)
            {
                try
                {
                    int count = items.Count;
                    for (int i = 0; i < count; i++) objects[index++] = items[i];
                }
                catch (ArrayTypeMismatchException)
                {
                    throw InvalidArrayType();
                }
                return;
            }
            throw InvalidArrayType();
        }

        object IList.this[int index]
        {
            get => items[index];
            set
            {
                ThrowIfNullAndNullsAreIllegal(value);
                T item;
                try { item = (T)value; }
                catch (InvalidCastException) { throw WrongType(value); }
                this[index] = item;
            }
        }

        bool IList.IsReadOnly => items.IsReadOnly;

        bool IList.IsFixedSize => items is IList list ? list.IsFixedSize : items.IsReadOnly;

        int IList.Add(object value)
        {
            if (items.IsReadOnly) throw ReadOnly();
            ThrowIfNullAndNullsAreIllegal(value);
            try { Add((T)value); }
            catch (InvalidCastException) { throw WrongType(value); }
            return Count - 1;
        }

        bool IList.Contains(object value) => IsCompatibleObject(value) && Contains((T)value);

        int IList.IndexOf(object value) => IsCompatibleObject(value) ? IndexOf((T)value) : -1;

        void IList.Insert(int index, object value)
        {
            if (items.IsReadOnly) throw ReadOnly();
            ThrowIfNullAndNullsAreIllegal(value);
            try { Insert(index, (T)value); }
            catch (InvalidCastException) { throw WrongType(value); }
        }

        void IList.Remove(object value)
        {
            if (items.IsReadOnly) throw ReadOnly();
            if (IsCompatibleObject(value)) Remove((T)value);
        }

        private static bool IsCompatibleObject(object value) => value is T || (value == null && default(T) == null);

        private static void ThrowIfNullAndNullsAreIllegal(object value)
        {
            if (!(default(T) == null) && value == null) throw new ArgumentNullException("value");
        }

        private static ArgumentException WrongType(object value) =>
            new ArgumentException("The value \"" + value + "\" is not of type \"" + typeof(T) + "\" and cannot be used in this generic collection.", "value");

        private static ArgumentException InvalidArrayType() =>
            new ArgumentException("Target array type is not compatible with the type of items in the collection.", "array");

        private static NotSupportedException ReadOnly() => new NotSupportedException("Collection is read-only.");

        private static ArgumentOutOfRangeException IndexMustBeLess() =>
            new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
    }
}
