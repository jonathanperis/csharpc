// System.Collections.Concurrent.ConcurrentBag<T> with .NET's observable order (dotnet/runtime
// release/11.0 ConcurrentBag.cs) under one lock: each thread adds to and takes from the tail of its
// own queue (LIFO); a thread with nothing left steals from the head of the other threads' queues,
// first those created before its own, then from the newest; ToArray/CopyTo/enumeration list the
// queues newest first, each newest item first.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Concurrent.ConcurrentBag`1")]
    public class ConcurrentBagImpl<T> : System.Collections.Concurrent.IProducerConsumerCollection<T>, IReadOnlyCollection<T>
    {
        private sealed class ThreadQueue
        {
            internal readonly int Owner;
            internal readonly ThreadQueue Next; // created earlier
            internal readonly List<T> Items = new List<T>(); // head (oldest) first

            internal ThreadQueue(int owner, ThreadQueue next)
            {
                Owner = owner;
                Next = next;
            }
        }

        private readonly object _lock = new object();
        private ThreadQueue _queues; // newest first

        public ConcurrentBagImpl() { }

        public ConcurrentBagImpl(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException("collection");
            foreach (T item in collection) Add(item);
        }

        private ThreadQueue Local(bool create)
        {
            int thread = Environment.CurrentManagedThreadId;
            for (ThreadQueue queue = _queues; queue != null; queue = queue.Next)
                if (queue.Owner == thread) return queue;
            return create ? (_queues = new ThreadQueue(thread, _queues)) : null;
        }

        public void Add(T item)
        {
            lock (_lock) Local(true).Items.Add(item);
        }

        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Add(item);
            return true;
        }

        public bool TryTake(out T result) => TryTakeOrPeek(out result, true);
        public bool TryPeek(out T result) => TryTakeOrPeek(out result, false);
        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryTake(out T item) => TryTake(out item);

        private bool TryTakeOrPeek(out T result, bool take)
        {
            lock (_lock)
            {
                ThreadQueue local = Local(false);
                if (local != null && local.Items.Count > 0)
                {
                    int last = local.Items.Count - 1;
                    result = local.Items[last];
                    if (take) local.Items.RemoveAt(last);
                    return true;
                }
                // Steal from the head: the queues after the local one, then from the newest up to it.
                if (local == null ? Steal(_queues, null, out result, take) : Steal(local.Next, null, out result, take) || Steal(_queues, local, out result, take))
                    return true;
                result = default(T);
                return false;
            }
        }

        private static bool Steal(ThreadQueue start, ThreadQueue end, out T result, bool take)
        {
            for (ThreadQueue queue = start; queue != end; queue = queue.Next)
            {
                if (queue.Items.Count == 0) continue;
                result = queue.Items[0];
                if (take) queue.Items.RemoveAt(0);
                return true;
            }
            result = default(T);
            return false;
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    for (ThreadQueue queue = _queues; queue != null; queue = queue.Next) checked { count += queue.Items.Count; }
                    return count;
                }
            }
        }

        public bool IsEmpty => Count == 0;

        public void Clear()
        {
            lock (_lock)
                for (ThreadQueue queue = _queues; queue != null; queue = queue.Next) queue.Items.Clear();
        }

        public T[] ToArray()
        {
            lock (_lock)
            {
                int count = 0;
                for (ThreadQueue queue = _queues; queue != null; queue = queue.Next) count += queue.Items.Count;
                if (count == 0) return System.Array.Empty<T>();
                T[] array = new T[count];
                CopyQueues(array, 0);
                return array;
            }
        }

        private void CopyQueues(T[] array, int index)
        {
            for (ThreadQueue queue = _queues; queue != null; queue = queue.Next)
                for (int i = queue.Items.Count - 1; i >= 0; i--) array[index++] = queue.Items[i];
        }

        public void CopyTo(T[] array, int index)
        {
            if (array == null) throw new ArgumentNullException("array");
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            lock (_lock)
            {
                if (_queues == null) return; // .NET: nothing ever added, nothing checked
                int count = 0;
                for (ThreadQueue queue = _queues; queue != null; queue = queue.Next) count += queue.Items.Count;
                if (index > array.Length - count)
                    throw new ArgumentException("The number of elements in the collection is greater than the available space from index to the end of the destination array.", "index");
                CopyQueues(array, index);
            }
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

        /// <summary>A moment-in-time snapshot (ToArray's order).</summary>
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)ToArray()).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
