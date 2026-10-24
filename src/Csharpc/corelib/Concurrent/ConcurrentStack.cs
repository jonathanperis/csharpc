// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: System.Collections.Concurrent.ConcurrentStack<T> from dotnet/runtime release/11.0
// (System.Collections.Concurrent/src/System/Collections/Concurrent/ConcurrentStack.cs): the same
// lock-free linked list; the contention back-off spins with Thread.Yield and no event tracing.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Concurrent.ConcurrentStack`1")]
    public class ConcurrentStackImpl<T> : System.Collections.Concurrent.IProducerConsumerCollection<T>, IReadOnlyCollection<T>
    {
        private sealed class Node
        {
            internal readonly T _value;
            internal Node _next;

            internal Node(T value)
            {
                _value = value;
                _next = null;
            }
        }

        private volatile Node _head;

        public ConcurrentStackImpl() { }

        public ConcurrentStackImpl(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException("collection");
            Node lastNode = null;
            foreach (T element in collection)
            {
                Node newNode = new Node(element);
                newNode._next = lastNode;
                lastNode = newNode;
            }
            _head = lastNode;
        }

        public bool IsEmpty => _head == null;

        public int Count
        {
            get
            {
                int count = 0;
                for (Node curr = _head; curr != null; curr = curr._next) count++;
                return count;
            }
        }

        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => throw new NotSupportedException("The SyncRoot property may not be used for the synchronization of concurrent collections.");

        public void Clear() => _head = null;

        void ICollection.CopyTo(System.Array array, int index)
        {
            if (array == null) throw new ArgumentNullException("array");
            T[] items = ToList(_head).ToArray();
            System.Array.Copy(items, 0, array, index, items.Length);
        }

        public void CopyTo(T[] array, int index)
        {
            if (array == null) throw new ArgumentNullException("array");
            ToList(_head).CopyTo(array, index);
        }

        public void Push(T item)
        {
            Node newNode = new Node(item);
            newNode._next = _head;
            if (Interlocked.CompareExchange(ref _head, newNode, newNode._next) == newNode._next) return;
            PushCore(newNode, newNode);
        }

        public void PushRange(T[] items)
        {
            if (items == null) throw new ArgumentNullException("items");
            PushRange(items, 0, items.Length);
        }

        public void PushRange(T[] items, int startIndex, int count)
        {
            ValidatePushPopRangeInput(items, startIndex, count);
            if (count == 0) return;
            Node head, tail;
            head = tail = new Node(items[startIndex]);
            for (int i = startIndex + 1; i < startIndex + count; i++)
            {
                Node node = new Node(items[i]);
                node._next = head;
                head = node;
            }
            tail._next = _head;
            if (Interlocked.CompareExchange(ref _head, head, tail._next) == tail._next) return;
            PushCore(head, tail);
        }

        private void PushCore(Node head, Node tail)
        {
            do
            {
                Thread.Yield();
                tail._next = _head;
            }
            while (Interlocked.CompareExchange(ref _head, head, tail._next) != tail._next);
        }

        private static void ValidatePushPopRangeInput(T[] items, int startIndex, int count)
        {
            if (items == null) throw new ArgumentNullException("items");
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            int length = items.Length;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, length);
            ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
            if (length - count < startIndex)
                throw new ArgumentException("The sum of the startIndex and count arguments must be less than or equal to the collection's Count.");
        }

        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Push(item);
            return true;
        }

        public bool TryPeek(out T result)
        {
            Node head = _head;
            if (head == null)
            {
                result = default(T);
                return false;
            }
            result = head._value;
            return true;
        }

        public bool TryPop(out T result)
        {
            Node head = _head;
            if (head == null)
            {
                result = default(T);
                return false;
            }
            if (Interlocked.CompareExchange(ref _head, head._next, head) == head)
            {
                result = head._value;
                return true;
            }
            if (TryPopCore(1, out Node popped) == 1)
            {
                result = popped._value;
                return true;
            }
            result = default(T);
            return false;
        }

        public int TryPopRange(T[] items)
        {
            if (items == null) throw new ArgumentNullException("items");
            return TryPopRange(items, 0, items.Length);
        }

        public int TryPopRange(T[] items, int startIndex, int count)
        {
            ValidatePushPopRangeInput(items, startIndex, count);
            if (count == 0) return 0;
            int nodesCount = TryPopCore(count, out Node poppedHead);
            if (nodesCount > 0)
            {
                Node current = poppedHead;
                for (int i = startIndex; i < startIndex + nodesCount; i++)
                {
                    items[i] = current._value;
                    current = current._next;
                }
            }
            return nodesCount;
        }

        private int TryPopCore(int count, out Node poppedHead)
        {
            while (true)
            {
                Node head = _head;
                if (head == null)
                {
                    poppedHead = null;
                    return 0;
                }
                Node next = head;
                int nodesCount = 1;
                for (; nodesCount < count && next._next != null; nodesCount++) next = next._next;
                if (Interlocked.CompareExchange(ref _head, next._next, head) == head)
                {
                    poppedHead = head;
                    return nodesCount;
                }
                Thread.Yield();
            }
        }

        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryTake(out T item) => TryPop(out item);

        public T[] ToArray()
        {
            Node curr = _head;
            return curr == null ? System.Array.Empty<T>() : ToList(curr).ToArray();
        }

        private static List<T> ToList(Node curr)
        {
            List<T> list = new List<T>();
            while (curr != null)
            {
                list.Add(curr._value);
                curr = curr._next;
            }
            return list;
        }

        /// <summary>A moment-in-time snapshot (the nodes are immutable), newest first.</summary>
        public IEnumerator<T> GetEnumerator() => Enumerate(_head);

        private static IEnumerator<T> Enumerate(Node head)
        {
            for (Node current = head; current != null; current = current._next) yield return current._value;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
