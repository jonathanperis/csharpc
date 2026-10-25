// System.Net.Sockets.SocketAsyncEventArgs: the reusable state of one asynchronous socket operation
// (Socket.ReceiveAsync/SendAsync/AcceptAsync(SocketAsyncEventArgs)), after .NET's
// SocketAsyncEventArgs.cs. The operations run on the socket engine (SocketEngine.cs); a pending one
// completes by calling OnCompleted, which raises Completed, on the thread pool — under the execution
// context captured when it started, unless the instance was created with
// unsafeSuppressExecutionContextFlow (Kestrel's SocketAwaitableEventArgs).
#nullable disable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.Sockets.SocketAsyncEventArgs")]
    public class SocketAsyncEventArgsImpl : EventArgs, IDisposable
    {
        private Memory<byte> _buffer;
        private byte[] _array;
        private int _offset, _count;
        private IList<ArraySegment<byte>> _bufferList;
        private readonly bool _flowExecutionContext;
        private ExecutionFlow _capturedFlow;
        private int _inProgress;
        private bool _disposed;
        internal SocketImpl CurrentSocket;

        public SocketAsyncEventArgsImpl() : this(false) { }

        public SocketAsyncEventArgsImpl(bool unsafeSuppressExecutionContextFlow)
        {
            _flowExecutionContext = !unsafeSuppressExecutionContextFlow;
        }

        public event EventHandler<SocketAsyncEventArgs> Completed;

        protected virtual void OnCompleted(SocketAsyncEventArgs e) =>
            Completed?.Invoke(((SocketAsyncEventArgsImpl)(object)e).CurrentSocket, e);

        public byte[] Buffer => _array;
        public Memory<byte> MemoryBuffer => _buffer;
        public int Offset => _offset;
        public int Count => _count;

        public IList<ArraySegment<byte>> BufferList
        {
            get => _bufferList;
            set
            {
                ThrowIfInProgress();
                if (value != null)
                {
                    if (!_buffer.Equals(default(Memory<byte>)))
                        throw new ArgumentException("Buffer and BufferList properties cannot both be set to non-null values.", "value");
                    for (int i = 0; i < value.Count; i++)
                        if (value[i].Array == null) throw new ArgumentNullException("value");
                }
                _bufferList = value;
            }
        }

        public int BytesTransferred { get; private set; }
        public SocketError SocketError { get; set; }
        public SocketFlags SocketFlags { get; set; }
        public object UserToken { get; set; }
        public Socket AcceptSocket { get; set; }
        public Socket ConnectSocket => null;
        public EndPoint RemoteEndPoint { get; set; }
        public SocketAsyncOperation LastOperation { get; private set; }
        public Exception ConnectByNameError => null;

        public void SetBuffer(int offset, int count)
        {
            ThrowIfInProgress();
            if (_array == null) throw new InvalidOperationException("This operation is only allowed when the Buffer property refers to an array.");
            SetArrayBuffer(_array, offset, count);
        }

        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            ThrowIfInProgress();
            if (buffer == null)
            {
                _array = null;
                _buffer = default;
                _offset = _count = 0;
                return;
            }
            if (_bufferList != null) throw new ArgumentException("Buffer and BufferList properties cannot both be set to non-null values.", "buffer");
            SetArrayBuffer(buffer, offset, count);
        }

        private void SetArrayBuffer(byte[] buffer, int offset, int count)
        {
            if ((uint)offset > (uint)buffer.Length) throw new ArgumentOutOfRangeException("offset");
            if ((uint)count > (uint)(buffer.Length - offset)) throw new ArgumentOutOfRangeException("count");
            _array = buffer;
            _buffer = buffer;
            _offset = offset;
            _count = count;
        }

        public void SetBuffer(Memory<byte> buffer)
        {
            ThrowIfInProgress();
            if (buffer.Length != 0 && _bufferList != null)
                throw new ArgumentException("Buffer and BufferList properties cannot both be set to non-null values.", "buffer");
            _array = null;
            _buffer = buffer;
            _offset = 0;
            _count = buffer.Length;
        }

        /// <summary>The bytes the operation reads or writes: the buffer (offset, count) or its memory.</summary>
        internal Memory<byte> OperationBuffer => _array != null ? _buffer.Slice(_offset, _count) : _buffer;

        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfInProgress()
        {
            if (Volatile.Read(ref _inProgress) != 0)
                throw new InvalidOperationException("An asynchronous socket operation is already in progress using this SocketAsyncEventArgs instance.");
        }

        internal void StartOperation(SocketImpl socket, SocketAsyncOperation operation)
        {
            if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
                throw new InvalidOperationException("An asynchronous socket operation is already in progress using this SocketAsyncEventArgs instance.");
            if (_disposed)
            {
                Volatile.Write(ref _inProgress, 0);
                throw new ObjectDisposedException(GetType().FullName);
            }
            CurrentSocket = socket;
            LastOperation = operation;
            BytesTransferred = 0;
            SocketError = SocketError.Success;
            _capturedFlow = _flowExecutionContext ? ExecutionFlow.Current : null;
        }

        /// <summary>The operation completed synchronously (the caller returns false).</summary>
        internal void FinishSynchronously(SocketError error, int bytesTransferred)
        {
            BytesTransferred = bytesTransferred;
            SocketError = error;
            _capturedFlow = null;
            Volatile.Write(ref _inProgress, 0);
        }

        /// <summary>A pending operation completed: OnCompleted runs on the thread pool.</summary>
        internal void FinishAsynchronously(SocketError error, int bytesTransferred)
        {
            BytesTransferred = bytesTransferred;
            SocketError = error;
            ThreadPool.UnsafeQueueUserWorkItem(static e => e.RaiseCompleted(), this, false);
        }

        private void RaiseCompleted()
        {
            ExecutionFlow flow = _capturedFlow;
            _capturedFlow = null;
            Volatile.Write(ref _inProgress, 0);
            ExecutionFlow saved = ExecutionFlow.Current;
            ExecutionFlow.Current = flow;
            try { OnCompleted((SocketAsyncEventArgs)(object)this); }
            finally { ExecutionFlow.Current = saved; }
        }
    }
}
