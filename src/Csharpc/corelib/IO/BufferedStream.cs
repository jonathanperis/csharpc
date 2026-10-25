// System.IO.BufferedStream, ported from dotnet/runtime (src/libraries/System.Private.CoreLib/src/System/IO/BufferedStream.cs,
// release/11.0): the same buffering, flushing and seeking decisions, so a derived or wrapped stream sees the
// same calls in the same order (since .NET 10, WriteByte no longer flushes when it fills the buffer).
#nullable disable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.BufferedStream")]
    public sealed class BufferedStreamImpl : StreamImpl
    {
        /// <summary><code>MaxShadowBufferSize</code> is chosen such that shadow buffers are not allocated on the Large Object Heap.
        /// Currently, an object is allocated on the LOH if it is larger than 85000 bytes. See LARGE_OBJECT_SIZE in src/gc/gc.h
        /// We will go with exactly 80 Kbytes, although this is somewhat arbitrary.</summary>
        private const int MaxShadowBufferSize = 81920;  // Make sure not to get to the Large Object Heap.
        private const int DefaultBufferSize = 4096;

        private Stream _stream;                                     // Underlying stream.  Close sets _stream to null.
        private byte[] _buffer;                                     // Shared read/write buffer.  Alloc on first use.
        private readonly int _bufferSize;                            // Length of internal buffer (not counting the shadow buffer).
        private int _readPos;                                        // Read pointer within shared buffer.
        private int _readLen;                                        // Number of bytes read in buffer from _stream.
        private int _writePos;                                       // Write pointer within shared buffer.
        private AsyncGate _asyncActiveSemaphore;

        private AsyncGate EnsureAsyncActiveSemaphoreInitialized() => _asyncActiveSemaphore ??= new AsyncGate();

        // (.NET caches the last completed Task<int> of ReadAsync here; a new one per call behaves the same.)
        // // The last successful Task returned from ReadAsync
                                                                     // (perf optimization for successive reads of the same size)

        public BufferedStreamImpl(Stream stream)
            : this(stream, DefaultBufferSize)
        {
        }

        public BufferedStreamImpl(Stream stream, int bufferSize)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize), "'bufferSize' must be greater than zero.");

            _stream = stream;
            _bufferSize = bufferSize;

            // Allocate _buffer on its first use - it will not be used if all reads
            // & writes are greater than or equal to buffer size.

            if (!_stream.CanRead && !_stream.CanWrite)
                throw new ObjectDisposedException(null, "Cannot access a closed Stream.");
        }

        private void EnsureNotClosed()
        {
            if (_stream == null)
                throw new ObjectDisposedException(null, "Cannot access a closed Stream.");
        }

        private void EnsureCanSeek()
        {

            if (!_stream.CanSeek)
                throw new NotSupportedException("Stream does not support seeking.");
        }

        private void EnsureCanRead()
        {

            if (!_stream.CanRead)
                throw new NotSupportedException("Stream does not support reading.");
        }

        private void EnsureCanWrite()
        {

            if (!_stream.CanWrite)
                throw new NotSupportedException("Stream does not support writing.");
        }

        private void EnsureShadowBufferAllocated()
        {

            // Already have a shadow buffer?
            // Or is the user-specified buffer size already so large that we don't want to create one?
            if (_buffer.Length != _bufferSize || _bufferSize >= MaxShadowBufferSize)
                return;

            byte[] shadowBuffer = new byte[Math.Min(_bufferSize + _bufferSize, MaxShadowBufferSize)];
            Buffer.BlockCopy(_buffer, 0, shadowBuffer, 0, _writePos);
            _buffer = shadowBuffer;
        }

        private void EnsureBufferAllocated()
        {

            // BufferedStream is not intended for multi-threaded use, so no worries about the get/set race on _buffer.
            _buffer ??= new byte[_bufferSize];
        }

        public Stream UnderlyingStream
        {
            get
            {
                // _stream can be null when disposed. However we don't want to make UnderlyingStream nullable just for that scenario, since doing operations after dispose is invalid anyway.
                return _stream;
            }
        }

        public int BufferSize
        {
            get
            {
                return _bufferSize;
            }
        }

        public override bool CanRead
        {
            get
            {
                return _stream != null && _stream.CanRead;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return _stream != null && _stream.CanWrite;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return _stream != null && _stream.CanSeek;
            }
        }

        public override long Length
        {
            get
            {
                EnsureNotClosed();

                if (_writePos > 0)
                    FlushWrite();

                return _stream.Length;
            }
        }

        public override long Position
        {
            get
            {
                EnsureNotClosed();
                EnsureCanSeek();

                return _stream.Position + (_readPos - _readLen + _writePos);
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "Non-negative number required.");

                Seek(value, SeekOrigin.Begin);
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && _stream != null)
                {
                    try
                    {
                        Flush();
                    }
                    finally
                    {
                        _stream.Dispose();
                    }
                }
            }
            finally
            {
                _stream = null;
                _buffer = null;
                _writePos = 0; // WriteByte hot path relies on this

                // Call base.Dispose(bool) to cleanup async IO resources
                base.Dispose(disposing);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (_stream != null)
                {
                    try
                    {
                        await FlushAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await _stream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _stream = null;
                _buffer = null;
                _writePos = 0;
            }
        }

        public override void Flush()
        {
            EnsureNotClosed();

            // Has write data in the buffer:
            if (_writePos > 0)
            {
                FlushWrite();
                return;
            }

            // Has read data in the buffer:
            if (_readPos < _readLen)
            {
                // If the underlying stream is not seekable AND we have something in the read buffer, then FlushRead would throw.
                // We can either throw away the buffer resulting in data loss (!) or ignore the Flush.
                // (We cannot throw because it would be a breaking change.) We opt into ignoring the Flush in that situation.
                if (_stream.CanSeek)
                {
                    FlushRead();
                }

                // User streams may have opted to throw from Flush if CanWrite is false (although the abstract Stream does not do so).
                // However, if we do not forward the Flush to the underlying stream, we may have problems when chaining several streams.
                // Let us make a best effort attempt:
                if (_stream.CanWrite)
                    _stream.Flush();

                // If the Stream was seekable, then we should have called FlushRead which resets _readPos & _readLen.
                return;
            }

            // We had no data in the buffer, but we still need to tell the underlying stream to flush.
            if (_stream.CanWrite)
                _stream.Flush();

            _writePos = _readPos = _readLen = 0;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<int>(cancellationToken);

            EnsureNotClosed();

            return FlushAsyncInternal(cancellationToken);
        }

        private async Task FlushAsyncInternal(CancellationToken cancellationToken)
        {

            await EnsureAsyncActiveSemaphoreInitialized().WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_writePos > 0)
                {
                    await FlushWriteAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_readPos < _readLen)
                {
                    // If the underlying stream is not seekable AND we have something in the read buffer, then FlushRead would throw.
                    // We can either throw away the buffer resulting in date loss (!) or ignore the Flush. (We cannot throw because it
                    // would be a breaking change.) We opt into ignoring the Flush in that situation.
                    if (_stream.CanSeek)
                    {
                        FlushRead();  // not async; it uses Seek, but there's no SeekAsync
                    }

                    // User streams may have opted to throw from Flush if CanWrite is false (although the abstract Stream does not do so).
                    // However, if we do not forward the Flush to the underlying stream, we may have problems when chaining several streams.
                    // Let us make a best effort attempt:
                    if (_stream.CanWrite)
                        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                    // If the Stream was seekable, then we should have called FlushRead which resets _readPos & _readLen.
                    return;
                }

                // We had no data in the buffer, but we still need to tell the underlying stream to flush.
                if (_stream.CanWrite)
                    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                // There was nothing in the buffer:

            }
            finally
            {
                _asyncActiveSemaphore.Release();
            }
        }

        // Reading is done in blocks, but someone could read 1 byte from the buffer then write.
        // At that point, the underlying stream's pointer is out of sync with this stream's position.
        // All write functions should call this function to ensure that the buffered data is not lost.
        private void FlushRead()
        {

            if (_readPos - _readLen != 0)
                _stream.Seek(_readPos - _readLen, SeekOrigin.Current);

            _readPos = 0;
            _readLen = 0;
        }

        /// <summary>
        /// Called by Write methods to clear the Read Buffer
        /// </summary>
        private void ClearReadBufferBeforeWrite()
        {

            // No read data in the buffer:
            if (_readPos == _readLen)
            {
                _readPos = _readLen = 0;
                return;
            }

            // Must have read data.

            // If the underlying stream cannot seek, FlushRead would end up throwing NotSupported.
            // However, since the user did not call a method that is intuitively expected to seek, a better message is in order.
            // Ideally, we would throw an InvalidOperation here, but for backward compat we have to stick with NotSupported.
            if (!_stream.CanSeek)
                throw new NotSupportedException("Cannot write to a BufferedStream while the read buffer is not empty if the underlying stream is not seekable. Ensure that the stream underlying this BufferedStream can seek or avoid interleaving read and write operations on this BufferedStream.");

            FlushRead();
        }

        private void FlushWrite()
        {

            _stream.Write(_buffer, 0, _writePos);
            _writePos = 0;
            _stream.Flush();
        }

        private async ValueTask FlushWriteAsync(CancellationToken cancellationToken)
        {

            await _stream.WriteAsync(new ReadOnlyMemory<byte>(_buffer, 0, _writePos), cancellationToken).ConfigureAwait(false);
            _writePos = 0;
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private int ReadFromBuffer(byte[] buffer, int offset, int count)
        {
            int readbytes = _readLen - _readPos;

            if (readbytes == 0)
                return 0;

            if (readbytes > count)
                readbytes = count;
            Buffer.BlockCopy(_buffer, _readPos, buffer, offset, readbytes);
            _readPos += readbytes;

            return readbytes;
        }

        private int ReadFromBuffer(Span<byte> destination)
        {
            int readbytes = Math.Min(_readLen - _readPos, destination.Length);
            if (readbytes > 0)
            {
                new ReadOnlySpan<byte>(_buffer, _readPos, readbytes).CopyTo(destination);
                _readPos += readbytes;
            }
            return readbytes;
        }

        private int ReadFromBuffer(byte[] buffer, int offset, int count, out Exception error)
        {
            try
            {
                error = null;
                return ReadFromBuffer(buffer, offset, count);
            }
            catch (Exception ex)
            {
                error = ex;
                return 0;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            EnsureNotClosed();
            EnsureCanRead();

            int bytesFromBuffer = ReadFromBuffer(buffer, offset, count);

            // We may have read less than the number of bytes the user asked for, but that is part of the Stream Debug.

            // Reading again for more data may cause us to block if we're using a device with no clear end of file,
            // such as a serial port or pipe. If we blocked here and this code was used with redirected pipes for a
            // process's standard output, this can lead to deadlocks involving two processes.
            // BUT - this is a breaking change.
            // So: If we could not read all bytes the user asked for from the buffer, we will try once from the underlying
            // stream thus ensuring the same blocking behaviour as if the underlying stream was not wrapped in this BufferedStream.
            if (bytesFromBuffer == count)
                return bytesFromBuffer;

            int alreadySatisfied = bytesFromBuffer;
            if (bytesFromBuffer > 0)
            {
                count -= bytesFromBuffer;
                offset += bytesFromBuffer;
            }

            _readPos = _readLen = 0;

            // If there was anything in the write buffer, clear it.
            if (_writePos > 0)
                FlushWrite();

            // If the requested read is larger than buffer size, avoid the buffer and still use a single read:
            if (count >= _bufferSize)
            {
                return _stream.Read(buffer, offset, count) + alreadySatisfied;
            }

            // Ok. We can fill the buffer:
            EnsureBufferAllocated();
            _readLen = _stream.Read(_buffer, 0, _bufferSize);

            bytesFromBuffer = ReadFromBuffer(buffer, offset, count);

            // We may have read less than the number of bytes the user asked for, but that is part of the Stream Debug.
            // Reading again for more data may cause us to block if we're using a device with no clear end of stream,
            // such as a serial port or pipe.  If we blocked here & this code was used with redirected pipes for a process's
            // standard output, this can lead to deadlocks involving two processes. Additionally, translating one read on the
            // BufferedStream to more than one read on the underlying Stream may defeat the whole purpose of buffering of the
            // underlying reads are significantly more expensive.

            return bytesFromBuffer + alreadySatisfied;
        }

        public override int Read(Span<byte> destination)
        {
            EnsureNotClosed();
            EnsureCanRead();

            // Try to read from the buffer.
            int bytesFromBuffer = ReadFromBuffer(destination);
            if (bytesFromBuffer == destination.Length)
            {
                // We got as many bytes as were asked for; we're done.
                return bytesFromBuffer;
            }

            // We didn't get as many bytes as were asked for from the buffer, so try filling the buffer once.

            if (bytesFromBuffer > 0)
            {
                destination = destination.Slice(bytesFromBuffer);
            }

            _readPos = _readLen = 0;

            // If there was anything in the write buffer, clear it.
            if (_writePos > 0)
            {
                FlushWrite();
            }

            if (destination.Length >= _bufferSize)
            {
                // If the requested read is larger than buffer size, avoid the buffer and just read
                // directly into the destination.
                return _stream.Read(destination) + bytesFromBuffer;
            }
            else
            {
                // Otherwise, fill the buffer, then read from that.
                EnsureBufferAllocated();
                _readLen = _stream.Read(_buffer, 0, _bufferSize);
                return ReadFromBuffer(destination) + bytesFromBuffer;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);

            // Fast path check for cancellation already requested
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<int>(cancellationToken);

            EnsureNotClosed();
            EnsureCanRead();

            int bytesFromBuffer = 0;
            // Try to satisfy the request from the buffer synchronously. But still need a sem-lock in case that another
            // Async IO Task accesses the buffer concurrently. If we fail to acquire the lock without waiting, make this
            // an Async operation.
            AsyncGate sem = EnsureAsyncActiveSemaphoreInitialized();
            Task semaphoreLockTask = sem.WaitAsync(cancellationToken);
            if (semaphoreLockTask.IsCompletedSuccessfully)
            {
                bool completeSynchronously = true;
                try
                {
                    Exception error;
                    bytesFromBuffer = ReadFromBuffer(buffer, offset, count, out error);

                    // If we satisfied enough data from the buffer, we can complete synchronously.
                    // Reading again for more data may cause us to block if we're using a device with no clear end of file,
                    // such as a serial port or pipe. If we blocked here and this code was used with redirected pipes for a
                    // process's standard output, this can lead to deadlocks involving two processes.
                    // BUT - this is a breaking change.
                    // So: If we could not read all bytes the user asked for from the buffer, we will try once from the underlying
                    // stream thus ensuring the same blocking behaviour as if the underlying stream was not wrapped in this BufferedStream.
                    completeSynchronously = (bytesFromBuffer == count || error != null);

                    if (completeSynchronously)
                    {

                        return (error == null)
                                    ? Task.FromResult(bytesFromBuffer)
                                    : Task.FromException<int>(error);
                    }
                }
                finally
                {
                    if (completeSynchronously)  // if this is FALSE, we will be entering ReadFromUnderlyingStreamAsync and releasing there.
                        sem.Release();
                }
            }

            // Delegate to the async implementation.
            return ReadFromUnderlyingStreamAsync(
                new Memory<byte>(buffer, offset + bytesFromBuffer, count - bytesFromBuffer),
                cancellationToken, bytesFromBuffer, semaphoreLockTask).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            EnsureNotClosed();
            EnsureCanRead();

            int bytesFromBuffer = 0;
            AsyncGate sem = EnsureAsyncActiveSemaphoreInitialized();
            Task semaphoreLockTask = sem.WaitAsync(cancellationToken);
            if (semaphoreLockTask.IsCompletedSuccessfully)
            {
                bool completeSynchronously = true;
                try
                {
                    bytesFromBuffer = ReadFromBuffer(buffer.Span);
                    completeSynchronously = bytesFromBuffer == buffer.Length;
                    if (completeSynchronously)
                    {
                        // If we satisfied enough data from the buffer, we can complete synchronously.
                        return new ValueTask<int>(bytesFromBuffer);
                    }
                }
                finally
                {
                    if (completeSynchronously)  // if this is FALSE, we will be entering ReadFromUnderlyingStreamAsync and releasing there.
                    {
                        sem.Release();
                    }
                }
            }

            // Delegate to the async implementation.
            return ReadFromUnderlyingStreamAsync(buffer.Slice(bytesFromBuffer), cancellationToken, bytesFromBuffer, semaphoreLockTask);
        }

        /// <summary>BufferedStream should be as thin a wrapper as possible. We want ReadAsync to delegate to
        /// ReadAsync of the underlying _stream rather than calling the base Stream which implements the one in terms of the other.
        /// This allows BufferedStream to affect the semantics of the stream it wraps as little as possible. </summary>
        /// <returns>-2 if _bufferSize was set to 0 while waiting on the semaphore; otherwise num of bytes read.</returns>
        private async ValueTask<int> ReadFromUnderlyingStreamAsync(
            Memory<byte> buffer, CancellationToken cancellationToken, int bytesAlreadySatisfied, Task semaphoreLockTask)
        {
            // Same conditions validated with exceptions in ReadAsync:

            // Employ async waiting based on the same synchronization used in BeginRead of the abstract Stream.
            await semaphoreLockTask.ConfigureAwait(false);
            try
            {
                // The buffer might have been changed by another async task while we were waiting on the semaphore.
                // Check it now again.
                int bytesFromBuffer = ReadFromBuffer(buffer.Span);
                if (bytesFromBuffer == buffer.Length)
                {
                    return bytesAlreadySatisfied + bytesFromBuffer;
                }

                if (bytesFromBuffer > 0)
                {
                    buffer = buffer.Slice(bytesFromBuffer);
                    bytesAlreadySatisfied += bytesFromBuffer;
                }

                _readPos = _readLen = 0;

                // If there was anything in the write buffer, clear it.
                if (_writePos > 0)
                {
                    await FlushWriteAsync(cancellationToken).ConfigureAwait(false);  // no Begin-End read version for Flush. Use Async.
                }

                // If the requested read is larger than buffer size, avoid the buffer and still use a single read:
                if (buffer.Length >= _bufferSize)
                {
                    return await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) + bytesAlreadySatisfied;
                }

                // Ok. We can fill the buffer:
                EnsureBufferAllocated();
                _readLen = await _stream.ReadAsync(new Memory<byte>(_buffer, 0, _bufferSize), cancellationToken).ConfigureAwait(false);

                bytesFromBuffer = ReadFromBuffer(buffer.Span);
                return bytesAlreadySatisfied + bytesFromBuffer;
            }
            finally
            {
                _asyncActiveSemaphore.Release();
            }
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
            System.Threading.Tasks.TaskToAsyncResult.Begin(ReadAsync(buffer, offset, count, CancellationToken.None), callback, state);

        public override int EndRead(IAsyncResult asyncResult) =>
            System.Threading.Tasks.TaskToAsyncResult.End<int>(asyncResult);

        public override int ReadByte()
        {
            return _readPos != _readLen ?
                _buffer[_readPos++] :
                ReadByteSlow();
        }

        private int ReadByteSlow()
        {

            // We want to check for whether the underlying stream has been closed and whether
            // it's readable, but we only need to do so if we don't have data in our buffer,
            // as any data we have came from reading it from an open stream, and we don't
            // care if the stream has been closed or become unreadable since. Further, if
            // the stream is closed, its read buffer is flushed, so we'll take this slow path.
            EnsureNotClosed();
            EnsureCanRead();

            if (_writePos > 0)
                FlushWrite();

            EnsureBufferAllocated();
            _readLen = _stream.Read(_buffer, 0, _bufferSize);
            _readPos = 0;

            if (_readLen == 0)
                return -1;

            return _buffer[_readPos++];
        }

        private void WriteToBuffer(byte[] buffer, ref int offset, ref int count)
        {
            int bytesToWrite = Math.Min(_bufferSize - _writePos, count);

            if (bytesToWrite <= 0)
                return;

            EnsureBufferAllocated();
            Buffer.BlockCopy(buffer, offset, _buffer, _writePos, bytesToWrite);

            _writePos += bytesToWrite;
            count -= bytesToWrite;
            offset += bytesToWrite;
        }

        private int WriteToBuffer(ReadOnlySpan<byte> buffer)
        {
            int bytesToWrite = Math.Min(_bufferSize - _writePos, buffer.Length);
            if (bytesToWrite > 0)
            {
                EnsureBufferAllocated();
                buffer.Slice(0, bytesToWrite).CopyTo(new Span<byte>(_buffer, _writePos, bytesToWrite));
                _writePos += bytesToWrite;
            }
            return bytesToWrite;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            EnsureNotClosed();
            EnsureCanWrite();

            if (_writePos == 0)
                ClearReadBufferBeforeWrite();

            #region Write algorithm comment
            // We need to use the buffer, while avoiding unnecessary buffer usage / memory copies.
            // We ASSUME that memory copies are much cheaper than writes to the underlying stream, so if an extra copy is
            // guaranteed to reduce the number of writes, we prefer it.
            // We pick a simple strategy that makes degenerate cases rare if our assumptions are right.
            //
            // For ever write, we use a simple heuristic (below) to decide whether to use the buffer.
            // The heuristic has the desirable property (*) that if the specified user data can fit into the currently available
            // buffer space without filling it up completely, the heuristic will always tell us to use the buffer. It will also
            // tell us to use the buffer in cases where the current write would fill the buffer, but the remaining data is small
            // enough such that subsequent operations can use the buffer again.
            //
            // Algorithm:
            // Determine whether or not to buffer according to the heuristic (below).
            // If we decided to use the buffer:
            //     Copy as much user data as we can into the buffer.
            //     If we consumed all data: We are finished.
            //     Otherwise, write the buffer out.
            //     Copy the rest of user data into the now cleared buffer (no need to write out the buffer again as the heuristic
            //     will prevent it from being filled twice).
            // If we decided not to use the buffer:
            //     Can the data already in the buffer and current user data be combines to a single write
            //     by allocating a "shadow" buffer of up to twice the size of _bufferSize (up to a limit to avoid LOH)?
            //     Yes, it can:
            //         Allocate a larger "shadow" buffer and ensure the buffered  data is moved there.
            //         Copy user data to the shadow buffer.
            //         Write shadow buffer to the underlying stream in a single operation.
            //     No, it cannot (amount of data is still too large):
            //         Write out any data possibly in the buffer.
            //         Write out user data directly.
            //
            // Heuristic:
            // If the subsequent write operation that follows the current write operation will result in a write to the
            // underlying stream in case that we use the buffer in the current write, while it would not have if we avoided
            // using the buffer in the current write (by writing current user data to the underlying stream directly), then we
            // prefer to avoid using the buffer since the corresponding memory copy is wasted (it will not reduce the number
            // of writes to the underlying stream, which is what we are optimising for).
            // ASSUME that the next write will be for the same amount of bytes as the current write (most common case) and
            // determine if it will cause a write to the underlying stream. If the next write is actually larger, our heuristic
            // still yields the right behaviour, if the next write is actually smaller, we may making an unnecessary write to
            // the underlying stream. However, this can only occur if the current write is larger than half the buffer size and
            // we will recover after one iteration.
            // We have:
            //     useBuffer = (_writePos + count + count < _bufferSize + _bufferSize)
            //
            // Example with _bufferSize = 20, _writePos = 6, count = 10:
            //
            //     +---------------------------------------+---------------------------------------+
            //     |             current buffer            | next iteration's "future" buffer      |
            //     +---------------------------------------+---------------------------------------+
            //     |0| | | | | | | | | |1| | | | | | | | | |2| | | | | | | | | |3| | | | | | | | | |
            //     |0|1|2|3|4|5|6|7|8|9|0|1|2|3|4|5|6|7|8|9|0|1|2|3|4|5|6|7|8|9|0|1|2|3|4|5|6|7|8|9|
            //     +-----------+-------------------+-------------------+---------------------------+
            //     | _writePos |  current count    | assumed next count|avail buff after next write|
            //     +-----------+-------------------+-------------------+---------------------------+
            //
            // A nice property (*) of this heuristic is that it will always succeed if the user data completely fits into the
            // available buffer, i.e. if count < (_bufferSize - _writePos).
            #endregion Write algorithm comment


            int totalUserbytes;
            bool useBuffer;
            checked
            {  // We do not expect buffer sizes big enough for an overflow, but if it happens, lets fail early:
                totalUserbytes = _writePos + count;
                // Allow current totalUserbytes up to int.MaxValue by using uint arithmetic operation for totalUserbytes + count
                useBuffer = ((uint)totalUserbytes + count < (_bufferSize + _bufferSize));
            }

            if (useBuffer)
            {
                WriteToBuffer(buffer, ref offset, ref count);

                if (_writePos < _bufferSize)
                {
                    return;
                }


                _stream.Write(_buffer, 0, _writePos);
                _writePos = 0;

                WriteToBuffer(buffer, ref offset, ref count);

            }
            else
            {  // if (!useBuffer)
               // Write out the buffer if necessary.
                if (_writePos > 0)
                {

                    // Try avoiding extra write to underlying stream by combining previously buffered data with current user data:
                    if (totalUserbytes <= (_bufferSize + _bufferSize) && totalUserbytes <= MaxShadowBufferSize)
                    {
                        EnsureShadowBufferAllocated();
                        Buffer.BlockCopy(buffer, offset, _buffer, _writePos, count);
                        _stream.Write(_buffer, 0, totalUserbytes);
                        _writePos = 0;
                        return;
                    }

                    _stream.Write(_buffer, 0, _writePos);
                    _writePos = 0;
                }

                // Write out user data.
                _stream.Write(buffer, offset, count);
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureNotClosed();
            EnsureCanWrite();

            if (_writePos == 0)
            {
                ClearReadBufferBeforeWrite();
            }

            int totalUserbytes;
            bool useBuffer;
            checked
            {
                // We do not expect buffer sizes big enough for an overflow, but if it happens, lets fail early:
                totalUserbytes = _writePos + buffer.Length;
                // Allow current totalUserbytes up to int.MaxValue by using uint arithmetic operation for totalUserbytes + buffer.Length
                useBuffer = ((uint)totalUserbytes + buffer.Length < (_bufferSize + _bufferSize));
            }

            if (useBuffer)
            {
                // Copy as much data to the buffer as will fit.  If there's still room in the buffer,
                // everything must have fit.
                int bytesWritten = WriteToBuffer(buffer);
                if (_writePos < _bufferSize)
                {
                    return;
                }
                buffer = buffer.Slice(bytesWritten);


                // Output the buffer to the underlying stream.
                _stream.Write(_buffer, 0, _writePos);
                _writePos = 0;

                // Now write the remainder.  It must fit, as we're only on this path if that's true.
                bytesWritten = WriteToBuffer(buffer);

            }
            else // skip the buffer
            {
                // Flush anything existing in the buffer.
                if (_writePos > 0)
                {

                    // Try avoiding extra write to underlying stream by combining previously buffered data with current user data:
                    if (totalUserbytes <= (_bufferSize + _bufferSize) && totalUserbytes <= MaxShadowBufferSize)
                    {
                        EnsureShadowBufferAllocated();
                        buffer.CopyTo(new Span<byte>(_buffer, _writePos, buffer.Length));
                        _stream.Write(_buffer, 0, totalUserbytes);
                        _writePos = 0;
                        return;
                    }

                    _stream.Write(_buffer, 0, _writePos);
                    _writePos = 0;
                }

                // Write out user data.
                _stream.Write(buffer);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);

            return WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Fast path check for cancellation already requested
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            EnsureNotClosed();
            EnsureCanWrite();

            // Try to satisfy the request from the buffer synchronously.
            AsyncGate sem = EnsureAsyncActiveSemaphoreInitialized();
            Task semaphoreLockTask = sem.WaitAsync(cancellationToken);
            if (semaphoreLockTask.IsCompletedSuccessfully)
            {
                bool completeSynchronously = true;
                try
                {
                    if (_writePos == 0)
                    {
                        ClearReadBufferBeforeWrite();
                    }


                    // If the write completely fits into the buffer, we can complete synchronously:
                    completeSynchronously = buffer.Length < _bufferSize - _writePos;
                    if (completeSynchronously)
                    {
                        int bytesWritten = WriteToBuffer(buffer.Span);
                        return default;
                    }
                }
                finally
                {
                    if (completeSynchronously)  // if this is FALSE, we will be entering WriteToUnderlyingStreamAsync and releasing there.
                        sem.Release();
                }
            }

            // Delegate to the async implementation.
            return WriteToUnderlyingStreamAsync(buffer, cancellationToken, semaphoreLockTask);
        }

        /// <summary>BufferedStream should be as thin a wrapper as possible. We want WriteAsync to delegate to
        /// WriteAsync of the underlying _stream rather than calling the base Stream which implements the one
        /// in terms of the other. This allows BufferedStream to affect the semantics of the stream it wraps as
        /// little as possible.
        /// </summary>
        private async ValueTask WriteToUnderlyingStreamAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken, Task semaphoreLockTask)
        {

            // See the LARGE COMMENT in Write(..) for the explanation of the write buffer algorithm.

            await semaphoreLockTask.ConfigureAwait(false);
            try
            {
                // The buffer might have been changed by another async task while we were waiting on the semaphore.
                // However, note that if we recalculate the sync completion condition to TRUE, then useBuffer will also be TRUE.

                if (_writePos == 0)
                    ClearReadBufferBeforeWrite();

                int totalUserBytes;
                bool useBuffer;
                checked
                {
                    // We do not expect buffer sizes big enough for an overflow, but if it happens, lets fail early:
                    totalUserBytes = _writePos + buffer.Length;
                    // Allow current totalUserBytes up to int.MaxValue by using uint arithmetic operation for totalUserBytes + buffer.Length
                    useBuffer = ((uint)totalUserBytes + buffer.Length < (_bufferSize + _bufferSize));
                }

                if (useBuffer)
                {
                    buffer = buffer.Slice(WriteToBuffer(buffer.Span));

                    if (_writePos < _bufferSize)
                    {
                        return;
                    }


                    await _stream.WriteAsync(new ReadOnlyMemory<byte>(_buffer, 0, _writePos), cancellationToken).ConfigureAwait(false);
                    _writePos = 0;

                    int bytesWritten = WriteToBuffer(buffer.Span);


                }
                else // !useBuffer
                {
                    // Write out the buffer if necessary.
                    if (_writePos > 0)
                    {

                        // Try avoiding extra write to underlying stream by combining previously buffered data with current user data:
                        if (totalUserBytes <= (_bufferSize + _bufferSize) && totalUserBytes <= MaxShadowBufferSize)
                        {
                            EnsureShadowBufferAllocated();
                            buffer.Span.CopyTo(new Span<byte>(_buffer, _writePos, buffer.Length));

                            await _stream.WriteAsync(new ReadOnlyMemory<byte>(_buffer, 0, totalUserBytes), cancellationToken).ConfigureAwait(false);
                            _writePos = 0;
                            return;
                        }

                        await _stream.WriteAsync(new ReadOnlyMemory<byte>(_buffer, 0, _writePos), cancellationToken).ConfigureAwait(false);
                        _writePos = 0;
                    }

                    // Write out user data.
                    await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _asyncActiveSemaphore.Release();
            }
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
            System.Threading.Tasks.TaskToAsyncResult.Begin(WriteAsync(buffer, offset, count, CancellationToken.None), callback, state);

        public override void EndWrite(IAsyncResult asyncResult) =>
            System.Threading.Tasks.TaskToAsyncResult.End(asyncResult);

        public override void WriteByte(byte value)
        {
            if (_writePos > 0 && _writePos < _bufferSize - 1)
            {
                _buffer[_writePos++] = value;
            }
            else
            {
                WriteByteSlow(value);
            }
        }

        private void WriteByteSlow(byte value)
        {
            EnsureNotClosed();

            if (_writePos == 0)
            {
                EnsureCanWrite();
                ClearReadBufferBeforeWrite();
                EnsureBufferAllocated();
            }

            if (_writePos >= _bufferSize - 1)
            {
                _stream.Write(_buffer, 0, _writePos);
                _writePos = 0;
            }

            _buffer[_writePos++] = value;

        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            EnsureNotClosed();
            EnsureCanSeek();

            // If we have bytes in the write buffer, flush them out, seek and be done.
            if (_writePos > 0)
            {
                // We should be only writing the buffer and not flushing,
                // but the previous version did flush and we stick to it for back-compat reasons.
                FlushWrite();
                return _stream.Seek(offset, origin);
            }

            // The buffer is either empty or we have a buffered read.

            if (_readLen - _readPos > 0 && origin == SeekOrigin.Current)
            {
                // If we have bytes in the read buffer, adjust the seek offset to account for the resulting difference
                // between this stream's position and the underlying stream's position.
                offset -= (_readLen - _readPos);
            }

            long oldPos = Position;

            long newPos = _stream.Seek(offset, origin);

            // If the seek destination is still within the data currently in the buffer, we want to keep the buffer data and continue using it.
            // Otherwise we will throw away the buffer. This can only happen on read, as we flushed write data above.

            // The offset of the new/updated seek pointer within _buffer:
            long readPos = (newPos - (oldPos - _readPos));

            // If the offset of the updated seek pointer in the buffer is still legal, then we can keep using the buffer:
            if (0 <= readPos && readPos < _readLen)
            {
                _readPos = (int)readPos;
                // Adjust the seek pointer of the underlying stream to reflect the amount of useful bytes in the read buffer:
                _stream.Seek(_readLen - _readPos, SeekOrigin.Current);
            }
            else
            {  // The offset of the updated seek pointer is not a legal offset. Loose the buffer.
                _readPos = _readLen = 0;
            }

            return newPos;
        }

        public override void SetLength(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException("value", "Non-negative number required.");

            EnsureNotClosed();
            EnsureCanSeek();
            EnsureCanWrite();

            Flush();
            _stream.SetLength(value);
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            ValidateCopyToArguments(destination, bufferSize);
            EnsureNotClosed();
            EnsureCanRead();

            int readBytes = _readLen - _readPos;

            if (readBytes > 0)
            {
                // If there's any read data in the buffer, write it all to the destination stream.
                destination.Write(_buffer, _readPos, readBytes);
                _readPos = _readLen = 0;
            }
            else if (_writePos > 0)
            {
                // If there's write data in the buffer, flush it back to the underlying stream, as does ReadAsync.
                FlushWrite();
            }

            // Our buffer is now clear. Copy data directly from the source stream to the destination stream.
            _stream.CopyTo(destination, bufferSize);
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            ValidateCopyToArguments(destination, bufferSize);
            EnsureNotClosed();
            EnsureCanRead();

            return cancellationToken.IsCancellationRequested ?
                Task.FromCanceled<int>(cancellationToken) :
                CopyToAsyncCore(destination, bufferSize, cancellationToken);
        }

        private async Task CopyToAsyncCore(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {

            // Synchronize async operations as does Read/WriteAsync.
            await EnsureAsyncActiveSemaphoreInitialized().WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int readBytes = _readLen - _readPos;

                if (readBytes > 0)
                {
                    // If there's any read data in the buffer, write it all to the destination stream.
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(_buffer, _readPos, readBytes), cancellationToken).ConfigureAwait(false);
                    _readPos = _readLen = 0;
                }
                else if (_writePos > 0)
                {
                    // If there's write data in the buffer, flush it back to the underlying stream, as does ReadAsync.
                    await FlushWriteAsync(cancellationToken).ConfigureAwait(false);
                }

                // Our buffer is now clear. Copy data directly from the source stream to the destination stream.
                await _stream.CopyToAsync(destination, bufferSize, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _asyncActiveSemaphore.Release();
            }
        }
    }  // class BufferedStream
}
