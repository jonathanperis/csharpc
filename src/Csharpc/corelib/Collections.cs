// Queue<T>, Stack<T>, LinkedList<T>, PriorityQueue<TElement,TPriority>, SortedList/SortedDictionary/SortedSet.
// Growth policies and algorithms follow .NET where they're observable (e.g. PriorityQueue's 4-ary heap
// decides the dequeue order of equal priorities).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Generic.Queue`1")]
    public class QueueImpl<T> : IEnumerable<T>, IReadOnlyCollection<T>
    {
        internal T[] _array;
        internal int _head;
        private int _tail;
        internal int _size;
        internal int _version;

        public QueueImpl() { _array = new T[0]; }

        public QueueImpl(int capacity)
        {
            if (capacity < 0) ThrowHelper.ArgumentOutOfRange("capacity", "Non-negative number required.");
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
            if (_size == 0) ThrowHelper.EmptyQueue();
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

        public void CopyTo(T[] array, int arrayIndex)
        {
            for (int i = 0; i < _size; i++) array[arrayIndex + i] = _array[(_head + i) % _array.Length];
        }

        public int EnsureCapacity(int capacity)
        {
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
        public struct Enumerator
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

            public T Current => _current;
            public void Dispose() { _index = -2; }
        }
    }

    [Shadows("System.Collections.Generic.Stack`1")]
    public class StackImpl<T> : IEnumerable<T>, IReadOnlyCollection<T>
    {
        internal T[] _array;
        internal int _size;
        internal int _version;

        public StackImpl() { _array = new T[0]; }

        public StackImpl(int capacity)
        {
            if (capacity < 0) ThrowHelper.ArgumentOutOfRange("capacity", "Non-negative number required.");
            _array = new T[capacity];
        }

        public StackImpl(IEnumerable<T> collection)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            _array = new T[0];
            foreach (T item in collection) Push(item);
        }

        public int Count => _size;
        public int Capacity => _array.Length;

        public void Clear()
        {
            for (int i = 0; i < _size; i++) _array[i] = default(T);
            _size = 0;
            _version++;
        }

        public bool Contains(T item)
        {
            for (int i = _size - 1; i >= 0; i--)
                if (CoreIntrinsics.AreEqual(_array[i], item)) return true;
            return false;
        }

        public T Peek()
        {
            if (_size == 0) ThrowHelper.EmptyStack();
            return _array[_size - 1];
        }

        public bool TryPeek(out T result)
        {
            if (_size == 0) { result = default(T); return false; }
            result = _array[_size - 1];
            return true;
        }

        public T Pop()
        {
            if (_size == 0) ThrowHelper.EmptyStack();
            _version++;
            T item = _array[--_size];
            _array[_size] = default(T);
            return item;
        }

        public bool TryPop(out T result)
        {
            if (_size == 0) { result = default(T); return false; }
            result = Pop();
            return true;
        }

        public void Push(T item)
        {
            if (_size >= _array.Length)
            {
                int newCapacity = _array.Length == 0 ? 4 : 2 * _array.Length;
                T[] grown = new T[newCapacity];
                for (int i = 0; i < _size; i++) grown[i] = _array[i];
                _array = grown;
            }
            _array[_size++] = item;
            _version++;
        }

        public T[] ToArray()
        {
            T[] result = new T[_size];
            for (int i = 0; i < _size; i++) result[i] = _array[_size - 1 - i];
            return result;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            for (int i = 0; i < _size; i++) array[arrayIndex + i] = _array[_size - 1 - i];
        }

        public int EnsureCapacity(int capacity)
        {
            if (_array.Length < capacity)
            {
                int newCapacity = Math.Max(capacity, _array.Length == 0 ? 4 : 2 * _array.Length);
                T[] grown = new T[newCapacity];
                for (int i = 0; i < _size; i++) grown[i] = _array[i];
                _array = grown;
                _version++;
            }
            return _array.Length;
        }

        public void TrimExcess() { }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new SimpleEnumerator<T>(ToArray());
        IEnumerator IEnumerable.GetEnumerator() => new SimpleEnumerator<T>(ToArray());

        [Shadows("System.Collections.Generic.Stack`1+Enumerator")]
        public struct Enumerator
        {
            private readonly StackImpl<T> _stack;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(StackImpl<T> stack) { _stack = stack; _version = stack._version; _index = -2; _current = default(T); }

            public bool MoveNext()
            {
                if (_version != _stack._version) ThrowHelper.VersionChanged();
                if (_index == -2)
                {
                    _index = _stack._size - 1;
                }
                else
                {
                    if (_index == -1) return false;
                    --_index;
                }
                if (_index >= 0) { _current = _stack._array[_index]; return true; }
                _current = default(T);
                return false;
            }

            public T Current => _current;
            public void Dispose() { _index = -1; }
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

    // ---------------------------------------------------------------------------------------------
    // LinkedList<T>
    // ---------------------------------------------------------------------------------------------

    [Shadows("System.Collections.Generic.LinkedListNode`1")]
    public sealed class LinkedListNodeImpl<T>
    {
        internal LinkedList<T> list;
        internal LinkedListNode<T> next;
        internal LinkedListNode<T> prev;
        internal T item;

        public LinkedListNodeImpl(T value) { item = value; }

        internal static LinkedListNodeImpl<T> Of(LinkedListNode<T> node) => (LinkedListNodeImpl<T>)(object)node;

        public LinkedList<T> List => list;
        public LinkedListNode<T> Next => next == null || (object)next == (object)LinkedListImpl<T>.Of(list).head ? null : next;
        public LinkedListNode<T> Previous => prev == null || (object)this == (object)LinkedListImpl<T>.Of(list).head ? null : prev;
        public T Value { get => item; set => item = value; }
    }

    [Shadows("System.Collections.Generic.LinkedList`1")]
    public class LinkedListImpl<T> : ICollection<T>, IReadOnlyCollection<T>
    {
        internal LinkedListNode<T> head;   // circular doubly-linked; head.prev is the last node
        internal int count;
        internal int version;

        public LinkedListImpl() { }

        public LinkedListImpl(IEnumerable<T> collection)
        {
            if (collection == null) ThrowHelper.ArgumentNull("collection");
            foreach (T item in collection) AddLast(item);
        }

        internal static LinkedListImpl<T> Of(LinkedList<T> list) => (LinkedListImpl<T>)(object)list;
        private LinkedList<T> Self => (LinkedList<T>)(object)this;
        private static LinkedListNodeImpl<T> N(LinkedListNode<T> node) => LinkedListNodeImpl<T>.Of(node);

        public int Count => count;
        public LinkedListNode<T> First => head;
        public LinkedListNode<T> Last => head == null ? null : N(head).prev;
        bool ICollection<T>.IsReadOnly => false;

        private void ValidateNode(LinkedListNode<T> node)
        {
            if (node == null) ThrowHelper.ArgumentNull("node");
            if ((object)N(node).list != (object)this) ThrowHelper.InvalidOperation("The LinkedList node does not belong to current LinkedList.");
        }

        private void ValidateNewNode(LinkedListNode<T> node)
        {
            if (node == null) ThrowHelper.ArgumentNull("node");
            if (N(node).list != null) ThrowHelper.InvalidOperation("The LinkedList node already belongs to a LinkedList.");
        }

        private void InsertNodeBefore(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            var n = N(newNode);
            n.next = node;
            n.prev = N(node).prev;
            N(N(node).prev).next = newNode;
            N(node).prev = newNode;
            n.list = Self;
            version++;
            count++;
        }

        private void InsertNodeToEmptyList(LinkedListNode<T> newNode)
        {
            var n = N(newNode);
            n.next = newNode;
            n.prev = newNode;
            n.list = Self;
            head = newNode;
            version++;
            count++;
        }

        public LinkedListNode<T> AddAfter(LinkedListNode<T> node, T value)
        {
            ValidateNode(node);
            var result = new LinkedListNode<T>(value);
            InsertNodeBefore(N(node).next, result);
            return result;
        }

        public void AddAfter(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            ValidateNode(node);
            ValidateNewNode(newNode);
            InsertNodeBefore(N(node).next, newNode);
        }

        public LinkedListNode<T> AddBefore(LinkedListNode<T> node, T value)
        {
            ValidateNode(node);
            var result = new LinkedListNode<T>(value);
            InsertNodeBefore(node, result);
            if ((object)node == (object)head) head = result;
            return result;
        }

        public void AddBefore(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            ValidateNode(node);
            ValidateNewNode(newNode);
            InsertNodeBefore(node, newNode);
            if ((object)node == (object)head) head = newNode;
        }

        public LinkedListNode<T> AddFirst(T value)
        {
            var result = new LinkedListNode<T>(value);
            AddFirst(result);
            return result;
        }

        public void AddFirst(LinkedListNode<T> node)
        {
            ValidateNewNode(node);
            if (head == null) InsertNodeToEmptyList(node);
            else { InsertNodeBefore(head, node); head = node; }
        }

        public LinkedListNode<T> AddLast(T value)
        {
            var result = new LinkedListNode<T>(value);
            AddLast(result);
            return result;
        }

        public void AddLast(LinkedListNode<T> node)
        {
            ValidateNewNode(node);
            if (head == null) InsertNodeToEmptyList(node);
            else InsertNodeBefore(head, node);
        }

        void ICollection<T>.Add(T value) => AddLast(value);

        public void Clear()
        {
            var current = head;
            while (current != null)
            {
                var temp = current;
                current = temp.Next;
                var t = N(temp);
                t.list = null; t.next = null; t.prev = null;
            }
            head = null;
            count = 0;
            version++;
        }

        public bool Contains(T value) => Find(value) != null;

        public void CopyTo(T[] array, int index)
        {
            var node = head;
            if (node != null)
            {
                do
                {
                    array[index++] = N(node).item;
                    node = N(node).next;
                } while ((object)node != (object)head);
            }
        }

        public LinkedListNode<T> Find(T value)
        {
            var node = head;
            if (node != null)
            {
                do
                {
                    if (CoreIntrinsics.AreEqual(N(node).item, value)) return node;
                    node = N(node).next;
                } while ((object)node != (object)head);
            }
            return null;
        }

        public LinkedListNode<T> FindLast(T value)
        {
            if (head == null) return null;
            var last = N(head).prev;
            var node = last;
            do
            {
                if (CoreIntrinsics.AreEqual(N(node).item, value)) return node;
                node = N(node).prev;
            } while ((object)node != (object)last);
            return null;
        }

        public bool Remove(T value)
        {
            var node = Find(value);
            if (node != null) { InternalRemoveNode(node); return true; }
            return false;
        }

        public void Remove(LinkedListNode<T> node)
        {
            ValidateNode(node);
            InternalRemoveNode(node);
        }

        public void RemoveFirst()
        {
            if (head == null) ThrowHelper.InvalidOperation("The LinkedList is empty.");
            InternalRemoveNode(head);
        }

        public void RemoveLast()
        {
            if (head == null) ThrowHelper.InvalidOperation("The LinkedList is empty.");
            InternalRemoveNode(N(head).prev);
        }

        private void InternalRemoveNode(LinkedListNode<T> node)
        {
            var n = N(node);
            if ((object)n.next == (object)node)
            {
                head = null;
            }
            else
            {
                N(n.next).prev = n.prev;
                N(n.prev).next = n.next;
                if ((object)head == (object)node) head = n.next;
            }
            n.list = null; n.next = null; n.prev = null;
            count--;
            version++;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            T[] items = new T[count];
            CopyTo(items, 0);
            return new SimpleEnumerator<T>(items);
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        [Shadows("System.Collections.Generic.LinkedList`1+Enumerator")]
        public struct Enumerator
        {
            private readonly LinkedListImpl<T> _list;
            private LinkedListNode<T> _node;
            private readonly int _version;
            private T _current;
            private int _index;

            internal Enumerator(LinkedListImpl<T> list)
            {
                _list = list;
                _version = list.version;
                _node = list.head;
                _current = default(T);
                _index = 0;
            }

            public bool MoveNext()
            {
                if (_version != _list.version) ThrowHelper.VersionChanged();
                if (_node == null) { _index = _list.count + 1; return false; }
                ++_index;
                _current = N(_node).item;
                _node = N(_node).next;
                if ((object)_node == (object)_list.head) _node = null;
                return true;
            }

            public T Current => _current;
            public void Dispose() { }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // PriorityQueue<TElement, TPriority>: .NET's array-backed quaternary min-heap.
    // ---------------------------------------------------------------------------------------------

    [Shadows("System.Collections.Generic.PriorityQueue`2")]
    public class PriorityQueueImpl<TElement, TPriority>
    {
        private TElement[] _elements;
        private TPriority[] _priorities;
        private int _size;
        private readonly IComparer<TPriority> _comparer;   // null = default
        private const int Arity = 4;
        private const int Log2Arity = 2;

        public PriorityQueueImpl() { _elements = new TElement[0]; _priorities = new TPriority[0]; }

        public PriorityQueueImpl(int initialCapacity) : this(initialCapacity, null) { }

        public PriorityQueueImpl(IComparer<TPriority> comparer)
        {
            _elements = new TElement[0];
            _priorities = new TPriority[0];
            if (comparer != null && (object)comparer != (object)Comparer<TPriority>.Default) _comparer = comparer;
        }

        public PriorityQueueImpl(int initialCapacity, IComparer<TPriority> comparer)
        {
            if (initialCapacity < 0) ThrowHelper.ArgumentOutOfRange("initialCapacity", "Non-negative number required.");
            _elements = new TElement[initialCapacity];
            _priorities = new TPriority[initialCapacity];
            if (comparer != null && (object)comparer != (object)Comparer<TPriority>.Default) _comparer = comparer;
        }

        public int Count => _size;
        public IComparer<TPriority> Comparer => _comparer ?? Comparer<TPriority>.Default;

        private int Compare(TPriority a, TPriority b) => _comparer == null ? CoreIntrinsics.Compare(a, b) : _comparer.Compare(a, b);

        public void Enqueue(TElement element, TPriority priority)
        {
            int currentSize = _size;
            if (_elements.Length == currentSize) Grow(currentSize + 1);
            _size = currentSize + 1;
            MoveUp(element, priority, currentSize);
        }

        public TElement Peek()
        {
            if (_size == 0) ThrowHelper.InvalidOperation("Queue empty.");
            return _elements[0];
        }

        public TElement Dequeue()
        {
            if (_size == 0) ThrowHelper.InvalidOperation("Queue empty.");
            TElement element = _elements[0];
            RemoveRootNode();
            return element;
        }

        public TElement EnqueueDequeue(TElement element, TPriority priority)
        {
            if (_size != 0)
            {
                if (Compare(priority, _priorities[0]) > 0)
                {
                    TElement root = _elements[0];
                    MoveDown(element, priority, 0);
                    return root;
                }
            }
            return element;
        }

        public TElement DequeueEnqueue(TElement element, TPriority priority)
        {
            if (_size == 0) ThrowHelper.InvalidOperation("Queue empty.");
            TElement root = _elements[0];
            MoveDown(element, priority, 0);
            return root;
        }

        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (_size != 0)
            {
                element = _elements[0];
                priority = _priorities[0];
                RemoveRootNode();
                return true;
            }
            element = default(TElement);
            priority = default(TPriority);
            return false;
        }

        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (_size != 0)
            {
                element = _elements[0];
                priority = _priorities[0];
                return true;
            }
            element = default(TElement);
            priority = default(TPriority);
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < _size; i++) { _elements[i] = default(TElement); _priorities[i] = default(TPriority); }
            _size = 0;
        }

        public int EnsureCapacity(int capacity)
        {
            if (_elements.Length < capacity) Grow(capacity);
            return _elements.Length;
        }

        private void Grow(int minCapacity)
        {
            int newcapacity = 2 * _elements.Length;
            if (newcapacity < _elements.Length + 4) newcapacity = _elements.Length + 4;
            if (newcapacity < minCapacity) newcapacity = minCapacity;
            TElement[] elements = new TElement[newcapacity];
            TPriority[] priorities = new TPriority[newcapacity];
            for (int i = 0; i < _size; i++) { elements[i] = _elements[i]; priorities[i] = _priorities[i]; }
            _elements = elements;
            _priorities = priorities;
        }

        private void RemoveRootNode()
        {
            int lastNodeIndex = --_size;
            if (lastNodeIndex > 0) MoveDown(_elements[lastNodeIndex], _priorities[lastNodeIndex], 0);
            _elements[lastNodeIndex] = default(TElement);
            _priorities[lastNodeIndex] = default(TPriority);
        }

        private void MoveUp(TElement element, TPriority priority, int nodeIndex)
        {
            while (nodeIndex > 0)
            {
                int parentIndex = (nodeIndex - 1) >> Log2Arity;
                if (Compare(priority, _priorities[parentIndex]) < 0)
                {
                    _elements[nodeIndex] = _elements[parentIndex];
                    _priorities[nodeIndex] = _priorities[parentIndex];
                    nodeIndex = parentIndex;
                }
                else break;
            }
            _elements[nodeIndex] = element;
            _priorities[nodeIndex] = priority;
        }

        private void MoveDown(TElement element, TPriority priority, int nodeIndex)
        {
            int size = _size;
            int i;
            while ((i = (nodeIndex << Log2Arity) + 1) < size)
            {
                int minChildIndex = i;
                int childIndexUpperBound = Math.Min(i + Arity, size);
                while (++i < childIndexUpperBound)
                    if (Compare(_priorities[i], _priorities[minChildIndex]) < 0) minChildIndex = i;
                if (Compare(priority, _priorities[minChildIndex]) <= 0) break;
                _elements[nodeIndex] = _elements[minChildIndex];
                _priorities[nodeIndex] = _priorities[minChildIndex];
                nodeIndex = minChildIndex;
            }
            _elements[nodeIndex] = element;
            _priorities[nodeIndex] = priority;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Sorted collections: sorted arrays + binary search (SortedList's real design; SortedDictionary
    // and SortedSet observe the same order with different complexity).
    // ---------------------------------------------------------------------------------------------

    internal sealed class SortedStore<TKey, TValue>
    {
        internal TKey[] keys = new TKey[0];
        internal TValue[] values = new TValue[0];
        internal int size;
        internal int version;
        internal IComparer<TKey> comparer;   // null = default

        internal int Compare(TKey a, TKey b) => comparer == null ? CoreIntrinsics.Compare(a, b) : comparer.Compare(a, b);

        internal int IndexOfKey(TKey key)
        {
            int lo = 0, hi = size - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                int c = Compare(keys[i], key);
                if (c == 0) return i;
                if (c < 0) lo = i + 1; else hi = i - 1;
            }
            return ~lo;
        }

        internal void Insert(int index, TKey key, TValue value)
        {
            if (size == keys.Length)
            {
                int newCapacity = keys.Length == 0 ? 4 : keys.Length * 2;
                TKey[] k = new TKey[newCapacity];
                TValue[] v = new TValue[newCapacity];
                for (int i = 0; i < size; i++) { k[i] = keys[i]; v[i] = values[i]; }
                keys = k;
                values = v;
            }
            for (int i = size; i > index; i--) { keys[i] = keys[i - 1]; values[i] = values[i - 1]; }
            keys[index] = key;
            values[index] = value;
            size++;
            version++;
        }

        internal void RemoveAt(int index)
        {
            size--;
            for (int i = index; i < size; i++) { keys[i] = keys[i + 1]; values[i] = values[i + 1]; }
            keys[size] = default(TKey);
            values[size] = default(TValue);
            version++;
        }

        internal void Clear()
        {
            for (int i = 0; i < size; i++) { keys[i] = default(TKey); values[i] = default(TValue); }
            size = 0;
            version++;
        }
    }

    [Shadows("System.Collections.Generic.SortedList`2")]
    public class SortedListImpl<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    {
        internal readonly SortedStore<TKey, TValue> _store = new SortedStore<TKey, TValue>();

        public SortedListImpl() { }
        public SortedListImpl(IComparer<TKey> comparer) { if (comparer != null && (object)comparer != (object)Comparer<TKey>.Default) _store.comparer = comparer; }
        public SortedListImpl(int capacity) { }
        public SortedListImpl(IDictionary<TKey, TValue> dictionary)
        {
            foreach (var pair in dictionary) Add(pair.Key, pair.Value);
        }

        public int Count => _store.size;
        public IComparer<TKey> Comparer => _store.comparer ?? Comparer<TKey>.Default;
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public IList<TKey> Keys { get { var a = new TKey[_store.size]; for (int i = 0; i < a.Length; i++) a[i] = _store.keys[i]; return new List<TKey>(a); } }
        public IList<TValue> Values { get { var a = new TValue[_store.size]; for (int i = 0; i < a.Length; i++) a[i] = _store.values[i]; return new List<TValue>(a); } }
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public TValue this[TKey key]
        {
            get
            {
                int i = _store.IndexOfKey(key);
                if (i >= 0) return _store.values[i];
                ThrowHelper.KeyNotFound(key.ToString());
                return default(TValue);
            }
            set
            {
                if (key == null) ThrowHelper.ArgumentNull("key");
                int i = _store.IndexOfKey(key);
                if (i >= 0) { _store.values[i] = value; _store.version++; }
                else _store.Insert(~i, key, value);
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (key == null) ThrowHelper.ArgumentNull("key");
            int i = _store.IndexOfKey(key);
            if (i >= 0) ThrowHelper.DuplicateKey(key.ToString());
            _store.Insert(~i, key, value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        public bool ContainsKey(TKey key) => _store.IndexOfKey(key) >= 0;
        public bool ContainsValue(TValue value) => IndexOfValue(value) >= 0;
        public int IndexOfKey(TKey key) { int i = _store.IndexOfKey(key); return i >= 0 ? i : -1; }

        public int IndexOfValue(TValue value)
        {
            for (int i = 0; i < _store.size; i++) if (CoreIntrinsics.AreEqual(_store.values[i], value)) return i;
            return -1;
        }

        public TKey GetKeyAtIndex(int index) => _store.keys[index];
        public TValue GetValueAtIndex(int index) => _store.values[index];
        public void RemoveAt(int index) => _store.RemoveAt(index);

        public bool Remove(TKey key)
        {
            int i = _store.IndexOfKey(key);
            if (i >= 0) { _store.RemoveAt(i); return true; }
            return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) => ContainsKey(item.Key);

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            for (int i = 0; i < _store.size; i++) array[index + i] = new KeyValuePair<TKey, TValue>(_store.keys[i], _store.values[i]);
        }

        public void Clear() => _store.Clear();

        public bool TryGetValue(TKey key, out TValue value)
        {
            int i = _store.IndexOfKey(key);
            if (i >= 0) { value = _store.values[i]; return true; }
            value = default(TValue);
            return false;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => new SortedEnumerator<TKey, TValue>(_store);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class SortedEnumerator<TKey, TValue> : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly SortedStore<TKey, TValue> _store;
        private readonly int _version;
        private int _index = -1;

        public SortedEnumerator(SortedStore<TKey, TValue> store) { _store = store; _version = store.version; }

        public bool MoveNext()
        {
            if (_version != _store.version) ThrowHelper.VersionChanged();
            return ++_index < _store.size;
        }

        public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(_store.keys[_index], _store.values[_index]);
        object IEnumerator.Current => CoreIntrinsics.Box(Current);
        public void Reset() { _index = -1; }
        public void Dispose() { }
    }

    [Shadows("System.Collections.Generic.SortedDictionary`2")]
    public class SortedDictionaryImpl<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    {
        internal readonly SortedStore<TKey, TValue> _store = new SortedStore<TKey, TValue>();

        public SortedDictionaryImpl() { }
        public SortedDictionaryImpl(IComparer<TKey> comparer) { if (comparer != null && (object)comparer != (object)Comparer<TKey>.Default) _store.comparer = comparer; }
        public SortedDictionaryImpl(IDictionary<TKey, TValue> dictionary)
        {
            foreach (var pair in dictionary) Add(pair.Key, pair.Value);
        }

        public int Count => _store.size;
        public IComparer<TKey> Comparer => _store.comparer ?? Comparer<TKey>.Default;
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public KeyCollection Keys => new KeyCollection(_store);
        public ValueCollection Values => new ValueCollection(_store);
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => new List<TKey>(Keys);
        ICollection<TValue> IDictionary<TKey, TValue>.Values => new List<TValue>(Values);
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public TValue this[TKey key]
        {
            get
            {
                if (key == null) ThrowHelper.ArgumentNull("key");
                int i = _store.IndexOfKey(key);
                if (i >= 0) return _store.values[i];
                ThrowHelper.KeyNotFound(key.ToString());
                return default(TValue);
            }
            set
            {
                if (key == null) ThrowHelper.ArgumentNull("key");
                int i = _store.IndexOfKey(key);
                if (i >= 0) { _store.values[i] = value; _store.version++; }
                else _store.Insert(~i, key, value);
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (key == null) ThrowHelper.ArgumentNull("key");
            int i = _store.IndexOfKey(key);
            if (i >= 0) ThrowHelper.DuplicateKey(key.ToString());
            _store.Insert(~i, key, value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        public bool ContainsKey(TKey key) => _store.IndexOfKey(key) >= 0;

        public bool ContainsValue(TValue value)
        {
            for (int i = 0; i < _store.size; i++) if (CoreIntrinsics.AreEqual(_store.values[i], value)) return true;
            return false;
        }

        public bool Remove(TKey key)
        {
            int i = _store.IndexOfKey(key);
            if (i >= 0) { _store.RemoveAt(i); return true; }
            return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) => ContainsKey(item.Key);

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            for (int i = 0; i < _store.size; i++) array[index + i] = new KeyValuePair<TKey, TValue>(_store.keys[i], _store.values[i]);
        }

        public void Clear() => _store.Clear();

        public bool TryGetValue(TKey key, out TValue value)
        {
            int i = _store.IndexOfKey(key);
            if (i >= 0) { value = _store.values[i]; return true; }
            value = default(TValue);
            return false;
        }

        public Enumerator GetEnumerator() => new Enumerator(_store);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => new SortedEnumerator<TKey, TValue>(_store);
        IEnumerator IEnumerable.GetEnumerator() => new SortedEnumerator<TKey, TValue>(_store);

        [Shadows("System.Collections.Generic.SortedDictionary`2+Enumerator")]
        public struct Enumerator
        {
            private readonly SortedStore<TKey, TValue> _store;
            private readonly int _version;
            private int _index;

            internal Enumerator(SortedStore<TKey, TValue> store) { _store = store; _version = store.version; _index = -1; }

            public bool MoveNext()
            {
                if (_version != _store.version) ThrowHelper.VersionChanged();
                return ++_index < _store.size;
            }

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(_store.keys[_index], _store.values[_index]);
            public void Dispose() { }
        }

        [Shadows("System.Collections.Generic.SortedDictionary`2+KeyCollection")]
        public sealed class KeyCollection : IReadOnlyCollection<TKey>
        {
            private readonly SortedStore<TKey, TValue> _store;
            internal KeyCollection(SortedStore<TKey, TValue> store) { _store = store; }
            public int Count => _store.size;

            private TKey[] Snapshot()
            {
                var a = new TKey[_store.size];
                for (int i = 0; i < a.Length; i++) a[i] = _store.keys[i];
                return a;
            }

            public Enumerator GetEnumerator() => new Enumerator(_store);
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => new SimpleEnumerator<TKey>(Snapshot());
            IEnumerator IEnumerable.GetEnumerator() => new SimpleEnumerator<TKey>(Snapshot());

            [Shadows("System.Collections.Generic.SortedDictionary`2+KeyCollection+Enumerator")]
            public struct Enumerator
            {
                private readonly SortedStore<TKey, TValue> _store;
                private int _index;
                internal Enumerator(SortedStore<TKey, TValue> store) { _store = store; _index = -1; }
                public bool MoveNext() => ++_index < _store.size;
                public TKey Current => _store.keys[_index];
                public void Dispose() { }
            }
        }

        [Shadows("System.Collections.Generic.SortedDictionary`2+ValueCollection")]
        public sealed class ValueCollection : IReadOnlyCollection<TValue>
        {
            private readonly SortedStore<TKey, TValue> _store;
            internal ValueCollection(SortedStore<TKey, TValue> store) { _store = store; }
            public int Count => _store.size;

            private TValue[] Snapshot()
            {
                var a = new TValue[_store.size];
                for (int i = 0; i < a.Length; i++) a[i] = _store.values[i];
                return a;
            }

            public Enumerator GetEnumerator() => new Enumerator(_store);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => new SimpleEnumerator<TValue>(Snapshot());
            IEnumerator IEnumerable.GetEnumerator() => new SimpleEnumerator<TValue>(Snapshot());

            [Shadows("System.Collections.Generic.SortedDictionary`2+ValueCollection+Enumerator")]
            public struct Enumerator
            {
                private readonly SortedStore<TKey, TValue> _store;
                private int _index;
                internal Enumerator(SortedStore<TKey, TValue> store) { _store = store; _index = -1; }
                public bool MoveNext() => ++_index < _store.size;
                public TValue Current => _store.values[_index];
                public void Dispose() { }
            }
        }
    }

    [Shadows("System.Collections.Generic.SortedSet`1")]
    public class SortedSetImpl<T> : ISet<T>, IReadOnlySet<T>
    {
        internal readonly SortedStore<T, bool> _store = new SortedStore<T, bool>();

        public SortedSetImpl() { }
        public SortedSetImpl(IComparer<T> comparer) { if (comparer != null && (object)comparer != (object)Comparer<T>.Default) _store.comparer = comparer; }
        public SortedSetImpl(IEnumerable<T> collection) { UnionWith(collection); }
        public SortedSetImpl(IEnumerable<T> collection, IComparer<T> comparer) : this(comparer) { UnionWith(collection); }

        public int Count => _store.size;
        public IComparer<T> Comparer => _store.comparer ?? Comparer<T>.Default;
        bool ICollection<T>.IsReadOnly => false;
        public T Min => _store.size == 0 ? default(T) : _store.keys[0];
        public T Max => _store.size == 0 ? default(T) : _store.keys[_store.size - 1];

        public bool Add(T item)
        {
            int i = _store.IndexOfKey(item);
            if (i >= 0) return false;
            _store.Insert(~i, item, true);
            return true;
        }

        void ICollection<T>.Add(T item) => Add(item);
        public bool Contains(T item) => _store.IndexOfKey(item) >= 0;

        public bool Remove(T item)
        {
            int i = _store.IndexOfKey(item);
            if (i < 0) return false;
            _store.RemoveAt(i);
            return true;
        }

        public void Clear() => _store.Clear();

        public int RemoveWhere(Predicate<T> match)
        {
            int removed = 0;
            for (int i = _store.size - 1; i >= 0; i--)
                if (match(_store.keys[i])) { _store.RemoveAt(i); removed++; }
            return removed;
        }

        public void CopyTo(T[] array) => CopyTo(array, 0);
        public void CopyTo(T[] array, int index) { for (int i = 0; i < _store.size; i++) array[index + i] = _store.keys[i]; }

        private T[] Snapshot()
        {
            var a = new T[_store.size];
            for (int i = 0; i < a.Length; i++) a[i] = _store.keys[i];
            return a;
        }

        public IEnumerable<T> Reverse()
        {
            var a = Snapshot();
            var r = new T[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[a.Length - 1 - i];
            return r;
        }

        public void UnionWith(IEnumerable<T> other) { foreach (T item in other) Add(item); }

        public void IntersectWith(IEnumerable<T> other)
        {
            var keep = new SortedSet<T>(other, Comparer);
            foreach (T item in Snapshot()) if (!keep.Contains(item)) Remove(item);
        }

        public void ExceptWith(IEnumerable<T> other) { foreach (T item in other) Remove(item); }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            foreach (T item in new SortedSet<T>(other, Comparer)) if (!Remove(item)) Add(item);
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            var set = new SortedSet<T>(other, Comparer);
            foreach (T item in Snapshot()) if (!set.Contains(item)) return false;
            return true;
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            var set = new SortedSet<T>(other, Comparer);
            return Count < set.Count && IsSubsetOf(set);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            foreach (T item in other) if (!Contains(item)) return false;
            return true;
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            var set = new SortedSet<T>(other, Comparer);
            return set.Count < Count && IsSupersetOf(set);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            foreach (T item in other) if (Contains(item)) return true;
            return false;
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            var set = new SortedSet<T>(other, Comparer);
            return set.Count == Count && IsSupersetOf(set);
        }

        public Enumerator GetEnumerator() => new Enumerator(_store);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new SimpleEnumerator<T>(Snapshot());
        IEnumerator IEnumerable.GetEnumerator() => new SimpleEnumerator<T>(Snapshot());

        [Shadows("System.Collections.Generic.SortedSet`1+Enumerator")]
        public struct Enumerator
        {
            private readonly SortedStore<T, bool> _store;
            private readonly int _version;
            private int _index;

            internal Enumerator(SortedStore<T, bool> store) { _store = store; _version = store.version; _index = -1; }

            public bool MoveNext()
            {
                if (_version != _store.version) ThrowHelper.VersionChanged();
                return ++_index < _store.size;
            }

            public T Current => _store.keys[_index];
            public void Dispose() { }
        }
    }
}
