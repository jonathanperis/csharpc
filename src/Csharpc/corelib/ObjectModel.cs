// System.Collections.ObjectModel.ReadOnlyCollection<T>: a read-only view over an IList<T> (changes
// to the list show through), as .NET's.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.ObjectModel.ReadOnlyCollection`1")]
    public class ReadOnlyCollectionImpl<T> : IList<T>, IReadOnlyList<T>
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

        private static NotSupportedException ReadOnly() => new NotSupportedException("Collection is read-only.");
    }
}
