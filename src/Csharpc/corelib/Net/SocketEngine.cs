// Asynchronous socket operations. On Linux, as .NET's SocketAsyncEngine.Unix: a socket used
// asynchronously becomes non-blocking and joins one epoll instance (edge-triggered), read by a
// dedicated thread; each socket keeps FIFO queues of pending reads (receive, accept) and writes (send).
// An operation is first tried at once; when the kernel says EAGAIN it waits in its queue and is retried
// when epoll reports readiness. Completions run on the thread pool. Elsewhere (macOS, Windows) an
// operation runs as a blocking call on the thread pool: correct, but a thread per pending operation.
#nullable disable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    /// <summary>One asynchronous socket operation.</summary>
    internal abstract unsafe class SocketOperation
    {
        internal SocketError Error;
        internal int Transferred;
        internal CancellationTokenRegistration Registration;

        /// <summary>One non-blocking attempt: true when the operation finished (successfully or not), false on EAGAIN.</summary>
        internal abstract bool TryRun(SocketImpl socket);

        /// <summary>Reports the outcome (on the thread pool for pending operations).</summary>
        internal abstract void Complete(bool synchronously);

        internal CancellationToken Token;
        internal bool Canceled;

        internal bool Fail(int error)
        {
            Error = NetNative.ToSocketError(error);
            return true;
        }

        internal virtual bool IsWrite => false;

        /// <summary>Clears the outcome of a previous run (operations owned by a SocketAsyncEventArgs are reused).</summary>
        internal virtual void Reset()
        {
            Error = SocketError.Success;
            Transferred = 0;
            Registration = default;
            Token = default;
            Canceled = false;
        }
    }

    /// <summary>Receive into a buffer (or a buffer list); an empty buffer waits for data without reading it.</summary>
    internal unsafe class ReceiveOperation : SocketOperation
    {
        internal Memory<byte> Buffer;
        internal IList<ArraySegment<byte>> BufferList;
        internal SocketFlags Flags;

        internal override bool TryRun(SocketImpl socket)
        {
            int error;
            int n;
            if (BufferList != null)
            {
                // Like readv: fills the segments in order; stops at a short read.
                int total = 0;
                for (int i = 0; i < BufferList.Count; i++)
                {
                    ArraySegment<byte> segment = BufferList[i];
                    if (segment.Count == 0) continue;
                    fixed (byte* p = &segment.Array[segment.Offset])
                        n = NetNative.TryReceive(socket.RawHandle, p, segment.Count, (int)Flags, out error);
                    if (n < 0)
                    {
                        if (total > 0) break;
                        if (NetNative.IsWouldBlock(error)) return false;
                        return Fail(error);
                    }
                    total += n;
                    if (n < segment.Count) break;
                }
                Transferred = total;
                return true;
            }
            if (Buffer.Length == 0)
            {
                // A zero-byte receive completes when data (or the end of the stream) is there: peek one byte.
                byte peek;
                n = NetNative.TryReceive(socket.RawHandle, &peek, 1, NetNative.MSG_PEEK, out error);
                if (n < 0) return NetNative.IsWouldBlock(error) ? false : Fail(error);
                Transferred = 0;
                return true;
            }
            fixed (byte* p = Buffer.Span)
                n = NetNative.TryReceive(socket.RawHandle, p, Buffer.Length, (int)Flags, out error);
            if (n < 0) return NetNative.IsWouldBlock(error) ? false : Fail(error);
            Transferred = n;
            return true;
        }

        internal override void Complete(bool synchronously) { }
    }

    /// <summary>Send a buffer (or a buffer list): completes when every byte is sent, as .NET's stream sends do.</summary>
    internal unsafe class SendOperation : SocketOperation
    {
        internal ReadOnlyMemory<byte> Buffer;
        internal IList<ArraySegment<byte>> BufferList;
        private int _segment, _segmentOffset;

        internal override bool IsWrite => true;

        internal override void Reset()
        {
            base.Reset();
            _segment = _segmentOffset = 0;
        }

        internal override bool TryRun(SocketImpl socket)
        {
            int error;
            if (BufferList != null)
            {
                while (_segment < BufferList.Count)
                {
                    ArraySegment<byte> segment = BufferList[_segment];
                    int remaining = segment.Count - _segmentOffset;
                    if (remaining > 0)
                    {
                        int n;
                        fixed (byte* p = &segment.Array[segment.Offset])
                            n = NetNative.TrySend(socket.RawHandle, p + _segmentOffset, remaining, out error);
                        if (n < 0) return NetNative.IsWouldBlock(error) ? false : Fail(error);
                        Transferred += n;
                        _segmentOffset += n;
                        if (n < remaining) continue;
                    }
                    _segment++;
                    _segmentOffset = 0;
                }
                return true;
            }
            while (Transferred < Buffer.Length)
            {
                int n;
                fixed (byte* p = Buffer.Span)
                    n = NetNative.TrySend(socket.RawHandle, p + Transferred, Buffer.Length - Transferred, out error);
                if (n < 0) return NetNative.IsWouldBlock(error) ? false : Fail(error);
                Transferred += n;
            }
            return true;
        }

        internal override void Complete(bool synchronously) { }
    }

    internal unsafe class AcceptOperation : SocketOperation
    {
        internal SocketImpl Accepted;

        internal override void Reset()
        {
            base.Reset();
            Accepted = null;
        }

        internal override bool TryRun(SocketImpl socket)
        {
            long client = NetNative.TryAccept(socket.RawHandle, out int error);
            if (client == NetNative.InvalidSocket) return NetNative.IsWouldBlock(error) ? false : Fail(error);
            Accepted = socket.CreateAccepted(client);
            return true;
        }

        internal override void Complete(bool synchronously) { }
    }

    /// <summary>The operations of a SocketAsyncEventArgs: its buffers in, BytesTransferred and SocketError out.</summary>
    internal static class EventArgsOperations
    {
        internal sealed class Receive : ReceiveOperation
        {
            internal SocketAsyncEventArgsImpl Args;
            internal override void Complete(bool synchronously)
            {
                if (synchronously) Args.FinishSynchronously(Error, Transferred);
                else Args.FinishAsynchronously(Error, Transferred);
            }
        }

        internal sealed class Send : SendOperation
        {
            internal SocketAsyncEventArgsImpl Args;
            internal override void Complete(bool synchronously)
            {
                if (synchronously) Args.FinishSynchronously(Error, Transferred);
                else Args.FinishAsynchronously(Error, Transferred);
            }
        }

        internal sealed class Accept : AcceptOperation
        {
            internal SocketAsyncEventArgsImpl Args;
            internal override void Complete(bool synchronously)
            {
                if (Error == SocketError.Success) Args.AcceptSocket = (Socket)(object)Accepted;
                if (synchronously) Args.FinishSynchronously(Error, 0);
                else Args.FinishAsynchronously(Error, 0);
            }
        }
    }

    /// <summary>The Task-based operations (ValueTask&lt;int&gt; ReceiveAsync/SendAsync, ValueTask&lt;Socket&gt; AcceptAsync).</summary>
    internal static class TaskOperations
    {
        internal sealed class Receive : ReceiveOperation
        {
            internal readonly TaskCompletionSource<int> Source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal override void Complete(bool synchronously)
            {
                Registration.Dispose();
                if (Canceled) Source.TrySetCanceled();
                else if (Error == SocketError.Success) Source.TrySetResult(Transferred);
                else Source.TrySetException(new SocketException((int)Error));
            }
        }

        internal sealed class Send : SendOperation
        {
            internal readonly TaskCompletionSource<int> Source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal override void Complete(bool synchronously)
            {
                Registration.Dispose();
                if (Canceled) Source.TrySetCanceled();
                else if (Error == SocketError.Success) Source.TrySetResult(Transferred);
                else Source.TrySetException(new SocketException((int)Error));
            }
        }

        internal sealed class Accept : AcceptOperation
        {
            internal readonly TaskCompletionSource<Socket> Source = new TaskCompletionSource<Socket>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal override void Complete(bool synchronously)
            {
                Registration.Dispose();
                if (Canceled) Source.TrySetCanceled();
                else if (Error == SocketError.Success) Source.TrySetResult((Socket)(object)Accepted);
                else Source.TrySetException(new SocketException((int)Error));
            }
        }
    }

    /// <summary>A socket's pending operations.</summary>
    internal sealed class SocketAsyncContext
    {
        internal readonly SocketImpl Socket;
        internal readonly Queue<SocketOperation> Reads = new Queue<SocketOperation>();
        internal readonly Queue<SocketOperation> Writes = new Queue<SocketOperation>();
        internal bool Closed;
        internal ulong Id;

        internal SocketAsyncContext(SocketImpl socket) { Socket = socket; }

        /// <summary>
        /// Starts an operation: true when it is pending (it completes later through op.Complete(false)),
        /// false when it finished now (the caller reports it synchronously).
        /// </summary>
        internal bool Start(SocketOperation op)
        {
            if (SocketEngine.Blocking)
            {
                if (Closed) { op.Error = SocketError.OperationAborted; return false; }
                ThreadPool.UnsafeQueueUserWorkItem(static state =>
                {
                    // A blocking socket finishes the attempt (EAGAIN only after a receive/send timeout).
                    if (!state.op.TryRun(state.socket)) state.op.Error = SocketError.TimedOut;
                    state.op.Complete(false);
                }, (op: op, socket: Socket), false);
                return true;
            }
            Queue<SocketOperation> queue = op.IsWrite ? Writes : Reads;
            lock (this)
            {
                if (Closed) { op.Error = SocketError.OperationAborted; return false; }
                // Operations of one kind complete in order: a new one waits behind pending ones.
                if (queue.Count == 0 && op.TryRun(Socket)) return false;
                queue.Enqueue(op);
            }
            return true;
        }

        /// <summary>Takes a pending operation out of its queue (cancellation); false if it already completed.</summary>
        internal bool Remove(SocketOperation op)
        {
            lock (this)
            {
                Queue<SocketOperation> queue = op.IsWrite ? Writes : Reads;
                if (!queue.Contains(op)) return false;
                var kept = new Queue<SocketOperation>();
                while (queue.Count > 0)
                {
                    SocketOperation next = queue.Dequeue();
                    if (!ReferenceEquals(next, op)) kept.Enqueue(next);
                }
                while (kept.Count > 0) queue.Enqueue(kept.Dequeue());
                return true;
            }
        }

        /// <summary>Readiness from the engine: retries the waiting operations of that direction.</summary>
        internal void Process(bool readable, bool writable)
        {
            // One operation at a time, each completed outside the lock: a socket rarely has more than one
            // pending read and one pending write (Kestrel never does), and collecting them in a list
            // allocated two objects per event.
            if (readable) while (DrainOne(Reads) is { } op) op.Complete(false);
            if (writable) while (DrainOne(Writes) is { } op) op.Complete(false);
        }

        private SocketOperation DrainOne(Queue<SocketOperation> queue)
        {
            lock (this)
            {
                if (Closed || queue.Count == 0 || !queue.Peek().TryRun(Socket)) return null;
                return queue.Dequeue();
            }
        }

        /// <summary>The socket is closing: every pending operation ends with OperationAborted.</summary>
        internal void Close()
        {
            List<SocketOperation> aborted = new List<SocketOperation>();
            lock (this)
            {
                if (Closed) return;
                Closed = true;
                while (Reads.Count > 0) aborted.Add(Reads.Dequeue());
                while (Writes.Count > 0) aborted.Add(Writes.Dequeue());
            }
            SocketEngine.Unregister(this);
            foreach (SocketOperation op in aborted)
            {
                op.Error = SocketError.OperationAborted;
                op.Complete(false);
            }
        }
    }

    internal static unsafe class SocketEngine
    {
#if CSHARPC_TARGET_LINUX
        [DllImport("__Internal", EntryPoint = "csrt_epoll_create")] private static extern int EpollCreate();
        [DllImport("__Internal", EntryPoint = "csrt_epoll_add")] private static extern int EpollAdd(int epfd, int fd, ulong data);
        [DllImport("__Internal", EntryPoint = "csrt_epoll_wait")] private static extern int EpollWait(int epfd, ulong* datas, uint* events, int max);

        private const uint EPOLLIN = 0x1, EPOLLOUT = 0x4, EPOLLERR = 0x8, EPOLLHUP = 0x10, EPOLLRDHUP = 0x2000;

        private static readonly object s_lock = new object();
        private static int s_epoll = -1;
        // Registered contexts, keyed by their address: the epoll data of a socket is its context's
        // address, so the event loop gets from an event to its context without a lookup or a lock (the
        // collector doesn't move objects). This set keeps every registered context alive until it is
        // unregistered; an event already read for a context closed meanwhile is still on the event
        // loop's stack, which the collector scans, and Process ignores closed contexts.
        private static readonly Dictionary<ulong, SocketAsyncContext> s_contexts = new Dictionary<ulong, SocketAsyncContext>();

        /// <summary>Makes the socket non-blocking and watches it.</summary>
        internal static void Register(SocketAsyncContext context)
        {
            NetNative.SetNonBlocking(context.Socket.RawHandle);
            lock (s_lock)
            {
                if (s_epoll < 0)
                {
                    int epoll = EpollCreate();
                    if (epoll < 0) throw NetNative.Error(-epoll);
                    s_epoll = epoll;
                    var thread = new Thread(EventLoop);
                    thread.IsBackground = true;
                    thread.Name = ".NET Sockets";
                    thread.Start();
                }
                context.Id = (ulong)(long)CoreIntrinsics.ObjectToPointer(context);
                s_contexts[context.Id] = context;
                int error = EpollAdd(s_epoll, (int)context.Socket.RawHandle, context.Id);
                if (error != 0)
                {
                    s_contexts.Remove(context.Id);
                    throw NetNative.Error(error);
                }
            }
        }

        /// <summary>Forgets the socket (closing its descriptor removes it from the epoll set).</summary>
        internal static void Unregister(SocketAsyncContext context)
        {
            lock (s_lock) s_contexts.Remove(context.Id);
        }

        private static void EventLoop()
        {
            const int Max = 256;
            ulong* datas = stackalloc ulong[Max];
            uint* events = stackalloc uint[Max];
            while (true)
            {
                int n = EpollWait(s_epoll, datas, events, Max);
                if (n < 0) throw NetNative.Error(-n);
                for (int i = 0; i < n; i++)
                {
                    var context = (SocketAsyncContext)CoreIntrinsics.PointerToObject((IntPtr)(long)datas[i]);
                    uint e = events[i];
                    bool error = (e & (EPOLLERR | EPOLLHUP)) != 0;
                    context.Process(error || (e & (EPOLLIN | EPOLLRDHUP)) != 0, error || (e & EPOLLOUT) != 0);
                }
            }
        }

        internal const bool Blocking = false;
#else
        // No readiness notification yet: each pending operation is a blocking call on the thread pool.
        internal static void Register(SocketAsyncContext context) { }
        internal static void Unregister(SocketAsyncContext context) { }
        internal const bool Blocking = true;
#endif
    }
}
