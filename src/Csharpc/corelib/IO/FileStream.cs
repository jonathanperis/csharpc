// System.IO.FileStream (buffered, over POSIX fds) and FileStreamOptions. Stream, MemoryStream, UnmanagedMemoryStream, BufferedStream,
// BinaryReader/Writer come from the framework's IL (upstream-types/System.IO.txt).
#nullable disable
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.FileStream")]
    public unsafe class FileStreamImpl : Stream
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
        /// depends on; Asynchronous makes IsAsync true on Windows only, as .NET reports it (an overlapped
        /// handle there; this stream completes its async methods synchronously); DeleteOnClose removes the
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
#if CSHARPC_TARGET_WINDOWS
            _isAsync = (options & FileOptions.Asynchronous) != 0; // an overlapped handle
#endif
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
            : this(path, mode, access, share, bufferSize, 0x1B6 /* 0666, minus umask */) { }

        public FileStreamImpl(string path, FileStreamOptions options)
            : this(path, OptionsOrThrow(options).Mode, options.Access, options.Share, options.BufferSize,
                options.UnixCreateMode is { } unixMode ? (int)unixMode : 0x1B6) { }

        private static FileStreamOptions OptionsOrThrow(FileStreamOptions options) => options ?? throw new ArgumentNullException("options");

        private FileStreamImpl(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, int unixCreateMode)
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
            _fd = Posix.open(p, flags, unixCreateMode, (int)share);
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

#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
        private Microsoft.Win32.SafeHandles.SafeFileHandle _safeHandle;

        /// <summary>The descriptor, after the buffered writes (as .NET flushes before handing it out); the stream keeps owning it.</summary>
        public virtual Microsoft.Win32.SafeHandles.SafeFileHandle SafeFileHandle
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(null, "Cannot access a closed file.");
                Flush();
                return _safeHandle ??= new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)_fd, ownsHandle: false);
            }
        }
#endif
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

    /// <summary>System.IO.FileStreamOptions (FileStreamOptions.cs): the arguments of FileStream(path, options), validated as .NET does.</summary>
    [Shadows("System.IO.FileStreamOptions")]
    public sealed class FileStreamOptionsImpl
    {
        private FileMode _mode = FileMode.Open;
        private FileAccess _access = FileAccess.Read;
        private FileShare _share = FileShare.Read;
        private FileOptions _options;
        private long _preallocationSize;
        private int _bufferSize = 4096;
        private UnixFileMode? _unixCreateMode;

        private static ArgumentOutOfRangeException EnumValue() => new ArgumentOutOfRangeException("value", "Enum value was out of legal range.");

        public FileMode Mode
        {
            get => _mode;
            set => _mode = value < FileMode.CreateNew || value > FileMode.Append ? throw EnumValue() : value;
        }

        public FileAccess Access
        {
            get => _access;
            set => _access = value < FileAccess.Read || value > FileAccess.ReadWrite ? throw EnumValue() : value;
        }

        public FileShare Share
        {
            get => _share;
            set
            {
                FileShare share = value & ~FileShare.Inheritable;
                if (share < FileShare.None || share > (FileShare.ReadWrite | FileShare.Delete)) throw EnumValue();
                _share = value;
            }
        }

        public FileOptions Options
        {
            get => _options;
            set
            {
                // FileStreamHelpers.AreInvalid: only the documented flags (and 0x4000, FILE_FLAG_OPEN_NO_RECALL... as .NET accepts).
                const FileOptions Valid = FileOptions.WriteThrough | FileOptions.Asynchronous | FileOptions.RandomAccess
                    | FileOptions.DeleteOnClose | FileOptions.SequentialScan | FileOptions.Encrypted | (FileOptions)0x20000000;
                if (value != FileOptions.None && (value & ~Valid) != 0) throw EnumValue();
                _options = value;
            }
        }

        public long PreallocationSize
        {
            get => _preallocationSize;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _preallocationSize = value;
            }
        }

        public int BufferSize
        {
            get => _bufferSize;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _bufferSize = value;
            }
        }

        public UnixFileMode? UnixCreateMode
        {
            get => _unixCreateMode;
            set
            {
#if CSHARPC_TARGET_WINDOWS
                throw new PlatformNotSupportedException("Unix file modes are not supported on this platform.");
#else
                if (value.HasValue && ((int)value.Value & ~0xFFF) != 0)
                    throw new ArgumentException("Invalid UnixFileMode value.", "UnixCreateMode");
                _unixCreateMode = value;
#endif
            }
        }
    }
}
