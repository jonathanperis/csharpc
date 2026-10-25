// System.IO streams: Stream, FileStream (buffered, over POSIX fds), MemoryStream, BinaryReader/Writer.
#nullable disable
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    // System.IO.Stream, after dotnet/runtime's Stream.cs (release/11.0): the same virtual surface (so streams
    // compiled from IL can override any of it: ASP.NET Core's request/response streams, System.IO.Pipelines'
    // adapters), exception types and messages. The default asynchronous operations run the synchronous ones on
    // the thread pool one at a time, as .NET's do (it serializes them with a semaphore; here each one starts
    // when the previous one has finished), and go through BeginRead/BeginWrite so overrides of those are used.
    [Shadows("System.IO.Stream")]
    public abstract class StreamImpl : MarshalByRefObjectImpl, IDisposable, IAsyncDisposable
    {
        public static readonly Stream Null = (Stream)(object)new NullStream();

        private object _asyncLock;
        private Task _asyncTail;

        public abstract bool CanRead { get; }
        public abstract bool CanSeek { get; }
        public abstract bool CanWrite { get; }
        public virtual bool CanTimeout => false;
        public abstract long Length { get; }
        public abstract long Position { get; set; }

        public virtual int ReadTimeout
        {
            get => throw new InvalidOperationException("Timeouts are not supported on this stream.");
            set => throw new InvalidOperationException("Timeouts are not supported on this stream.");
        }

        public virtual int WriteTimeout
        {
            get => throw new InvalidOperationException("Timeouts are not supported on this stream.");
            set => throw new InvalidOperationException("Timeouts are not supported on this stream.");
        }

        public abstract void Flush();
        public abstract int Read(byte[] buffer, int offset, int count);
        public abstract long Seek(long offset, SeekOrigin origin);
        public abstract void SetLength(long value);
        public abstract void Write(byte[] buffer, int offset, int count);

        public virtual int Read(Span<byte> buffer)
        {
            byte[] shared = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                int n = Read(shared, 0, buffer.Length);
                if ((uint)n > (uint)buffer.Length) throw new IOException("Stream was too long.");
                new ReadOnlySpan<byte>(shared, 0, n).CopyTo(buffer);
                return n;
            }
            finally { ArrayPool<byte>.Shared.Return(shared); }
        }

        public virtual void Write(ReadOnlySpan<byte> buffer)
        {
            byte[] shared = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(shared);
                Write(shared, 0, buffer.Length);
            }
            finally { ArrayPool<byte>.Shared.Return(shared); }
        }

        public virtual int ReadByte()
        {
            byte[] one = new byte[1];
            return Read(one, 0, 1) == 0 ? -1 : one[0];
        }

        public virtual void WriteByte(byte value)
        {
            byte[] one = new byte[1];
            one[0] = value;
            Write(one, 0, 1);
        }

        public void ReadExactly(Span<byte> buffer) => ReadAtLeastCore(buffer, buffer.Length, true);

        public void ReadExactly(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            ReadAtLeastCore(buffer.AsSpan(offset, count), count, true);
        }

        public int ReadAtLeast(Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream = true)
        {
            ValidateReadAtLeastArguments(buffer.Length, minimumBytes);
            return ReadAtLeastCore(buffer, minimumBytes, throwOnEndOfStream);
        }

        private int ReadAtLeastCore(Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream)
        {
            int total = 0;
            while (total < minimumBytes)
            {
                int n = Read(buffer.Slice(total));
                if (n == 0)
                {
                    if (throwOnEndOfStream) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                    return total;
                }
                total += n;
            }
            return total;
        }

        public void CopyTo(Stream destination) => CopyTo(destination, GetCopyBufferSize());

        public virtual void CopyTo(Stream destination, int bufferSize)
        {
            ValidateCopyToArguments(destination, bufferSize);
            if (!CanRead)
            {
                if (CanWrite) throw new NotSupportedException("Stream does not support reading.");
                throw new ObjectDisposedException(GetType().Name, "Cannot access a closed Stream.");
            }
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int n;
                while ((n = Read(buffer, 0, buffer.Length)) != 0) destination.Write(buffer, 0, n);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public Task CopyToAsync(Stream destination) => CopyToAsync(destination, GetCopyBufferSize());
        public Task CopyToAsync(Stream destination, int bufferSize) => CopyToAsync(destination, bufferSize, CancellationToken.None);
        public Task CopyToAsync(Stream destination, CancellationToken cancellationToken) => CopyToAsync(destination, GetCopyBufferSize(), cancellationToken);

        public virtual Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            ValidateCopyToArguments(destination, bufferSize);
            if (!CanRead)
            {
                if (CanWrite) throw new NotSupportedException("Stream does not support reading.");
                throw new ObjectDisposedException(GetType().Name, "Cannot access a closed Stream.");
            }
            return CopyToAsyncCore((Stream)(object)this, destination, bufferSize, cancellationToken);
        }

        private static async Task CopyToAsyncCore(Stream source, Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int n;
                while ((n = await source.ReadAsync(new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false)) != 0)
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, n), cancellationToken).ConfigureAwait(false);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private int GetCopyBufferSize()
        {
            int bufferSize = 81920;
            if (CanSeek)
            {
                long length = Length, position = Position;
                if (length <= position) bufferSize = 1;
                else
                {
                    long remaining = length - position;
                    if (remaining > 0) bufferSize = (int)Math.Min(bufferSize, remaining);
                }
            }
            return bufferSize;
        }

        public void Dispose() => Close();

        public virtual void Close() => Dispose(true);

        protected virtual void Dispose(bool disposing) { }

        public virtual ValueTask DisposeAsync()
        {
            try
            {
                Dispose();
                return default;
            }
            catch (Exception e)
            {
                return ValueTask.FromException(e);
            }
        }

        public Task FlushAsync() => FlushAsync(CancellationToken.None);

        public virtual Task FlushAsync(CancellationToken cancellationToken) =>
            Task.Factory.StartNew(state => ((Stream)state).Flush(), this, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

        // ---- asynchronous reads and writes ---------------------------------------------------------
        public virtual IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (!CanRead) throw new NotSupportedException("Stream does not support reading.");
            return new StreamAsyncResult(RunSerialized(() => Read(buffer, offset, count)), true, callback, state);
        }

        public virtual int EndRead(IAsyncResult asyncResult) => End(asyncResult, true);

        public virtual IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (!CanWrite) throw new NotSupportedException("Stream does not support writing.");
            return new StreamAsyncResult(RunSerialized(() => { Write(buffer, offset, count); return 0; }), false, callback, state);
        }

        public virtual void EndWrite(IAsyncResult asyncResult) => End(asyncResult, false);

        private static int End(IAsyncResult asyncResult, bool read)
        {
            if (asyncResult == null) throw new ArgumentNullException("asyncResult");
            if (!(asyncResult is StreamAsyncResult result) || result.IsRead != read)
                throw new ArgumentException("Either the IAsyncResult object did not come from the corresponding async method on this type, or the End method was called multiple times with the same IAsyncResult.");
            if (result.EndCalled)
                throw new InvalidOperationException("Either the IAsyncResult object did not come from the corresponding async method on this type, or the End method was called multiple times with the same IAsyncResult.");
            result.EndCalled = true;
            return result.Task.GetAwaiter().GetResult();
        }

        /// <summary>Runs a default asynchronous operation on the thread pool once the previous one has finished.</summary>
        private Task<int> RunSerialized(Func<int> operation)
        {
            object gate = _asyncLock ?? Interlocked.CompareExchange(ref _asyncLock, new object(), null) ?? _asyncLock;
            lock (gate)
            {
                Task previous = _asyncTail;
                Task<int> task = previous == null || previous.IsCompleted
                    ? Task.Factory.StartNew(operation, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default)
                    : previous.ContinueWith(_ => operation(), CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
                _asyncTail = task;
                return task;
            }
        }

        private sealed class StreamAsyncResult : IAsyncResult
        {
            internal readonly Task<int> Task;
            internal readonly bool IsRead;
            internal bool EndCalled;
            private readonly object _state;

            internal StreamAsyncResult(Task<int> task, bool isRead, AsyncCallback callback, object state)
            {
                Task = task;
                IsRead = isRead;
                _state = state;
                if (callback != null)
                    task.ContinueWith(_ => callback(this), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            public object AsyncState => _state;
            public bool CompletedSynchronously => false;
            public bool IsCompleted => Task.IsCompleted;
            public WaitHandle AsyncWaitHandle => throw new NotSupportedException("IAsyncResult.AsyncWaitHandle of a stream operation is not supported by csharpc yet.");
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None);

        public virtual Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<int>(cancellationToken);
            var completion = new TaskCompletionSource<int>();
            BeginRead(buffer, offset, count, result =>
            {
                try { completion.SetResult(EndRead(result)); }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception e) { completion.SetException(e); }
            }, null);
            return completion.Task;
        }

        public virtual ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            byte[] shared = ArrayPool<byte>.Shared.Rent(buffer.Length);
            return FinishReadAsync(ReadAsync(shared, 0, buffer.Length, cancellationToken), shared, buffer);
        }

        private static async ValueTask<int> FinishReadAsync(Task<int> readTask, byte[] localBuffer, Memory<byte> localDestination)
        {
            try
            {
                int n = await readTask.ConfigureAwait(false);
                new ReadOnlySpan<byte>(localBuffer, 0, n).CopyTo(localDestination.Span);
                return n;
            }
            finally { ArrayPool<byte>.Shared.Return(localBuffer); }
        }

        public ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new ValueTask(ReadAtLeastAsyncCore(buffer, buffer.Length, true, cancellationToken).AsTask());

        public ValueTask ReadExactlyAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            ValidateBufferArguments(buffer, offset, count);
            return new ValueTask(ReadAtLeastAsyncCore(buffer.AsMemory(offset, count), count, true, cancellationToken).AsTask());
        }

        public ValueTask<int> ReadAtLeastAsync(Memory<byte> buffer, int minimumBytes, bool throwOnEndOfStream = true, CancellationToken cancellationToken = default)
        {
            ValidateReadAtLeastArguments(buffer.Length, minimumBytes);
            return ReadAtLeastAsyncCore(buffer, minimumBytes, throwOnEndOfStream, cancellationToken);
        }

        private async ValueTask<int> ReadAtLeastAsyncCore(Memory<byte> buffer, int minimumBytes, bool throwOnEndOfStream, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < minimumBytes)
            {
                int n = await ReadAsync(buffer.Slice(total), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                {
                    if (throwOnEndOfStream) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                    return total;
                }
                total += n;
            }
            return total;
        }

        public Task WriteAsync(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None);

        public virtual Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            var completion = new TaskCompletionSource();
            BeginWrite(buffer, offset, count, result =>
            {
                try { EndWrite(result); completion.SetResult(); }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception e) { completion.SetException(e); }
            }, null);
            return completion.Task;
        }

        public virtual ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            byte[] shared = ArrayPool<byte>.Shared.Rent(buffer.Length);
            buffer.Span.CopyTo(shared);
            return new ValueTask(FinishWriteAsync(WriteAsync(shared, 0, buffer.Length, cancellationToken), shared));
        }

        private static async Task FinishWriteAsync(Task writeTask, byte[] localBuffer)
        {
            try { await writeTask.ConfigureAwait(false); }
            finally { ArrayPool<byte>.Shared.Return(localBuffer); }
        }

        // ---- argument checks -----------------------------------------------------------------------
        protected static void ValidateBufferArguments(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
            if ((uint)count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException("count", "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }

        private static void ValidateReadAtLeastArguments(int bufferLength, int minimumBytes)
        {
            if (minimumBytes < 0) throw new ArgumentOutOfRangeException("minimumBytes", "Non-negative number required.");
            if (bufferLength < minimumBytes) throw new ArgumentOutOfRangeException("minimumBytes", "Must not be greater than the length of the buffer.");
        }

        protected static void ValidateCopyToArguments(Stream destination, int bufferSize)
        {
            if (destination == null) throw new ArgumentNullException("destination");
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException("bufferSize", bufferSize, "bufferSize ('" + bufferSize.ToString() + "') must be a non-negative and non-zero value.");
            if (!destination.CanWrite)
            {
                if (destination.CanRead) throw new NotSupportedException("Stream does not support writing.");
                throw new ObjectDisposedException(destination.GetType().Name, "Cannot access a closed Stream.");
            }
        }

        public static Stream Synchronized(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            return stream;
        }

        private sealed class NullStream : StreamImpl
        {
            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }
            public override void CopyTo(Stream destination, int bufferSize) { }
            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
            protected override void Dispose(bool disposing) { }
            public override void Flush() { }
            public override Task FlushAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override int Read(Span<byte> buffer) => 0;
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? Task.FromCanceled<int>(cancellationToken) : Task.FromResult(0);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? new ValueTask<int>(Task.FromCanceled<int>(cancellationToken)) : new ValueTask<int>(0);
            public override int ReadByte() => -1;
            public override void Write(byte[] buffer, int offset, int count) { }
            public override void Write(ReadOnlySpan<byte> buffer) { }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
                cancellationToken.IsCancellationRequested ? new ValueTask(Task.FromCanceled(cancellationToken)) : default;
            public override void WriteByte(byte value) { }
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long length) { }
        }
    }

    [Shadows("System.IO.FileStream")]
    public unsafe class FileStreamImpl : StreamImpl
    {
        private const int DefaultBufferSize = 4096;
        private int _fd;
        /// <summary>The file descriptor, for MemoryMappedFile.CreateFromFile.</summary>
        internal int Descriptor => _fd;
        private readonly string _path;
        private readonly bool _canRead, _canWrite, _append;
        private byte[] _readBuffer, _writeBuffer;
        private int _readPos, _readLen, _writePos;
        private readonly int _bufferSize;
        private bool _disposed;

        public FileStreamImpl(string path, FileMode mode) : this(path, mode, mode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite, FileShare.Read, DefaultBufferSize) { }
        public FileStreamImpl(string path, FileMode mode, FileAccess access) : this(path, mode, access, FileShare.Read, DefaultBufferSize) { }
        public FileStreamImpl(string path, FileMode mode, FileAccess access, FileShare share) : this(path, mode, access, share, DefaultBufferSize) { }
        /// <summary>
        /// FileOptions: SequentialScan, RandomAccess and WriteThrough are hints or durability modes no output
        /// depends on; Asynchronous only makes IsAsync true (this stream completes its async methods
        /// synchronously, which is what an awaiting caller observes of a fast read); DeleteOnClose removes the
        /// file when the stream is disposed. Encrypted (Windows EFS) is not supported.
        /// </summary>
        public FileStreamImpl(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
            : this(path, mode, access, share, bufferSize, CheckOptions(options), validate: true)
        {
        }

        public FileStreamImpl(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
            : this(path, mode, access, share, bufferSize, useAsync ? FileOptions.Asynchronous : FileOptions.None, validate: true)
        {
        }

        private FileStreamImpl(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options, bool validate)
            : this(path, mode, access, share, bufferSize)
        {
            _isAsync = (options & FileOptions.Asynchronous) != 0;
            _deleteOnClose = (options & FileOptions.DeleteOnClose) != 0;
        }

        // Checked before the file is opened, as FileStreamHelpers.ValidateArguments does.
        private static FileOptions CheckOptions(FileOptions options)
        {
            const FileOptions known = FileOptions.WriteThrough | FileOptions.Asynchronous | FileOptions.RandomAccess
                | FileOptions.DeleteOnClose | FileOptions.SequentialScan | FileOptions.Encrypted | (FileOptions)0x20000000 /* NoBuffering */;
            if (options != FileOptions.None && (options & ~known) != 0)
                throw new ArgumentOutOfRangeException("options", "Enum value was out of legal range.");
            if ((options & FileOptions.Encrypted) != 0 || (options & (FileOptions)0x20000000) != 0)
                throw new NotSupportedException("FileOptions." + ((options & FileOptions.Encrypted) != 0 ? "Encrypted" : "NoBuffering") + " is not supported by csharpc yet.");
            return options;
        }

        private readonly bool _isAsync, _deleteOnClose;
        public virtual bool IsAsync => _isAsync;

        public FileStreamImpl(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path.Length == 0) throw new ArgumentException("Empty path name is not legal. (Parameter 'path')");
            _path = PathImpl.GetFullPath(path);
            _canRead = (access & FileAccess.Read) != 0;
            _canWrite = (access & FileAccess.Write) != 0;
            _bufferSize = bufferSize > 1 ? bufferSize : 1;
            int flags = Posix.O_CLOEXEC | (_canRead && _canWrite ? Posix.O_RDWR : _canWrite ? Posix.O_WRONLY : Posix.O_RDONLY);
            switch (mode)
            {
                case FileMode.CreateNew: flags |= Posix.O_CREAT | Posix.O_EXCL; break;
                case FileMode.Create: flags |= Posix.O_CREAT | Posix.O_TRUNC; break;
                case FileMode.OpenOrCreate: flags |= Posix.O_CREAT; break;
                case FileMode.Truncate: flags |= Posix.O_TRUNC; break;
                case FileMode.Append: flags |= Posix.O_CREAT; _append = true; break;
            }
            byte* p = Utf8.ToNative(_path);
            _fd = Posix.open(p, flags, 0x1B6 /* 0666, minus umask */, (int)share);
            int errno = Posix.Errno;
            Libc.free(p);
            if (_fd < 0) throw Posix.IOError(errno, _path, false);
            byte* st = stackalloc byte[Posix.StatSize];
            if (Posix.fstat(_fd, st) == 0 && (Posix.Mode(st) & Posix.S_IFMT) == Posix.S_IFDIR)
            {
                Posix.close(_fd);
                _fd = -1;
                throw new UnauthorizedAccessException("Access to the path '" + _path + "' is denied.");
            }
            if (_append) Posix.lseek(_fd, 0, Posix.SEEK_END);
        }

        public string Name => _path;
        public override bool CanRead => _canRead && !_disposed;
        public override bool CanWrite => _canWrite && !_disposed;
        public override bool CanSeek => !_disposed;

        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(null, "Cannot access a closed file.");
        }

        public override long Length
        {
            get
            {
                EnsureOpen();
                FlushWrite();
                byte* st = stackalloc byte[Posix.StatSize];
                Posix.fstat(_fd, st);
                return Posix.Size(st);
            }
        }

        public override long Position
        {
            get
            {
                EnsureOpen();
                return Posix.lseek(_fd, 0, Posix.SEEK_CUR) - (_readLen - _readPos) + _writePos;
            }
            set => Seek(value, SeekOrigin.Begin);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            EnsureOpen();
            long target = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? Position + offset : Length + offset;
            if (target < 0) throw new IOException("An attempt was made to move the position before the beginning of the stream.");
            FlushWrite();
            _readPos = _readLen = 0;
            Posix.lseek(_fd, target, Posix.SEEK_SET);
            return target;
        }

        public override void SetLength(long value)
        {
            EnsureOpen();
            FlushWrite();
            DiscardRead();
            if (Posix.ftruncate(_fd, value) != 0) throw Posix.IOError(Posix.Errno, _path, false);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException("count", "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            fixed (byte* b = buffer) return ReadCore(b + offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            fixed (byte* b = buffer) return ReadCore(b, buffer.Length);
        }

        public override int ReadByte()
        {
            if (_readPos < _readLen) return _readBuffer[_readPos++];
            byte one;
            return ReadCore(&one, 1) == 0 ? -1 : one;
        }

        private int ReadCore(byte* dest, int count)
        {
            EnsureOpen();
            if (!_canRead) throw new NotSupportedException("Stream does not support reading.");
            FlushWrite();
            if (count == 0) return 0;
            int available = _readLen - _readPos;
            if (available == 0)
            {
                if (count >= _bufferSize) return RawRead(dest, count);
                _readBuffer ??= new byte[_bufferSize];
                fixed (byte* rb = _readBuffer) _readLen = RawRead(rb, _bufferSize);
                _readPos = 0;
                available = _readLen;
                if (available == 0) return 0;
            }
            int n = count < available ? count : available;
            fixed (byte* rb = _readBuffer) Libc.memcpy(dest, rb + _readPos, (nuint)n);
            _readPos += n;
            return n;
        }

        private int RawRead(byte* dest, int count)
        {
            while (true)
            {
                nint n = Posix.read(_fd, dest, (nuint)count);
                if (n >= 0) return (int)n;
                int errno = Posix.Errno;
                if (errno != Posix.EINTR) throw Posix.IOError(errno, _path, false);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException("count", "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            fixed (byte* b = buffer) WriteCore(b + offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            fixed (byte* b = buffer) WriteCore(b, buffer.Length);
        }

        public override void WriteByte(byte value) => WriteCore(&value, 1);

        private void WriteCore(byte* source, int count)
        {
            EnsureOpen();
            if (!_canWrite) throw new NotSupportedException("Stream does not support writing.");
            DiscardRead();
            if (count == 0) return;
            _writeBuffer ??= new byte[_bufferSize];
            if (_writePos + count <= _bufferSize)
            {
                fixed (byte* wb = _writeBuffer) Libc.memcpy(wb + _writePos, source, (nuint)count);
                _writePos += count;
                if (_writePos == _bufferSize) FlushWrite();
                return;
            }
            FlushWrite();
            if (count >= _bufferSize) { RawWrite(source, count); return; }
            fixed (byte* wb = _writeBuffer) Libc.memcpy(wb, source, (nuint)count);
            _writePos = count;
        }

        private void RawWrite(byte* source, int count)
        {
            while (count > 0)
            {
                nint n = Posix.write(_fd, source, (nuint)count);
                if (n < 0)
                {
                    int errno = Posix.Errno;
                    if (errno == Posix.EINTR) continue;
                    throw Posix.IOError(errno, _path, false);
                }
                source += n;
                count -= (int)n;
            }
        }

        private void FlushWrite()
        {
            if (_writePos == 0) return;
            fixed (byte* wb = _writeBuffer) RawWrite(wb, _writePos);
            _writePos = 0;
        }

        private void DiscardRead()
        {
            if (_readPos < _readLen) Posix.lseek(_fd, _readPos - _readLen, Posix.SEEK_CUR);
            _readPos = _readLen = 0;
        }

        public override void Flush()
        {
            EnsureOpen();
            FlushWrite();
        }

        public virtual void Flush(bool flushToDisk)
        {
            Flush();
            if (flushToDisk) Posix.fsync(_fd);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            try { FlushWrite(); }
            finally
            {
                _disposed = true;
                if (_fd >= 0) Posix.close(_fd);
                _fd = -1;
                if (_deleteOnClose)
                {
                    byte* p = Utf8.ToNative(_path);
                    Posix.unlink(p);
                    Libc.free(p);
                }
            }
        }
    }

    /// <summary>A read-only stream over unmanaged memory (System.Reflection.Metadata's GetStream()).</summary>
    [Shadows("System.IO.UnmanagedMemoryStream")]
    public unsafe class UnmanagedMemoryStreamImpl : StreamImpl
    {
        private byte* _memory;
        private long _length, _position;
        private bool _open;

        public UnmanagedMemoryStreamImpl(byte* pointer, long length)
        {
            if (pointer == null) throw new ArgumentNullException("pointer");
            if (length < 0) throw new ArgumentOutOfRangeException("length", "Non-negative number required.");
            _memory = pointer;
            _length = length;
            _open = true;
        }

        public override bool CanRead => _open;
        public override bool CanSeek => _open;
        public override bool CanWrite => false;

        private void EnsureOpen()
        {
            if (!_open) throw new ObjectDisposedException(null, "Cannot access a closed Stream.");
        }

        public override long Length { get { EnsureOpen(); return _length; } }
        public long Capacity { get { EnsureOpen(); return _length; } }

        public override long Position
        {
            get { EnsureOpen(); return _position; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException("value", "Non-negative number required.");
                EnsureOpen();
                _position = value;
            }
        }

        public byte* PositionPointer
        {
            get { EnsureOpen(); return _memory + _position; }
        }

        public override void Flush() => EnsureOpen();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            if ((uint)count > (uint)(buffer.Length - offset))
                throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            return Read(new Span<byte>(buffer, offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            EnsureOpen();
            long n = _length - _position;
            if (n > buffer.Length) n = buffer.Length;
            if (n <= 0) return 0;
            new ReadOnlySpan<byte>(_memory + _position, (int)n).CopyTo(buffer);
            _position += n;
            return (int)n;
        }

        public override int ReadByte()
        {
            EnsureOpen();
            if (_position >= _length) return -1;
            return _memory[_position++];
        }

        public override long Seek(long offset, SeekOrigin loc)
        {
            EnsureOpen();
            long target = loc == SeekOrigin.Begin ? offset : loc == SeekOrigin.Current ? _position + offset : _length + offset;
            if (target < 0) throw new IOException("An attempt was made to move the position before the beginning of the stream.");
            _position = target;
            return target;
        }

        public override void SetLength(long value)
        {
            EnsureOpen();
            throw new NotSupportedException("Stream does not support writing.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureOpen();
            throw new NotSupportedException("Stream does not support writing.");
        }

        protected override void Dispose(bool disposing)
        {
            _open = false;
            _memory = null;
        }
    }

    [Shadows("System.IO.MemoryStream")]
    public unsafe class MemoryStreamImpl : StreamImpl
    {
        private byte[] _buffer;
        private int _origin, _position, _length, _capacity;
        private readonly bool _expandable, _writable;
        private bool _disposed;

        public MemoryStreamImpl() : this(0) { }

        public MemoryStreamImpl(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException("capacity", "Non-negative number required.");
            _buffer = new byte[capacity];
            _capacity = capacity;
            _expandable = true;
            _writable = true;
        }

        public MemoryStreamImpl(byte[] buffer) : this(buffer, true) { }

        public MemoryStreamImpl(byte[] buffer, bool writable)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            _buffer = buffer;
            _length = _capacity = buffer.Length;
            _writable = writable;
        }

        public MemoryStreamImpl(byte[] buffer, int index, int count) : this(buffer, index, count, true) { }

        public MemoryStreamImpl(byte[] buffer, int index, int count, bool writable)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            _buffer = buffer;
            _origin = _position = index;
            _length = _capacity = index + count;
            _writable = writable;
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => _writable && !_disposed;

        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(null, "Cannot access a closed Stream.");
        }

        public virtual int Capacity
        {
            get { EnsureOpen(); return _capacity - _origin; }
            set
            {
                if (value < _length - _origin) throw new ArgumentOutOfRangeException("value", "capacity was less than the current size.");
                if (!_expandable) throw new NotSupportedException("Memory stream is not expandable.");
                if (value == _capacity) return;
                byte[] grown = new byte[value];
                Array.Copy(_buffer, 0, grown, 0, _length);
                _buffer = grown;
                _capacity = value;
            }
        }

        public override long Length { get { EnsureOpen(); return _length - _origin; } }

        public override long Position
        {
            get { EnsureOpen(); return _position - _origin; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException("value", "Non-negative number required.");
                _position = _origin + (int)value;
            }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureOpen();
            if (buffer == null) throw new ArgumentNullException("buffer");
            int n = _length - _position;
            if (n > count) n = count;
            if (n <= 0) return 0;
            Array.Copy(_buffer, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            EnsureOpen();
            int n = _length - _position;
            if (n > buffer.Length) n = buffer.Length;
            if (n <= 0) return 0;
            for (int i = 0; i < n; i++) buffer[i] = _buffer[_position + i];
            _position += n;
            return n;
        }

        public override int ReadByte()
        {
            EnsureOpen();
            return _position >= _length ? -1 : _buffer[_position++];
        }

        public override long Seek(long offset, SeekOrigin loc)
        {
            EnsureOpen();
            long target = loc == SeekOrigin.Begin ? _origin + offset : loc == SeekOrigin.Current ? _position + offset : _length + offset;
            if (target < _origin) throw new IOException("An attempt was made to move the position before the beginning of the stream.");
            _position = (int)target;
            return _position - _origin;
        }

        public override void SetLength(long value)
        {
            EnsureOpen();
            int newLength = _origin + (int)value;
            EnsureCapacity(newLength);
            if (newLength > _length) Array.Clear(_buffer, _length, newLength - _length);
            _length = newLength;
            if (_position > newLength) _position = newLength;
        }

        private void EnsureCapacity(int value)
        {
            if (value <= _capacity) return;
            if (!_expandable) throw new NotSupportedException("Memory stream is not expandable.");
            int newCapacity = value < 256 ? 256 : value;
            if (newCapacity < _capacity * 2) newCapacity = _capacity * 2;
            Capacity = newCapacity;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureOpen();
            if (!_writable) throw new NotSupportedException("Stream does not support writing.");
            if (buffer == null) throw new ArgumentNullException("buffer");
            int end = _position + count;
            EnsureCapacity(end);
            if (_position > _length) Array.Clear(_buffer, _length, _position - _length);
            Array.Copy(buffer, offset, _buffer, _position, count);
            _position = end;
            if (end > _length) _length = end;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            byte[] temp = buffer.ToArray();
            Write(temp, 0, temp.Length);
        }

        public override void WriteByte(byte value)
        {
            EnsureOpen();
            if (!_writable) throw new NotSupportedException("Stream does not support writing.");
            EnsureCapacity(_position + 1);
            if (_position > _length) Array.Clear(_buffer, _length, _position - _length);
            _buffer[_position++] = value;
            if (_position > _length) _length = _position;
        }

        public virtual byte[] ToArray()
        {
            byte[] copy = new byte[_length - _origin];
            Array.Copy(_buffer, _origin, copy, 0, copy.Length);
            return copy;
        }

        public virtual byte[] GetBuffer() => _buffer;

        public virtual void WriteTo(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            stream.Write(_buffer, _origin, _length - _origin);
        }

        // MemoryStream completes its asynchronous operations synchronously, as .NET's does.
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<int>(cancellationToken);
            try { return Task.FromResult(Read(buffer, offset, count)); }
            catch (OperationCanceledException) { return Task.FromCanceled<int>(cancellationToken); }
            catch (Exception e) { return Task.FromException<int>(e); }
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            try { return new ValueTask<int>(Read(buffer.Span)); }
            catch (OperationCanceledException) { return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken)); }
            catch (Exception e) { return ValueTask.FromException<int>(e); }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            try
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
            catch (OperationCanceledException) { return Task.FromCanceled(cancellationToken); }
            catch (Exception e) { return Task.FromException(e); }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return new ValueTask(Task.FromCanceled(cancellationToken));
            try
            {
                Write(buffer.Span);
                return default;
            }
            catch (OperationCanceledException) { return new ValueTask(Task.FromCanceled(cancellationToken)); }
            catch (Exception e) { return ValueTask.FromException(e); }
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            try
            {
                Flush();
                return Task.CompletedTask;
            }
            catch (Exception e) { return Task.FromException(e); }
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            // A derived class may have overridden Read: the base implementation then reads through it.
            if (GetType() != typeof(MemoryStream)) return base.CopyToAsync(destination, bufferSize, cancellationToken);
            ValidateCopyToArguments(destination, bufferSize);
            EnsureOpen();
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            int start = _position;
            int n = _length > _position ? _length - _position : 0;
            _position += n;
            if (n == 0) return Task.CompletedTask;
            try { return destination.WriteAsync(new ReadOnlyMemory<byte>(_buffer, start, n), cancellationToken).AsTask(); }
            catch (Exception e) { return Task.FromException(e); }
        }

        protected override void Dispose(bool disposing) { _disposed = true; }
    }

    [Shadows("System.IO.BinaryWriter")]
    public unsafe class BinaryWriterImpl : IDisposable
    {
        private readonly Stream _stream;
        private readonly Encoding _encoding;
        private readonly bool _leaveOpen;

        public BinaryWriterImpl(Stream output) : this(output, new UTF8Encoding(false), false) { }
        public BinaryWriterImpl(Stream output, Encoding encoding) : this(output, encoding, false) { }
        public BinaryWriterImpl(Stream output, Encoding encoding, bool leaveOpen)
        {
            if (output == null) throw new ArgumentNullException("output");
            _stream = output;
            _encoding = encoding;
            _leaveOpen = leaveOpen;
        }

        public virtual Stream BaseStream => _stream;

        private void Raw(byte* p, int n)
        {
            byte[] b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = p[i];
            _stream.Write(b, 0, n);
        }

        public virtual void Write(bool value) => _stream.WriteByte(value ? (byte)1 : (byte)0);
        public virtual void Write(byte value) => _stream.WriteByte(value);
        public virtual void Write(sbyte value) => _stream.WriteByte((byte)value);
        public virtual void Write(byte[] buffer) => _stream.Write(buffer, 0, buffer.Length);
        public virtual void Write(byte[] buffer, int index, int count) => _stream.Write(buffer, index, count);
        public virtual void Write(short value) => Raw((byte*)&value, 2);
        public virtual void Write(ushort value) => Raw((byte*)&value, 2);
        public virtual void Write(int value) => Raw((byte*)&value, 4);
        public virtual void Write(uint value) => Raw((byte*)&value, 4);
        public virtual void Write(long value) => Raw((byte*)&value, 8);
        public virtual void Write(ulong value) => Raw((byte*)&value, 8);
        public virtual void Write(float value) => Raw((byte*)&value, 4);
        public virtual void Write(double value) => Raw((byte*)&value, 8);
        public virtual void Write(char ch)
        {
            byte[] bytes = _encoding.GetBytes(new string(ch, 1));
            _stream.Write(bytes, 0, bytes.Length);
        }

        public virtual void Write(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            byte[] bytes = _encoding.GetBytes(value);
            Write7BitEncodedInt(bytes.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void Write7BitEncodedInt(int value)
        {
            uint v = (uint)value;
            while (v >= 0x80) { _stream.WriteByte((byte)(v | 0x80)); v >>= 7; }
            _stream.WriteByte((byte)v);
        }

        public virtual void Flush() => _stream.Flush();
        public virtual long Seek(int offset, SeekOrigin origin) => _stream.Seek(offset, origin);
        public void Dispose() => Dispose(true);
        public virtual void Close() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (_leaveOpen) _stream.Flush();
            else _stream.Dispose();
        }
    }

    [Shadows("System.IO.BinaryReader")]
    public unsafe class BinaryReaderImpl : IDisposable
    {
        private readonly Stream _stream;
        private readonly Encoding _encoding;
        private readonly bool _leaveOpen;
        private readonly byte[] _scratch = new byte[16];

        public BinaryReaderImpl(Stream input) : this(input, new UTF8Encoding(false), false) { }
        public BinaryReaderImpl(Stream input, Encoding encoding) : this(input, encoding, false) { }
        public BinaryReaderImpl(Stream input, Encoding encoding, bool leaveOpen)
        {
            if (input == null) throw new ArgumentNullException("input");
            _stream = input;
            _encoding = encoding;
            _leaveOpen = leaveOpen;
        }

        public virtual Stream BaseStream => _stream;

        private byte* Fill(int n)
        {
            int read = 0;
            while (read < n)
            {
                int r = _stream.Read(_scratch, read, n - read);
                if (r == 0) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                read += r;
            }
            fixed (byte* p = _scratch) return p; // _scratch lives as long as the reader (GC never moves it)
        }

        public virtual bool ReadBoolean() => *Fill(1) != 0;
        public virtual byte ReadByte()
        {
            int b = _stream.ReadByte();
            if (b < 0) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
            return (byte)b;
        }
        public virtual sbyte ReadSByte() => (sbyte)ReadByte();
        public virtual short ReadInt16() => *(short*)Fill(2);
        public virtual ushort ReadUInt16() => *(ushort*)Fill(2);
        public virtual int ReadInt32() => *(int*)Fill(4);
        public virtual uint ReadUInt32() => *(uint*)Fill(4);
        public virtual long ReadInt64() => *(long*)Fill(8);
        public virtual ulong ReadUInt64() => *(ulong*)Fill(8);
        public virtual float ReadSingle() => *(float*)Fill(4);
        public virtual double ReadDouble() => *(double*)Fill(8);

        public virtual byte[] ReadBytes(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            byte[] result = new byte[count];
            int read = 0;
            while (read < count)
            {
                int r = _stream.Read(result, read, count - read);
                if (r == 0) break;
                read += r;
            }
            if (read == count) return result;
            byte[] shorter = new byte[read];
            Array.Copy(result, shorter, read);
            return shorter;
        }

        public int Read7BitEncodedInt()
        {
            uint result = 0;
            int shift = 0;
            byte b;
            do
            {
                if (shift == 35) throw new FormatException("Too many bytes in what should have been a 7-bit encoded integer.");
                b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return (int)result;
        }

        public virtual string ReadString()
        {
            int length = Read7BitEncodedInt();
            if (length < 0) throw new IOException("BinaryReader encountered an invalid string length of " + length + " characters.");
            if (length == 0) return "";
            byte[] bytes = ReadBytes(length);
            if (bytes.Length < length) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
            return _encoding.GetString(bytes);
        }

        public virtual char ReadChar()
        {
            byte first = ReadByte();
            if (first < 0x80) return (char)first;
            int extra = first >= 0xF0 ? 3 : first >= 0xE0 ? 2 : 1;
            byte[] bytes = new byte[1 + extra];
            bytes[0] = first;
            for (int i = 1; i <= extra; i++) bytes[i] = ReadByte();
            return _encoding.GetString(bytes)[0];
        }

        public virtual char[] ReadChars(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            char[] result = new char[count];
            for (int i = 0; i < count; i++)
            {
                if (_stream.CanSeek && _stream.Position >= _stream.Length)
                {
                    char[] shorter = new char[i];
                    Array.Copy(result, shorter, i);
                    return shorter;
                }
                result[i] = ReadChar();
            }
            return result;
        }

        public virtual int PeekChar()
        {
            if (!_stream.CanSeek) return -1;
            int b = _stream.ReadByte();
            if (b >= 0) _stream.Seek(-1, SeekOrigin.Current);
            return b;
        }

        public void Dispose() => Dispose(true);
        public virtual void Close() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (!_leaveOpen) _stream.Dispose();
        }
    }
}
