// System.Collections.Concurrent.ConcurrentQueue<T>: FIFO under one lock. .NET's lock-free segments
// aren't observable from one thread; what is — FIFO order, enumeration and ToArray/CopyTo as a
// moment-in-time snapshot, exception types and messages — follows .NET.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Concurrent.ConcurrentQueue`1")]
    public class ConcurrentQueueImpl<T> : System.Collections.Concurrent.IProducerConsumerCollection<T>, IReadOnlyCollection<T>
    {
        private readonly object _lock = new object();
        private T[] _items = new T[4];
        private int _head, _count;

        public ConcurrentQueueImpl() { }

        public ConcurrentQueueImpl(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException("collection");
            foreach (T item in collection) Enqueue(item);
        }

        public bool IsEmpty { get { lock (_lock) return _count == 0; } }
        public int Count { get { lock (_lock) return _count; } }

        public void Enqueue(T item)
        {
            lock (_lock)
            {
                if (_count == _items.Length)
                {
                    T[] grown = new T[_items.Length * 2];
                    for (int i = 0; i < _count; i++) grown[i] = _items[(_head + i) % _items.Length];
                    _items = grown;
                    _head = 0;
                }
                _items[(_head + _count) % _items.Length] = item;
                _count++;
            }
        }

        public bool TryDequeue(out T result)
        {
            lock (_lock)
            {
                if (_count == 0) { result = default(T); return false; }
                result = _items[_head];
                _items[_head] = default(T);
                _head = (_head + 1) % _items.Length;
                _count--;
                return true;
            }
        }

        public bool TryPeek(out T result)
        {
            lock (_lock)
            {
                if (_count == 0) { result = default(T); return false; }
                result = _items[_head];
                return true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items = new T[4];
                _head = 0;
                _count = 0;
            }
        }

        public T[] ToArray()
        {
            lock (_lock)
            {
                T[] array = new T[_count];
                for (int i = 0; i < _count; i++) array[i] = _items[(_head + i) % _items.Length];
                return array;
            }
        }

        public void CopyTo(T[] array, int index)
        {
            if (array == null) throw new ArgumentNullException("array");
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            T[] snapshot = ToArray();
            if (index > array.Length - snapshot.Length)
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            System.Array.Copy(snapshot, 0, array, index, snapshot.Length);
        }

        void ICollection.CopyTo(System.Array array, int index)
        {
            if (array is T[] typed) { CopyTo(typed, index); return; }
            if (array == null) throw new ArgumentNullException("array");
            T[] items = ToArray();
            System.Array.Copy(items, 0, array, index, items.Length);
        }

        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => throw new NotSupportedException("The SyncRoot property may not be used for the synchronization of concurrent collections.");

        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Enqueue(item);
            return true;
        }

        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryTake(out T item) => TryDequeue(out item);

        /// <summary>A moment-in-time snapshot: later Enqueue/TryDequeue calls don't change what it yields.</summary>
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)ToArray()).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
