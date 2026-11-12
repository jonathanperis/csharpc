// Queue<T>, a System.Private.CoreLib type (the CoreLib slice, ADR 0023). The other collections of System.Collections.dll
// (Stack, LinkedList, PriorityQueue, Sorted*, OrderedDictionary) come from the framework's IL.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Generic.Queue`1")]
    public class QueueImpl<T> : IEnumerable<T>, IReadOnlyCollection<T>, ICollection
    {
        // ICollection (.NET's Queue<T>/Stack<T> implement it; TryGetNonEnumeratedCount and `is ICollection` see it).
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
            ThrowHelper.IfNegative(index, "index");
            if (array.Length - index < _size) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            Array.Copy(ToArray(), 0, array, index, _size);
        }

        internal T[] _array;
        internal int _head;
        private int _tail;
        internal int _size;
        internal int _version;

        public QueueImpl() { _array = new T[0]; }

        public QueueImpl(int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            _array = new T[capacity];
        }

        public QueueImpl(IEnumerable<T> collection)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            _array = new T[0];
            foreach (T item in collection) Enqueue(item);
        }

        public int Count => _size;
        public int Capacity => _array.Length;

        public void Clear()
        {
            for (int i = 0; i < _array.Length; i++) _array[i] = default(T);
            _size = 0;
            _head = 0;
            _tail = 0;
            _version++;
        }

        public void Enqueue(T item)
        {
            if (_size == _array.Length) Grow(_size + 1);
            _array[_tail] = item;
            _tail = _tail + 1 == _array.Length ? 0 : _tail + 1;
            _size++;
            _version++;
        }

        public T Dequeue()
        {
            if (_size == 0) throw CoreIntrinsics.LibraryFrames(new InvalidOperationException("Queue empty."), "System.Collections.Generic.Queue`1.ThrowForEmptyQueue()\nSystem.Collections.Generic.Queue`1.Dequeue()");
            T removed = _array[_head];
            _array[_head] = default(T);
            _head = _head + 1 == _array.Length ? 0 : _head + 1;
            _size--;
            _version++;
            return removed;
        }

        public bool TryDequeue(out T result)
        {
            if (_size == 0) { result = default(T); return false; }
            result = Dequeue();
            return true;
        }

        public T Peek()
        {
            if (_size == 0) ThrowHelper.EmptyQueue();
            return _array[_head];
        }

        public bool TryPeek(out T result)
        {
            if (_size == 0) { result = default(T); return false; }
            result = _array[_head];
            return true;
        }

        public bool Contains(T item)
        {
            for (int i = 0; i < _size; i++)
                if (CoreIntrinsics.AreEqual(_array[(_head + i) % _array.Length], item)) return true;
            return false;
        }

        public T[] ToArray()
        {
            T[] result = new T[_size];
            for (int i = 0; i < _size; i++) result[i] = _array[(_head + i) % _array.Length];
            return result;
        }

        // .NET's checks, then Array.Copy of the one or two runs of the ring buffer: Array.Copy's rules apply to a
        // covariant destination (a string[] passed as object[] casts each element: InvalidCastException at the
        // first misfit, after the elements before it), where element stores would throw ArrayTypeMismatchException.
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) ThrowHelper.ArgumentNull("array");
            if (arrayIndex < 0 || arrayIndex > array.Length)
                ThrowHelper.ArgumentOutOfRange("arrayIndex", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (array.Length - arrayIndex < _size) ThrowHelper.Argument(ArrayChecks.InvalidOffLen);
            int numToCopy = _size;
            if (numToCopy == 0) return;
            int firstPart = Math.Min(_array.Length - _head, numToCopy);
            Array.Copy(_array, _head, array, arrayIndex, firstPart);
            numToCopy -= firstPart;
            if (numToCopy > 0) Array.Copy(_array, 0, array, arrayIndex + _array.Length - _head, numToCopy);
        }

        public int EnsureCapacity(int capacity)
        {
            ThrowHelper.IfNegative(capacity, "capacity");
            if (_array.Length < capacity) Grow(capacity);
            return _array.Length;
        }

        public void TrimExcess() { if (_size < (int)(_array.Length * 0.9)) SetCapacity(_size); }

        private void Grow(int capacity)
        {
            int newcapacity = 2 * _array.Length;
            if (newcapacity < _array.Length + 4) newcapacity = _array.Length + 4;
            if (newcapacity < capacity) newcapacity = capacity;
            SetCapacity(newcapacity);
        }

        private void SetCapacity(int capacity)
        {
            T[] newarray = new T[capacity];
            for (int i = 0; i < _size; i++) newarray[i] = _array[(_head + i) % _array.Length];
            _array = newarray;
            _head = 0;
            _tail = _size == capacity ? 0 : _size;
            _version++;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new SimpleEnumerator<T>(ToArray());
        IEnumerator IEnumerable.GetEnumerator() => new SimpleEnumerator<T>(ToArray());

        [Shadows("System.Collections.Generic.Queue`1+Enumerator")]
        public struct Enumerator : IEnumerator<T>
        {
            private readonly QueueImpl<T> _q;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(QueueImpl<T> q) { _q = q; _version = q._version; _index = -1; _current = default(T); }

            public bool MoveNext()
            {
                if (_version != _q._version) ThrowHelper.VersionChanged();
                if (_index == -2) return false;
                _index++;
                if (_index == _q._size) { _index = -2; _current = default(T); return false; }
                _current = _q._array[(_q._head + _index) % _q._array.Length];
                return true;
            }

            // .NET 11's Queue/Stack enumerators don't validate the state in Current (either form).
            public T Current => _current;
            object IEnumerator.Current => CoreIntrinsics.Box(_current);

            public void Dispose() { _index = -2; _current = default(T); }

            void IEnumerator.Reset()
            {
                if (_version != _q._version) ThrowHelper.VersionChanged();
                _index = -1;
                _current = default(T);
            }
        }
    }

    /// <summary>Snapshot enumerator used when a collection is enumerated through an interface.</summary>
    internal sealed class SimpleEnumerator<T> : IEnumerator<T>
    {
        private readonly T[] _items;
        private int _index = -1;

        public SimpleEnumerator(T[] items) { _items = items; }
        public bool MoveNext() => ++_index < _items.Length;
        public T Current => _items[_index];
        object IEnumerator.Current => CoreIntrinsics.Box(_items[_index]);
        public void Reset() { _index = -1; }
        public void Dispose() { }
    }
}
