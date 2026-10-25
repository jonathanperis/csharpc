// System.Collections.Concurrent.ConcurrentQueue<T>: .NET's lock-free queue (ConcurrentQueue.cs and
// ConcurrentQueueSegment.cs from dotnet/runtime, MIT license), ported: segments of bounded MPMC ring
// buffers linked under a cross-segment lock, snapshot enumeration by preserving segments. Mechanical
// changes: ThrowHelper calls as literal exceptions with .NET's messages, no debugger attributes or
// asserts, the padded head/tail as sequential padding fields. Kestrel's I/O queues and memory pool
// enqueue and dequeue on every request; the previous one-lock queue made them a contention point.
#nullable disable
#pragma warning disable CS0169, CS0414 // padding fields
using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.Concurrent.ConcurrentQueue`1")]
    public class ConcurrentQueueImpl<T> : System.Collections.Concurrent.IProducerConsumerCollection<T>, IReadOnlyCollection<T>
    {
        // This implementation provides an unbounded, multi-producer multi-consumer queue
        // that supports the standard Enqueue/TryDequeue operations, as well as support for
        // snapshot enumeration (GetEnumerator, ToArray, CopyTo), peeking, and Count/IsEmpty.
        // It is composed of a linked list of bounded ring buffers, each of which has a head
        // and a tail index, isolated from each other to minimize false sharing.  As long as
        // the number of elements in the queue remains less than the size of the current
        // buffer (Segment), no additional allocations are required for enqueued items.  When
        // the number of items exceeds the size of the current segment, the current segment is
        // "frozen" to prevent further enqueues, and a new segment is linked from it and set
        // as the new tail segment for subsequent enqueues.  As old segments are consumed by
        // dequeues, the head reference is updated to point to the segment that dequeuers should
        // try next.  To support snapshot enumeration, segments also support the notion of
        // preserving for observation, whereby they avoid overwriting state as part of dequeues.
        // Any operation that requires a snapshot results in all current segments being
        // both frozen for enqueues and preserved for observation: any new enqueues will go
        // to new segments, and dequeuers will consume from the existing segments but without
        // overwriting the existing data.

        private const int InitialSegmentLength = 32;
        private const int MaxSegmentLength = 1024 * 1024;

        private readonly object _crossSegmentLock;
        private volatile ConcurrentQueueSegmentImpl<T> _tail;
        private volatile ConcurrentQueueSegmentImpl<T> _head; // SOS's ThreadPool command depends on this name

        public ConcurrentQueueImpl()
        {
            _crossSegmentLock = new object();
            _tail = _head = new ConcurrentQueueSegmentImpl<T>(InitialSegmentLength);
        }

        public ConcurrentQueueImpl(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException("collection");
            }

            _crossSegmentLock = new object();

            // Determine the initial segment size.  We'll use the default,
            // unless the collection is known to be larger than that, in which
            // case we round its length up to a power of 2, as all segments must
            // be a power of 2 in length.
            int length = InitialSegmentLength;
            if (collection is ICollection<T> c)
            {
                int count = c.Count;
                if (count > length)
                {
                    length = (int)Math.Min(RoundUpToPowerOf2((uint)count), MaxSegmentLength);
                }
            }

            // Initialize the segment and add all of the data to it.
            _tail = _head = new ConcurrentQueueSegmentImpl<T>(length);
            foreach (T item in collection)
            {
                Enqueue(item);
            }
        }

        // BitOperations.RoundUpToPowerOf2 (count > 32 here).
        private static uint RoundUpToPowerOf2(uint value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        void ICollection.CopyTo(System.Array array, int index)
        {
            // Special-case when the Array is actually a T[], taking a faster path
            if (array is T[] szArray)
            {
                CopyTo(szArray, index);
                return;
            }

            // Validate arguments.
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            // Otherwise, fall back to the slower path that first copies the contents
            // to an array, and then uses that array's non-generic CopyTo to do the copy.
            ToArray().CopyTo(array, index);
        }

        bool ICollection.IsSynchronized => false; // always false, as true implies synchronization via SyncRoot

        object ICollection.SyncRoot => throw new NotSupportedException("The SyncRoot property may not be used for the synchronization of concurrent collections.");

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Enqueue(item);
            return true;
        }

        bool System.Collections.Concurrent.IProducerConsumerCollection<T>.TryTake(out T item) => TryDequeue(out item);

        public bool IsEmpty =>
            // IsEmpty == !TryPeek. We use a "resultUsed:false" peek in order to avoid marking
            // segments as preserved for observation, making IsEmpty a cheaper way than either
            // TryPeek(out T) or Count == 0 to check whether any elements are in the queue.
            !TryPeek(out _, resultUsed: false);

        public T[] ToArray()
        {
            // Snap the current contents for enumeration.
            SnapForObservation(out ConcurrentQueueSegmentImpl<T> head, out int headHead, out ConcurrentQueueSegmentImpl<T> tail, out int tailTail);

            // Count the number of items in that snapped set, and use it to allocate an
            // array of the right size.
            long count = GetCount(head, headHead, tail, tailTail);
            T[] arr = new T[count];

            // Now enumerate the contents, copying each element into the array.
            using (IEnumerator<T> e = Enumerate(head, headHead, tail, tailTail))
            {
                int i = 0;
                while (e.MoveNext())
                {
                    arr[i++] = e.Current;
                }
            }

            // And return it.
            return arr;
        }

        public int Count
        {
            get
            {
                SpinWait spinner = default;
                while (true)
                {
                    // Capture the head and tail, as well as the head's head and tail.
                    ConcurrentQueueSegmentImpl<T> head = _head;
                    ConcurrentQueueSegmentImpl<T> tail = _tail;
                    int headHead = Volatile.Read(ref head._headAndTail.Head);
                    int headTail = Volatile.Read(ref head._headAndTail.Tail);

                    if (head == tail)
                    {
                        // There was a single segment in the queue.  If the captured segments still
                        // match, then we can trust the values to compute the segment's count. (It's
                        // theoretically possible the values could have looped around and still exactly match,
                        // but that would required at least ~4 billion elements to have been enqueued and
                        // dequeued between the reads.)
                        if (head == _head &&
                            tail == _tail &&
                            headHead == Volatile.Read(ref head._headAndTail.Head) &&
                            headTail == Volatile.Read(ref head._headAndTail.Tail))
                        {
                            return GetCount(head, headHead, headTail);
                        }
                    }
                    else if (head._nextSegment == tail)
                    {
                        // There were two segments in the queue.  Get the positions from the tail, and as above,
                        // if the captured values match the previous reads, return the sum of the counts from both segments.
                        int tailHead = Volatile.Read(ref tail._headAndTail.Head);
                        int tailTail = Volatile.Read(ref tail._headAndTail.Tail);
                        if (head == _head &&
                            tail == _tail &&
                            headHead == Volatile.Read(ref head._headAndTail.Head) &&
                            headTail == Volatile.Read(ref head._headAndTail.Tail) &&
                            tailHead == Volatile.Read(ref tail._headAndTail.Head) &&
                            tailTail == Volatile.Read(ref tail._headAndTail.Tail))
                        {
                            return GetCount(head, headHead, headTail) + GetCount(tail, tailHead, tailTail);
                        }
                    }
                    else
                    {
                        // There were more than two segments in the queue.  Fall back to taking the cross-segment lock,
                        // which will ensure that the head and tail segments we read are stable (since the lock is needed to change them);
                        // for the two-segment case above, we can simply rely on subsequent comparisons, but for the two+ case, we need
                        // to be able to trust the internal segments between the head and tail.
                        lock (_crossSegmentLock)
                        {
                            // Now that we hold the lock, re-read the previously captured head and tail segments and head positions.
                            // If either has changed, start over.
                            if (head == _head && tail == _tail)
                            {
                                // Get the positions from the tail, and as above, if the captured values match the previous reads,
                                // we can use the values to compute the count of the head and tail segments.
                                int tailHead = Volatile.Read(ref tail._headAndTail.Head);
                                int tailTail = Volatile.Read(ref tail._headAndTail.Tail);
                                if (headHead == Volatile.Read(ref head._headAndTail.Head) &&
                                    headTail == Volatile.Read(ref head._headAndTail.Tail) &&
                                    tailHead == Volatile.Read(ref tail._headAndTail.Head) &&
                                    tailTail == Volatile.Read(ref tail._headAndTail.Tail))
                                {
                                    // We got stable values for the head and tail segments, so we can just compute the sizes
                                    // based on those and add them. Note that this and the below additions to count may overflow: previous
                                    // implementations allowed that, so we don't check, either, and it is theoretically possible for the
                                    // queue to store more than int.MaxValue items.
                                    int count = GetCount(head, headHead, headTail) + GetCount(tail, tailHead, tailTail);

                                    // Now add the counts for each internal segment. Since there were segments before these,
                                    // for counting purposes we consider them to start at the 0th element, and since there is at
                                    // least one segment after each, each was frozen, so we can count until each's frozen tail.
                                    // With the cross-segment lock held, we're guaranteed that all of these internal segments are
                                    // consistent, as the head and tail segment can't be changed while we're holding the lock, and
                                    // dequeueing and enqueueing can only be done from the head and tail segments, which these aren't.
                                    for (ConcurrentQueueSegmentImpl<T> s = head._nextSegment; s != tail; s = s._nextSegment)
                                    {
                                        count += s._headAndTail.Tail - s.FreezeOffset;
                                    }

                                    return count;
                                }
                            }
                        }
                    }

                    // We raced with enqueues/dequeues and captured an inconsistent picture of the queue.
                    // Spin and try again.
                    spinner.SpinOnce();
                }
            }
        }

        private static int GetCount(ConcurrentQueueSegmentImpl<T> s, int head, int tail)
        {
            if (head != tail && head != tail - s.FreezeOffset)
            {
                head &= s._slotsMask;
                tail &= s._slotsMask;
                return head < tail ? tail - head : s._slots.Length - head + tail;
            }
            return 0;
        }

        private static long GetCount(ConcurrentQueueSegmentImpl<T> head, int headHead, ConcurrentQueueSegmentImpl<T> tail, int tailTail)
        {
            // All of the segments should have been both frozen for enqueues and preserved for observation.
            // Validate that here for head and tail; we'll validate it for intermediate segments later.

            long count = 0;

            // Head segment.  We've already marked it as frozen for enqueues, so its tail position is fixed,
            // and we've already marked it as preserved for observation (before we grabbed the head), so we
            // can safely enumerate from its head to its tail and access its elements.
            int headTail = (head == tail ? tailTail : Volatile.Read(ref head._headAndTail.Tail)) - head.FreezeOffset;
            if (headHead < headTail)
            {
                // Mask the head and tail for the head segment
                headHead &= head._slotsMask;
                headTail &= head._slotsMask;

                // Increase the count by either the one or two regions, based on whether tail
                // has wrapped to be less than head.
                count += headHead < headTail ?
                    headTail - headHead :
                    head._slots.Length - headHead + headTail;
            }

            // We've enumerated the head.  If the tail is different from the head, we need to
            // enumerate the remaining segments.
            if (head != tail)
            {
                // Count the contents of each segment between head and tail, not including head and tail.
                // Since there were segments before these, for our purposes we consider them to start at
                // the 0th element, and since there is at least one segment after each, each was frozen
                // by the time we snapped it, so we can iterate until each's frozen tail.
                for (ConcurrentQueueSegmentImpl<T> s = head._nextSegment; s != tail; s = s._nextSegment)
                {
                    count += s._headAndTail.Tail - s.FreezeOffset;
                }

                // Finally, enumerate the tail.  As with the intermediate segments, there were segments
                // before this in the snapped region, so we can start counting from the beginning. Unlike
                // the intermediate segments, we can't just go until the Tail, as that could still be changing;
                // instead we need to go until the tail we snapped for observation.
                count += tailTail - tail.FreezeOffset;
            }

            // Return the computed count.
            return count;
        }

        public void CopyTo(T[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException("index");
            }

            // Snap for enumeration
            SnapForObservation(out ConcurrentQueueSegmentImpl<T> head, out int headHead, out ConcurrentQueueSegmentImpl<T> tail, out int tailTail);

            // Get the number of items to be enumerated
            long count = GetCount(head, headHead, tail, tailTail);
            if (index > array.Length - count)
            {
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            }

            // Copy the items to the target array
            int i = index;
            using (IEnumerator<T> e = Enumerate(head, headHead, tail, tailTail))
            {
                while (e.MoveNext())
                {
                    array[i++] = e.Current;
                }
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            SnapForObservation(out ConcurrentQueueSegmentImpl<T> head, out int headHead, out ConcurrentQueueSegmentImpl<T> tail, out int tailTail);
            return Enumerate(head, headHead, tail, tailTail);
        }

        private void SnapForObservation(out ConcurrentQueueSegmentImpl<T> head, out int headHead, out ConcurrentQueueSegmentImpl<T> tail, out int tailTail)
        {
            lock (_crossSegmentLock) // _head and _tail may only change while the lock is held.
            {
                // Snap the head and tail
                head = _head;
                tail = _tail;

                // Mark them and all segments in between as preserving, and ensure no additional items
                // can be added to the tail.
                for (ConcurrentQueueSegmentImpl<T> s = head; ; s = s._nextSegment)
                {
                    s._preservedForObservation = true;
                    if (s == tail) break;
                }
                tail.EnsureFrozenForEnqueues(); // we want to prevent the tailTail from moving

                // At this point, any dequeues from any segment won't overwrite the value, and
                // none of the existing segments can have new items enqueued.

                headHead = Volatile.Read(ref head._headAndTail.Head);
                tailTail = Volatile.Read(ref tail._headAndTail.Tail);
            }
        }

        private static T GetItemWhenAvailable(ConcurrentQueueSegmentImpl<T> segment, int i)
        {

            // Get the expected value for the sequence number
            int expectedSequenceNumberAndMask = (i + 1) & segment._slotsMask;

            // If the expected sequence number is not yet written, we're still waiting for
            // an enqueuer to finish storing it.  Spin until it's there.
            SpinWait spinner = default;
            // Must read SequenceNumber before reading Item, thus Volatile.Read
            while ((Volatile.Read(ref segment._slots[i].SequenceNumber) & segment._slotsMask) != expectedSequenceNumberAndMask)
            {
                spinner.SpinOnce();
            }

            // Return the value from the slot.
            return segment._slots[i].Item;
        }

        private static IEnumerator<T> Enumerate(ConcurrentQueueSegmentImpl<T> head, int headHead, ConcurrentQueueSegmentImpl<T> tail, int tailTail)
        {

            // Head segment.  We've already marked it as not accepting any more enqueues,
            // so its tail position is fixed, and we've already marked it as preserved for
            // enumeration (before we grabbed its head), so we can safely enumerate from
            // its head to its tail.
            int headTail = (head == tail ? tailTail : Volatile.Read(ref head._headAndTail.Tail)) - head.FreezeOffset;
            if (headHead < headTail)
            {
                headHead &= head._slotsMask;
                headTail &= head._slotsMask;

                if (headHead < headTail)
                {
                    for (int i = headHead; i < headTail; i++) yield return GetItemWhenAvailable(head, i);
                }
                else
                {
                    for (int i = headHead; i < head._slots.Length; i++) yield return GetItemWhenAvailable(head, i);
                    for (int i = 0; i < headTail; i++) yield return GetItemWhenAvailable(head, i);
                }
            }

            // We've enumerated the head.  If the tail is the same, we're done.
            if (head != tail)
            {
                // Each segment between head and tail, not including head and tail.  Since there were
                // segments before these, for our purposes we consider it to start at the 0th element.
                for (ConcurrentQueueSegmentImpl<T> s = head._nextSegment; s != tail; s = s._nextSegment)
                {

                    int sTail = s._headAndTail.Tail - s.FreezeOffset;
                    for (int i = 0; i < sTail; i++)
                    {
                        yield return GetItemWhenAvailable(s, i);
                    }
                }

                // Enumerate the tail.  Since there were segments before this, we can just start at
                // its beginning, and iterate until the tail we already grabbed.
                tailTail -= tail.FreezeOffset;
                for (int i = 0; i < tailTail; i++)
                {
                    yield return GetItemWhenAvailable(tail, i);
                }
            }
        }

        public void Enqueue(T item)
        {
            // Try to enqueue to the current tail.
            if (!_tail.TryEnqueue(item))
            {
                // If we're unable to, we need to take a slow path that will
                // try to add a new tail segment.
                EnqueueSlow(item);
            }
        }

        private void EnqueueSlow(T item)
        {
            while (true)
            {
                ConcurrentQueueSegmentImpl<T> tail = _tail;

                // Try to append to the existing tail.
                if (tail.TryEnqueue(item))
                {
                    return;
                }

                // If we were unsuccessful, take the lock so that we can compare and manipulate
                // the tail.  Assuming another enqueuer hasn't already added a new segment,
                // do so, then loop around to try enqueueing again.
                lock (_crossSegmentLock)
                {
                    if (tail == _tail)
                    {
                        // Make sure no one else can enqueue to this segment.
                        tail.EnsureFrozenForEnqueues();

                        // We determine the new segment's length based on the old length.
                        // In general, we double the size of the segment, to make it less likely
                        // that we'll need to grow again.  However, if the tail segment is marked
                        // as preserved for observation, something caused us to avoid reusing this
                        // segment, and if that happens a lot and we grow, we'll end up allocating
                        // lots of wasted space.  As such, in such situations we reset back to the
                        // initial segment length; if these observations are happening frequently,
                        // this will help to avoid wasted memory, and if they're not, we'll
                        // relatively quickly grow again to a larger size.
                        int nextSize = tail._preservedForObservation ? InitialSegmentLength : Math.Min(tail.Capacity * 2, MaxSegmentLength);
                        var newTail = new ConcurrentQueueSegmentImpl<T>(nextSize);

                        // Hook up the new tail.
                        tail._nextSegment = newTail;
                        _tail = newTail;
                    }
                }
            }
        }

        public bool TryDequeue(out T result)
        {
            // Get the current head
            ConcurrentQueueSegmentImpl<T> head = _head;

            // Try to take.  If we're successful, we're done.
            if (head.TryDequeue(out result))
            {
                return true;
            }

            // Check to see whether this segment is the last. If it is, we can consider
            // this to be a moment-in-time empty condition (even though between the TryDequeue
            // check and this check, another item could have arrived).
            if (head._nextSegment == null)
            {
                result = default;
                return false;
            }

            return TryDequeueSlow(out result); // slow path that needs to fix up segments
        }

        private bool TryDequeueSlow(out T item)
        {
            while (true)
            {
                // Get the current head
                ConcurrentQueueSegmentImpl<T> head = _head;

                // Try to take.  If we're successful, we're done.
                if (head.TryDequeue(out item))
                {
                    return true;
                }

                // Check to see whether this segment is the last. If it is, we can consider
                // this to be a moment-in-time empty condition (even though between the TryDequeue
                // check and this check, another item could have arrived).
                if (head._nextSegment == null)
                {
                    item = default;
                    return false;
                }

                // At this point we know that head.Next != null, which means
                // this segment has been frozen for additional enqueues. But between
                // the time that we ran TryDequeue and checked for a next segment,
                // another item could have been added.  Try to dequeue one more time
                // to confirm that the segment is indeed empty.
                if (head.TryDequeue(out item))
                {
                    return true;
                }

                // This segment is frozen (nothing more can be added) and empty (nothing is in it).
                // Update head to point to the next segment in the list, assuming no one's beat us to it.
                lock (_crossSegmentLock)
                {
                    if (head == _head)
                    {
                        _head = head._nextSegment;
                    }
                }
            }
        }

        public bool TryPeek(out T result) => TryPeek(out result, resultUsed: true);

        private bool TryPeek(out T result, bool resultUsed)
        {
            // Starting with the head segment, look through all of the segments
            // for the first one we can find that's not empty.
            ConcurrentQueueSegmentImpl<T> s = _head;
            while (true)
            {
                // Grab the next segment from this one, before we peek.
                // This is to be able to see whether the value has changed
                // during the peek operation.
                ConcurrentQueueSegmentImpl<T> next = Volatile.Read(ref s._nextSegment);

                // Peek at the segment.  If we find an element, we're done.
                if (s.TryPeek(out result, resultUsed))
                {
                    return true;
                }

                // The current segment was empty at the moment we checked.

                if (next != null)
                {
                    // If prior to the peek there was already a next segment, then
                    // during the peek no additional items could have been enqueued
                    // to it and we can just move on to check the next segment.
                    s = next;
                }
                else if (Volatile.Read(ref s._nextSegment) == null)
                {
                    // The next segment is null.  Nothing more to peek at.
                    break;
                }

                // The next segment was null before we peeked but non-null after.
                // That means either when we peeked the first segment had
                // already been frozen but the new segment not yet added,
                // or that the first segment was empty and between the time
                // that we peeked and then checked _nextSegment, so many items
                // were enqueued that we filled the first segment and went
                // into the next.  Since we need to peek in order, we simply
                // loop around again to peek on the same segment.  The next
                // time around on this segment we'll then either successfully
                // peek or we'll find that next was non-null before peeking,
                // and we'll traverse to that segment.
            }

            result = default;
            return false;
        }

        public void Clear()
        {
            lock (_crossSegmentLock)
            {
                // Simply substitute a new segment for the existing head/tail,
                // as is done in the constructor.  Operations currently in flight
                // may still read from or write to an existing segment that's
                // getting dropped, meaning that in flight operations may not be
                // linear with regards to this clear operation.  To help mitigate
                // in-flight operations enqueuing onto the tail that's about to
                // be dropped, we first freeze it; that'll force enqueuers to take
                // this lock to synchronize and see the new tail.
                _tail.EnsureFrozenForEnqueues();
                _tail = _head = new ConcurrentQueueSegmentImpl<T>(InitialSegmentLength);
            }
        }
    }

    internal sealed class ConcurrentQueueSegmentImpl<T>
    {
        // Segment design is inspired by the algorithm outlined at:
        // http://www.1024cores.net/home/lock-free-algorithms/queues/bounded-mpmc-queue

        internal readonly Slot[] _slots; // SOS's ThreadPool command depends on this name
        internal readonly int _slotsMask;
        internal PaddedHeadAndTail _headAndTail; // mutable struct: do not make this readonly

        internal bool _preservedForObservation;
        internal bool _frozenForEnqueues;
        internal ConcurrentQueueSegmentImpl<T> _nextSegment; // SOS's ThreadPool command depends on this name

        internal ConcurrentQueueSegmentImpl(int boundedLength)
        {
            // Validate the length

            // Initialize the slots and the mask.  The mask is used as a way of quickly doing "% _slots.Length",
            // instead letting us do "& _slotsMask".
            _slots = new Slot[boundedLength];
            _slotsMask = boundedLength - 1;

            // Initialize the sequence number for each slot.  The sequence number provides a ticket that
            // allows dequeuers to know whether they can dequeue and enqueuers to know whether they can
            // enqueue.  An enqueuer at position N can enqueue when the sequence number is N, and a dequeuer
            // for position N can dequeue when the sequence number is N + 1.  When an enqueuer is done writing
            // at position N, it sets the sequence number to N + 1 so that a dequeuer will be able to dequeue,
            // and when a dequeuer is done dequeueing at position N, it sets the sequence number to N + _slots.Length,
            // so that when an enqueuer loops around the slots, it'll find that the sequence number at
            // position N is N.  This also means that when an enqueuer finds that at position N the sequence
            // number is < N, there is still a value in that slot, i.e. the segment is full, and when a
            // dequeuer finds that the value in a slot is < N + 1, there is nothing currently available to
            // dequeue. (It is possible for multiple enqueuers to enqueue concurrently, writing into
            // subsequent slots, and to have the first enqueuer take longer, so that the slots for 1, 2, 3, etc.
            // may have values, but the 0th slot may still be being filled... in that case, TryDequeue will
            // return false.)
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].SequenceNumber = i;
            }
        }

        internal int Capacity => _slots.Length;

        internal int FreezeOffset => _slots.Length * 2;

        internal void EnsureFrozenForEnqueues() // must only be called while queue's segment lock is held
        {
            if (!_frozenForEnqueues) // flag used to ensure we don't increase the Tail more than once if frozen more than once
            {
                // Bump the Tail before setting the flag, as TryDequeue reads the flag and then
                // the Tail: a set flag must guarantee that the Tail read includes the FreezeOffset.
                Interlocked.Add(ref _headAndTail.Tail, FreezeOffset);
                _frozenForEnqueues = true;
            }
        }

        public bool TryDequeue(out T item)
        {
            Slot[] slots = _slots;

            // Loop in case of contention...
            SpinWait spinner = default;
            while (true)
            {
                // Get the head at which to try to dequeue.
                int currentHead = Volatile.Read(ref _headAndTail.Head);
                int slotsIndex = currentHead & _slotsMask;

                // Read the sequence number for the head position.
                int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

                // We can dequeue from this slot if it's been filled by an enqueuer, which
                // would have left the sequence number at pos+1.
                int diff = sequenceNumber - (currentHead + 1);
                if (diff == 0)
                {
                    // We may be racing with other dequeuers.  Try to reserve the slot by incrementing
                    // the head.  Once we've done that, no one else will be able to read from this slot,
                    // and no enqueuer will be able to read from this slot until we've written the new
                    // sequence number. WARNING: The next few lines are not reliable on a runtime that
                    // supports thread aborts. If a thread abort were to sneak in after the CompareExchange
                    // but before the Volatile.Write, enqueuers trying to enqueue into this slot would
                    // spin indefinitely.  If this implementation is ever used on such a platform, this
                    // if block should be wrapped in a finally / prepared region.
                    if (Interlocked.CompareExchange(ref _headAndTail.Head, currentHead + 1, currentHead) == currentHead)
                    {
                        // Successfully reserved the slot.  Note that after the above CompareExchange, other threads
                        // trying to dequeue from this slot will end up spinning until we do the subsequent Write.
                        item = slots[slotsIndex].Item;
                        if (!Volatile.Read(ref _preservedForObservation))
                        {
                            // If we're preserving, though, we don't zero out the slot, as we need it for
                            // enumerations, peeking, ToArray, etc.  And we don't update the sequence number,
                            // so that an enqueuer will see it as full and be forced to move to a new segment.
                            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                            {
                                slots[slotsIndex].Item = default;
                            }
                            Volatile.Write(ref slots[slotsIndex].SequenceNumber, currentHead + slots.Length);
                        }
                        return true;
                    }

                    // The head was already advanced by another thread. A newer head has already been observed and the next
                    // iteration would make forward progress, so there's no need to spin-wait before trying again.
                }
                else if (diff < 0)
                {
                    // The sequence number was less than what we needed, which means this slot doesn't
                    // yet contain a value we can dequeue, i.e. the segment is empty.  Technically it's
                    // possible that multiple enqueuers could have written concurrently, with those
                    // getting later slots actually finishing first, so there could be elements after
                    // this one that are available, but we need to dequeue in order.  So before declaring
                    // failure and that the segment is empty, we check the tail to see if we're actually
                    // empty or if we're just waiting for items in flight or after this one to become available.
                    bool frozen = Volatile.Read(ref _frozenForEnqueues);
                    int currentTail = _headAndTail.Tail;
                    if (currentTail - currentHead <= 0 || (frozen && (currentTail - FreezeOffset - currentHead <= 0)))
                    {
                        item = default;
                        return false;
                    }

                    // It's possible it could have become frozen after we checked _frozenForEnqueues
                    // and before reading the tail.  That's ok: in that rare race condition, we just
                    // loop around again. This is not necessarily an always-forward-progressing
                    // situation since this thread is waiting for another to write to the slot and
                    // this thread may have to check the same slot multiple times. Spin-wait to avoid
                    // a potential busy-wait, and then try again.
                    spinner.SpinOnce(sleep1Threshold: -1);
                }
                else
                {
                    // The item was already dequeued by another thread. The head has already been updated beyond what was
                    // observed above, and the sequence number observed above as a volatile load is more recent than the update
                    // to the head. So, the next iteration of the loop is guaranteed to see a new head. Since this is an
                    // always-forward-progressing situation, there's no need to spin-wait before trying again.
                }
            }
        }

        public bool TryPeek(out T result, bool resultUsed)
        {
            if (resultUsed)
            {
                // In order to ensure we don't get a torn read on the value, we mark the segment
                // as preserving for observation.  Additional items can still be enqueued to this
                // segment, but no space will be freed during dequeues, such that the segment will
                // no longer be reusable.
                _preservedForObservation = true;
                Interlocked.MemoryBarrier();
            }

            Slot[] slots = _slots;

            // Loop in case of contention...
            SpinWait spinner = default;
            while (true)
            {
                // Get the head at which to try to peek.
                int currentHead = Volatile.Read(ref _headAndTail.Head);
                int slotsIndex = currentHead & _slotsMask;

                // Read the sequence number for the head position.
                int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

                // We can peek from this slot if it's been filled by an enqueuer, which
                // would have left the sequence number at pos+1.
                int diff = sequenceNumber - (currentHead + 1);
                if (diff == 0)
                {
                    result = resultUsed ? slots[slotsIndex].Item : default;
                    return true;
                }
                else if (diff < 0)
                {
                    // The sequence number was less than what we needed, which means this slot doesn't
                    // yet contain a value we can peek, i.e. the segment is empty.  Technically it's
                    // possible that multiple enqueuers could have written concurrently, with those
                    // getting later slots actually finishing first, so there could be elements after
                    // this one that are available, but we need to peek in order.  So before declaring
                    // failure and that the segment is empty, we check the tail to see if we're actually
                    // empty or if we're just waiting for items in flight or after this one to become available.
                    bool frozen = Volatile.Read(ref _frozenForEnqueues);
                    int currentTail = _headAndTail.Tail;
                    if (currentTail - currentHead <= 0 || (frozen && (currentTail - FreezeOffset - currentHead <= 0)))
                    {
                        result = default;
                        return false;
                    }

                    // It's possible it could have become frozen after we checked _frozenForEnqueues
                    // and before reading the tail.  That's ok: in that rare race condition, we just
                    // loop around again. This is not necessarily an always-forward-progressing
                    // situation since this thread is waiting for another to write to the slot and
                    // this thread may have to check the same slot multiple times. Spin-wait to avoid
                    // a potential busy-wait, and then try again.
                    spinner.SpinOnce(sleep1Threshold: -1);
                }
                else
                {
                    // The item was already dequeued by another thread. The head has already been updated beyond what was
                    // observed above, and the sequence number observed above as a volatile load is more recent than the update
                    // to the head. So, the next iteration of the loop is guaranteed to see a new head. Since this is an
                    // always-forward-progressing situation, there's no need to spin-wait before trying again.
                }
            }
        }

        public bool TryEnqueue(T item)
        {
            Slot[] slots = _slots;

            // Loop in case of contention...
            while (true)
            {
                // Get the tail at which to try to return.
                int currentTail = Volatile.Read(ref _headAndTail.Tail);
                int slotsIndex = currentTail & _slotsMask;

                // Read the sequence number for the tail position.
                int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

                // The slot is empty and ready for us to enqueue into it if its sequence
                // number matches the slot.
                int diff = sequenceNumber - currentTail;
                if (diff == 0)
                {
                    // We may be racing with other enqueuers.  Try to reserve the slot by incrementing
                    // the tail.  Once we've done that, no one else will be able to write to this slot,
                    // and no dequeuer will be able to read from this slot until we've written the new
                    // sequence number. WARNING: The next few lines are not reliable on a runtime that
                    // supports thread aborts. If a thread abort were to sneak in after the CompareExchange
                    // but before the Volatile.Write, other threads will spin trying to access this slot.
                    // If this implementation is ever used on such a platform, this if block should be
                    // wrapped in a finally / prepared region.
                    if (Interlocked.CompareExchange(ref _headAndTail.Tail, currentTail + 1, currentTail) == currentTail)
                    {
                        // Successfully reserved the slot.  Note that after the above CompareExchange, other threads
                        // trying to return will end up spinning until we do the subsequent Write.
                        slots[slotsIndex].Item = item;
                        Volatile.Write(ref slots[slotsIndex].SequenceNumber, currentTail + 1);
                        return true;
                    }

                    // The tail was already advanced by another thread. A newer tail has already been observed and the next
                    // iteration would make forward progress, so there's no need to spin-wait before trying again.
                }
                else if (diff < 0)
                {
                    // The sequence number was less than what we needed, which means this slot still
                    // contains a value, i.e. the segment is full.  Technically it's possible that multiple
                    // dequeuers could have read concurrently, with those getting later slots actually
                    // finishing first, so there could be spaces after this one that are available, but
                    // we need to enqueue in order.
                    return false;
                }
                else
                {
                    // Either the slot contains an item, or it is empty but because the slot was filled and dequeued. In either
                    // case, the tail has already been updated beyond what was observed above, and the sequence number observed
                    // above as a volatile load is more recent than the update to the tail. So, the next iteration of the loop
                    // is guaranteed to see a new tail. Since this is an always-forward-progressing situation, there's no need
                    // to spin-wait before trying again.
                }
            }
        }

        internal struct Slot
        {
            public T Item; // SOS's ThreadPool command depends on this being at the beginning of the struct when T is a reference type
            public int SequenceNumber;
        }
    }

    internal struct PaddedHeadAndTail
    {
        // A cache line before, between and after (as .NET's explicit layout): producers and consumers
        // touch different lines.
        private long _pad0, _pad1, _pad2, _pad3, _pad4, _pad5, _pad6, _pad7;
        public int Head;
        private int _pad8;
        private long _pad9, _pad10, _pad11, _pad12, _pad13, _pad14, _pad15;
        public int Tail;
        private int _pad16;
        private long _pad17, _pad18, _pad19, _pad20, _pad21, _pad22, _pad23;
    }
}
