// Encoding.CreateTranscodingStream (.NET Core 3.0): a stream that converts between two encodings on the way to or from
// an inner stream. System.Net.Http.Json wraps a response body (or request body) with it when a JSON payload's
// charset isn't UTF-8. Reads decode the inner stream's bytes (innerStreamEncoding) and re-encode them as
// outerStreamEncoding; writes do the opposite, and disposing flushes the encoder's pending bytes.
#nullable disable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Text.Encoding")]
    internal static class EncodingTranscodingMembers
    {
        public static Stream CreateTranscodingStream(Stream innerStream, Encoding innerStreamEncoding, Encoding outerStreamEncoding, bool leaveOpen = false)
        {
            if (innerStream == null) throw new ArgumentNullException("innerStream");
            if (innerStreamEncoding == null) throw new ArgumentNullException("innerStreamEncoding");
            if (outerStreamEncoding == null) throw new ArgumentNullException("outerStreamEncoding");
            return new TranscodingStream(innerStream, innerStreamEncoding, outerStreamEncoding, leaveOpen);
        }
    }

    internal sealed class TranscodingStream : Stream
    {
        private readonly Stream _inner;
        private readonly bool _leaveOpen;
        // Reads: inner bytes -> chars (inner decoder) -> outer bytes (outer encoder).
        private readonly Decoder _readDecoder;
        private readonly Encoder _readEncoder;
        // Writes: outer bytes -> chars (outer decoder) -> inner bytes (inner encoder).
        private readonly Decoder _writeDecoder;
        private readonly Encoder _writeEncoder;
        private byte[] _pending = Array.Empty<byte>(); // outer bytes already converted but not yet read
        private int _pendingOffset;
        private bool _readEnded, _disposed;

        internal TranscodingStream(Stream inner, Encoding innerEncoding, Encoding outerEncoding, bool leaveOpen)
        {
            _inner = inner;
            _leaveOpen = leaveOpen;
            _readDecoder = innerEncoding.GetDecoder();
            _readEncoder = outerEncoding.GetEncoder();
            _writeDecoder = outerEncoding.GetDecoder();
            _writeEncoder = innerEncoding.GetEncoder();
        }

        public override bool CanRead => !_disposed && _inner.CanRead;
        public override bool CanWrite => !_disposed && _inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException("Stream does not support seeking.");
        public override long Position
        {
            get => throw new NotSupportedException("Stream does not support seeking.");
            set => throw new NotSupportedException("Stream does not support seeking.");
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Stream does not support seeking.");
        public override void SetLength(long value) => throw new NotSupportedException("Stream does not support seeking.");

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("Stream");
        }

        private void EnsureReadable()
        {
            ThrowIfDisposed();
            if (!_inner.CanRead) throw new NotSupportedException("Stream does not support reading.");
        }

        private void EnsureWritable()
        {
            ThrowIfDisposed();
            if (!_inner.CanWrite) throw new NotSupportedException("Stream does not support writing.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(new Span<byte>(buffer, offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            EnsureReadable();
            if (buffer.IsEmpty) return 0;
            var input = new byte[4096];
            while (_pendingOffset == _pending.Length && !_readEnded)
            {
                int n = _inner.Read(input, 0, input.Length);
                Convert(input, n);
            }
            return Drain(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureReadable();
            if (buffer.IsEmpty) return 0;
            var input = new byte[4096];
            while (_pendingOffset == _pending.Length && !_readEnded)
            {
                int n = await _inner.ReadAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
                Convert(input, n);
            }
            return Drain(buffer.Span);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        // Inner bytes -> outer bytes, held until the caller has room; zero bytes read is the end of the inner stream.
        private void Convert(byte[] input, int count)
        {
            bool flush = count == 0;
            int charCount = _readDecoder.GetCharCount(input, 0, count, flush);
            var chars = new char[charCount];
            charCount = _readDecoder.GetChars(input, 0, count, chars, 0, flush);
            int byteCount = _readEncoder.GetByteCount(chars, 0, charCount, flush);
            _pending = new byte[byteCount];
            _readEncoder.GetBytes(chars, 0, charCount, _pending, 0, flush);
            _pendingOffset = 0;
            if (flush) _readEnded = true;
        }

        private int Drain(Span<byte> buffer)
        {
            int n = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
            _pending.AsSpan(_pendingOffset, n).CopyTo(buffer);
            _pendingOffset += n;
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            Write(new ReadOnlySpan<byte>(buffer, offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWritable();
            byte[] converted = ConvertForWrite(buffer.ToArray(), flush: false);
            if (converted.Length > 0) _inner.Write(converted, 0, converted.Length);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureWritable();
            byte[] converted = ConvertForWrite(buffer.ToArray(), flush: false);
            if (converted.Length > 0) await _inner.WriteAsync(converted.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        private byte[] ConvertForWrite(byte[] bytes, bool flush)
        {
            int charCount = _writeDecoder.GetCharCount(bytes, 0, bytes.Length, flush);
            var chars = new char[charCount];
            charCount = _writeDecoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush);
            int byteCount = _writeEncoder.GetByteCount(chars, 0, charCount, flush);
            var result = new byte[byteCount];
            _writeEncoder.GetBytes(chars, 0, charCount, result, 0, flush);
            return result;
        }

        public override void Flush()
        {
            ThrowIfDisposed();
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return _inner.FlushAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try
                {
                    if (_inner.CanWrite)
                    {
                        byte[] tail = ConvertForWrite(Array.Empty<byte>(), flush: true);
                        if (tail.Length > 0) _inner.Write(tail, 0, tail.Length);
                    }
                }
                finally
                {
                    if (!_leaveOpen) _inner.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_inner.CanWrite)
                {
                    byte[] tail = ConvertForWrite(Array.Empty<byte>(), flush: true);
                    if (tail.Length > 0) await _inner.WriteAsync(tail.AsMemory()).ConfigureAwait(false);
                }
            }
            finally
            {
                if (!_leaveOpen) await _inner.DisposeAsync().ConfigureAwait(false);
            }
            GC.SuppressFinalize(this);
        }
    }
}
