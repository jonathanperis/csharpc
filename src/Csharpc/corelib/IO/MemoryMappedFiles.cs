// System.IO.MemoryMappedFiles: read-only views of files, the subset System.Reflection.Metadata (and
// so Roslyn's metadata references) use: MemoryMappedFile.CreateFromFile(FileStream, ...) with
// MemoryMappedFileAccess.Read, CreateViewAccessor, and the view's SafeBuffer pointer. Writable maps,
// named maps and views as streams are not implemented: they throw NotSupportedException.
#nullable disable
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CsharpcCoreLib
{
    [Shadows("System.IO.MemoryMappedFiles.MemoryMappedFile")]
    public unsafe class MemoryMappedFileImpl : IDisposable
    {
        // Views start at a multiple of this (4 KiB pages, 16 KiB on Apple silicon, 64 KiB Windows allocation granularity).
        internal const long Granularity = 65536;

        private readonly FileStream _stream;
        private readonly long _capacity;
        private readonly bool _leaveOpen;
        private bool _disposed;

        private MemoryMappedFileImpl(FileStream stream, long capacity, bool leaveOpen)
        {
            _stream = stream;
            _capacity = capacity;
            _leaveOpen = leaveOpen;
        }

        public static MemoryMappedFile CreateFromFile(FileStream fileStream, string mapName, long capacity, MemoryMappedFileAccess access,
            HandleInheritability inheritability, bool leaveOpen)
        {
            if (fileStream == null) throw new ArgumentNullException("fileStream", "fileStream cannot be null.");
            if (mapName != null && mapName.Length == 0) throw new ArgumentException("Map name cannot be an empty string.");
            if (capacity < 0) throw new ArgumentOutOfRangeException("capacity", "The capacity must be greater than or equal to 0 for a Memory Mapped File backed by a file.");
            if (access != MemoryMappedFileAccess.Read)
                throw new NotSupportedException("csharpc supports read-only memory-mapped files only (MemoryMappedFileAccess.Read) yet.");
            if (mapName != null) throw new NotSupportedException("Named memory-mapped files are not supported by csharpc yet.");
            if (!Posix.CanMapFiles) throw new PlatformNotSupportedException("Memory-mapped files are not supported on this platform.");
            long length = fileStream.Length;
            if (capacity == 0 && length == 0)
                throw new ArgumentException("A positive capacity must be specified for a Memory Mapped File backed by an empty file.");
            if (capacity != 0 && capacity < length)
                throw new ArgumentOutOfRangeException("capacity", "The capacity may not be smaller than the file size.");
            if (capacity > length)
                throw new ArgumentException("When specifying MemoryMappedFileAccess.Read access, the capacity must not be larger than the file size.");
            fileStream.Flush();
            return (MemoryMappedFile)(object)new MemoryMappedFileImpl(fileStream, capacity == 0 ? length : capacity, leaveOpen);
        }

        public MemoryMappedViewAccessor CreateViewAccessor() => CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        public MemoryMappedViewAccessor CreateViewAccessor(long offset, long size) => CreateViewAccessor(offset, size, MemoryMappedFileAccess.Read);

        public MemoryMappedViewAccessor CreateViewAccessor(long offset, long size, MemoryMappedFileAccess access)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
            if (size < 0) throw new ArgumentOutOfRangeException("size", "A positive number is required.");
            if (_disposed) throw new ObjectDisposedException("MemoryMappedFile", "Cannot access a disposed object.");
            if (access != MemoryMappedFileAccess.Read)
                throw new NotSupportedException("csharpc supports read-only memory-mapped files only (MemoryMappedFileAccess.Read) yet.");
            if (size == 0) size = _capacity - offset;
            if (size <= 0 || offset + size > _capacity)
                throw new ArgumentOutOfRangeException("size", "The capacity may not be smaller than the file size.");
            long start = offset - offset % Granularity;
            long delta = offset - start;
            int fd = ((FileStreamImpl)(object)_stream).Descriptor;
            byte* view = Posix.MapView(fd, start, size + delta);
            if (view == null) throw Posix.IOError(Posix.Errno, null, false);
            var handle = new SafeMemoryMappedViewHandleImpl(view, size + delta);
            return (MemoryMappedViewAccessor)(object)new MemoryMappedViewAccessorImpl(handle, delta, size);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_leaveOpen) _stream.Dispose();
        }
    }

    [Shadows("System.IO.MemoryMappedFiles.MemoryMappedViewAccessor")]
    public sealed class MemoryMappedViewAccessorImpl : IDisposable
    {
        private readonly SafeMemoryMappedViewHandleImpl _handle;
        private readonly long _pointerOffset, _capacity;

        internal MemoryMappedViewAccessorImpl(SafeMemoryMappedViewHandleImpl handle, long pointerOffset, long capacity)
        {
            _handle = handle;
            _pointerOffset = pointerOffset;
            _capacity = capacity;
        }

        public SafeMemoryMappedViewHandle SafeMemoryMappedViewHandle => (SafeMemoryMappedViewHandle)(object)_handle;
        public long PointerOffset => _pointerOffset;
        public long Capacity => _capacity;
        public bool CanRead => !_handle.IsClosed;
        public bool CanWrite => false;

        public void Dispose()
        {
            _handle.Dispose();
        }
    }

    /// <summary>SafeBuffer: a pointer to unmanaged memory with an acquire/release discipline.</summary>
    [Shadows("System.Runtime.InteropServices.SafeBuffer")]
    public abstract unsafe class SafeBufferImpl : IDisposable
    {
        internal byte* _pointer;
        internal ulong _byteLength;
        private bool _closed;

        public bool IsClosed => _closed;
        public ulong ByteLength => _byteLength;

        public void AcquirePointer(ref byte* pointer)
        {
            if (_closed) throw new ObjectDisposedException("SafeHandle", "Safe handle has been closed.");
            pointer = _pointer;
        }

        public void ReleasePointer() { }

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            ReleaseHandle();
        }

        protected abstract bool ReleaseHandle();
    }

    [Shadows("Microsoft.Win32.SafeHandles.SafeMemoryMappedViewHandle")]
    public sealed unsafe class SafeMemoryMappedViewHandleImpl : SafeBufferImpl
    {
        internal SafeMemoryMappedViewHandleImpl(byte* view, long length)
        {
            _pointer = view;
            _byteLength = (ulong)length;
        }

        protected override bool ReleaseHandle()
        {
            Posix.UnmapView(_pointer, (long)_byteLength);
            _pointer = null;
            return true;
        }
    }
}
