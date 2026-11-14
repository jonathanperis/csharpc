// System.IO.MemoryMappedFiles: views of files, the subset System.Reflection.Metadata (and so Roslyn's
// metadata references) use: MemoryMappedFile.CreateFromFile(FileStream, ...) with MemoryMappedFileAccess.Read,
// CreateViewAccessor, and the view's SafeBuffer pointer; and (PROTOTYPE) read-write maps of a file with
// UnmanagedMemoryAccessor's typed Read/Write (eM Client's Spotlight checkpoint). Named maps, copy-on-write and
// execute access, and views as streams are not implemented: they throw NotSupportedException.
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
        private readonly bool _writable;
        private bool _disposed;

        private MemoryMappedFileImpl(FileStream stream, long capacity, bool leaveOpen, bool writable)
        {
            _stream = stream;
            _capacity = capacity;
            _leaveOpen = leaveOpen;
            _writable = writable;
        }

        private static NotSupportedException UnsupportedAccess() =>
            new NotSupportedException("csharpc supports memory-mapped files with MemoryMappedFileAccess.Read or ReadWrite only yet.");

        public static MemoryMappedFile CreateFromFile(FileStream fileStream, string mapName, long capacity, MemoryMappedFileAccess access,
            HandleInheritability inheritability, bool leaveOpen)
        {
            if (fileStream == null) throw new ArgumentNullException("fileStream", "fileStream cannot be null.");
            if (mapName != null && mapName.Length == 0) throw new ArgumentException("Map name cannot be an empty string.");
            if (capacity < 0) throw new ArgumentOutOfRangeException("capacity", "The capacity must be greater than or equal to 0 for a Memory Mapped File backed by a file.");
            if (access is not (MemoryMappedFileAccess.Read or MemoryMappedFileAccess.ReadWrite)) throw UnsupportedAccess();
            if (mapName != null) throw new NotSupportedException("Named memory-mapped files are not supported by csharpc yet.");
            if (!Posix.CanMapFiles) throw new PlatformNotSupportedException("Memory-mapped files are not supported on this platform.");
            long length = fileStream.Length;
            if (capacity == 0 && length == 0)
                throw new ArgumentException("A positive capacity must be specified for a Memory Mapped File backed by an empty file.");
            if (capacity != 0 && capacity < length)
                throw new ArgumentOutOfRangeException("capacity", "The capacity may not be smaller than the file size.");
            if (capacity > length && access == MemoryMappedFileAccess.Read)
                throw new ArgumentException("When specifying MemoryMappedFileAccess.Read access, the capacity must not be larger than the file size.");
            fileStream.Flush();
            // A writable map larger than its file grows the file to the capacity first, as .NET does.
            if (capacity > length) fileStream.SetLength(capacity);
            return (MemoryMappedFile)(object)new MemoryMappedFileImpl(fileStream, capacity == 0 ? length : capacity, leaveOpen,
                access == MemoryMappedFileAccess.ReadWrite);
        }

        // .NET's default view access is ReadWrite, which a read-only map refuses; csharpc gives such a map a read view.
        public MemoryMappedViewAccessor CreateViewAccessor() => CreateViewAccessor(0, 0, _writable ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read);
        public MemoryMappedViewAccessor CreateViewAccessor(long offset, long size) =>
            CreateViewAccessor(offset, size, _writable ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read);

        public MemoryMappedViewAccessor CreateViewAccessor(long offset, long size, MemoryMappedFileAccess access)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
            if (size < 0) throw new ArgumentOutOfRangeException("size", "A positive number is required.");
            if (_disposed) throw new ObjectDisposedException("MemoryMappedFile", "Cannot access a disposed object.");
            if (access is not (MemoryMappedFileAccess.Read or MemoryMappedFileAccess.ReadWrite)) throw UnsupportedAccess();
            bool writable = access == MemoryMappedFileAccess.ReadWrite;
            if (writable && !_writable) throw new UnauthorizedAccessException("Access to the path is denied.");
            if (size == 0) size = _capacity - offset;
            if (size <= 0 || offset + size > _capacity)
                throw new ArgumentOutOfRangeException("size", "The capacity may not be smaller than the file size.");
            long start = offset - offset % Granularity;
            long delta = offset - start;
            int fd = ((FileStreamImpl)(object)_stream).Descriptor;
            byte* view = Posix.MapView(fd, start, size + delta, writable);
            if (view == null) throw Posix.IOError(Posix.Errno, null, false);
            var handle = new SafeMemoryMappedViewHandleImpl(view, size + delta);
            return (MemoryMappedViewAccessor)(object)new MemoryMappedViewAccessorImpl(handle, delta, size, writable);
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
    public sealed class MemoryMappedViewAccessorImpl : UnmanagedMemoryAccessorImpl
    {
        private readonly SafeMemoryMappedViewHandleImpl _handle;
        private readonly long _pointerOffset;

        internal MemoryMappedViewAccessorImpl(SafeMemoryMappedViewHandleImpl handle, long pointerOffset, long capacity, bool writable)
            : base(handle, pointerOffset, capacity, writable)
        {
            _handle = handle;
            _pointerOffset = pointerOffset;
        }

        public SafeMemoryMappedViewHandle SafeMemoryMappedViewHandle => (SafeMemoryMappedViewHandle)(object)_handle;
        public long PointerOffset => _pointerOffset;

        // MAP_SHARED pages are the file's: other readers of the file see the writes already.
        public void Flush() { }
    }

    /// <summary>
    /// UnmanagedMemoryAccessor (dotnet/runtime's UnmanagedMemoryAccessor.cs, the parts views use): bounds- and
    /// access-checked reads and writes at a position of a SafeBuffer, which starts <c>_start</c> bytes into the buffer.
    /// </summary>
    [Shadows("System.IO.UnmanagedMemoryAccessor")]
    public unsafe class UnmanagedMemoryAccessorImpl : IDisposable
    {
        private readonly SafeBufferImpl _buffer;
        private readonly long _start, _capacity;
        private readonly bool _canWrite;
        private bool _open;

        internal UnmanagedMemoryAccessorImpl(SafeBufferImpl buffer, long start, long capacity, bool canWrite)
        {
            _buffer = buffer;
            _start = start;
            _capacity = capacity;
            _canWrite = canWrite;
            _open = true;
        }

        public long Capacity => _capacity;
        public bool CanRead => _open;
        public bool CanWrite => _open && _canWrite;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_open) return;
            _open = false;
            _buffer.Dispose();
        }

        // .NET's checks, in its order (UnmanagedMemoryAccessor.cs): closed, access, negative position, then room.
        private void CheckOpen(bool write)
        {
            if (!_open) throw new ObjectDisposedException("UnmanagedMemoryAccessor", "Cannot access a closed accessor.");
            if (write && !_canWrite) throw new NotSupportedException("Accessor does not support writing.");
        }

        private static ArgumentOutOfRangeException PastCapacity() =>
            new ArgumentOutOfRangeException("position", "The position may not be greater or equal to the capacity of the accessor.");

        private ulong Checked(long position, int size, bool write)
        {
            CheckOpen(write);
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            if (position > _capacity - size)
            {
                if (position >= _capacity) throw PastCapacity();
                throw new ArgumentException(write ? "There are not enough bytes remaining in the accessor to write at this position."
                    : "There are not enough bytes remaining in the accessor to read at this position.", "position");
            }
            return (ulong)(_start + position);
        }

#pragma warning disable CS8500 // structs of unmanaged data only, as in .NET
        public void Read<T>(long position, out T structure) where T : struct => structure = _buffer.Read<T>(Checked(position, sizeof(T), false));
        public void Write<T>(long position, ref T structure) where T : struct => _buffer.Write<T>(Checked(position, sizeof(T), true), structure);

        public int ReadArray<T>(long position, T[] array, int offset, int count) where T : struct
        {
            ArrayArguments(array, offset, count);
            CheckOpen(false);
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            if (position >= _capacity) throw PastCapacity();
            int n = (int)Math.Min(count, (_capacity - position) / sizeof(T));
            if (n > 0) _buffer.ReadArray<T>((ulong)(_start + position), array, offset, n);
            return n;
        }

        // Room is the SafeBuffer's check here, as in .NET ("Not enough space available in the buffer."): the view's buffer
        // ends where the accessor does.
        public void WriteArray<T>(long position, T[] array, int offset, int count) where T : struct
        {
            ArrayArguments(array, offset, count);
            CheckOpen(true);
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            if (position >= _capacity) throw PastCapacity();
            _buffer.WriteArray<T>((ulong)(_start + position), array, offset, count);
        }
#pragma warning restore CS8500

        private static void ArrayArguments<T>(T[] array, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (array.Length - offset < count) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }

        public byte ReadByte(long position) => _buffer.Read<byte>(Checked(position, 1, false));
        public bool ReadBoolean(long position) => ReadByte(position) != 0;
        public short ReadInt16(long position) => _buffer.Read<short>(Checked(position, 2, false));
        public int ReadInt32(long position) => _buffer.Read<int>(Checked(position, 4, false));
        public long ReadInt64(long position) => _buffer.Read<long>(Checked(position, 8, false));
        public ushort ReadUInt16(long position) => _buffer.Read<ushort>(Checked(position, 2, false));
        public uint ReadUInt32(long position) => _buffer.Read<uint>(Checked(position, 4, false));
        public ulong ReadUInt64(long position) => _buffer.Read<ulong>(Checked(position, 8, false));
        public double ReadDouble(long position) => _buffer.Read<double>(Checked(position, 8, false));
        public void Write(long position, byte value) => _buffer.Write(Checked(position, 1, true), value);
        public void Write(long position, bool value) => Write(position, (byte)(value ? 1 : 0));
        public void Write(long position, short value) => _buffer.Write(Checked(position, 2, true), value);
        public void Write(long position, int value) => _buffer.Write(Checked(position, 4, true), value);
        public void Write(long position, long value) => _buffer.Write(Checked(position, 8, true), value);
        public void Write(long position, ushort value) => _buffer.Write(Checked(position, 2, true), value);
        public void Write(long position, uint value) => _buffer.Write(Checked(position, 4, true), value);
        public void Write(long position, ulong value) => _buffer.Write(Checked(position, 8, true), value);
        public void Write(long position, double value) => _buffer.Write(Checked(position, 8, true), value);
    }

    /// <summary>
    /// SafeBuffer (ported from dotnet/runtime's SafeBuffer.cs): a SafeHandle over unmanaged memory of a length
    /// set by Initialize; AcquirePointer/ReleasePointer add and release a reference. Libraries compiled from
    /// IL derive from it (System.Text.Encoding.CodePages' SafeAllocHHandle).
    /// </summary>
    [Shadows("System.Runtime.InteropServices.SafeBuffer")]
    public abstract unsafe class SafeBufferImpl : SafeHandleZeroOrMinusOneIsInvalidImpl
    {
        private static nuint Uninitialized => unchecked((nuint)(-1)); // nuint.MaxValue
        private nuint _numBytes;

        protected SafeBufferImpl(bool ownsHandle) : base(ownsHandle) { _numBytes = Uninitialized; }

        public void Initialize(ulong numBytes)
        {
            if (IntPtr.Size == 4 && numBytes > uint.MaxValue)
                throw new ArgumentOutOfRangeException("numBytes", "The number of bytes cannot exceed the virtual address space on a 32 bit machine.");
            if (numBytes >= (ulong)Uninitialized)
                throw new ArgumentOutOfRangeException("numBytes", "The length of the buffer must be less than the maximum value for UIntPtr.");
            _numBytes = (nuint)numBytes;
        }

        public void Initialize(uint numElements, uint sizeOfEachElement)
        {
            ulong total = (ulong)numElements * sizeOfEachElement;
            if (IntPtr.Size == 4 && total > uint.MaxValue)
                throw new ArgumentOutOfRangeException("numBytes", "The number of bytes cannot exceed the virtual address space on a 32 bit machine.");
            if (total >= (ulong)Uninitialized)
                throw new ArgumentOutOfRangeException("numElements", "The length of the buffer must be less than the maximum value for UIntPtr.");
            _numBytes = (nuint)total;
        }

        public void Initialize<T>(uint numElements) where T : struct => Initialize(numElements, (uint)sizeof(T));

        // Typed access (dotnet/runtime SafeBuffer.cs): every access checks the buffer's length and holds a reference
        // to the handle so that it can't be released meanwhile.
#pragma warning disable CS8500 // structs of unmanaged data only, as in .NET (T is not constrained to unmanaged there either)
        public T Read<T>(ulong byteOffset) where T : struct
        {
            if (_numBytes == Uninitialized) throw NotInitialized();
            byte* ptr = (byte*)handle + byteOffset;
            SpaceCheck(ptr, (nuint)sizeof(T));
            T value = default;
            bool mustCallRelease = false;
            try
            {
                DangerousAddRef(ref mustCallRelease);
                value = *(T*)ptr;
            }
            finally
            {
                if (mustCallRelease) DangerousRelease();
            }
            return value;
        }

        public void Write<T>(ulong byteOffset, T value) where T : struct
        {
            if (_numBytes == Uninitialized) throw NotInitialized();
            byte* ptr = (byte*)handle + byteOffset;
            SpaceCheck(ptr, (nuint)sizeof(T));
            bool mustCallRelease = false;
            try
            {
                DangerousAddRef(ref mustCallRelease);
                *(T*)ptr = value;
            }
            finally
            {
                if (mustCallRelease) DangerousRelease();
            }
        }

        public void ReadArray<T>(ulong byteOffset, T[] array, int index, int count) where T : struct
        {
            if (array == null) throw new ArgumentNullException("array", "Buffer cannot be null.");
            if (index < 0) throw new ArgumentOutOfRangeException("index", "Non-negative number required.");
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            if (array.Length - index < count) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            if (_numBytes == Uninitialized) throw NotInitialized();
            byte* ptr = (byte*)handle + byteOffset;
            SpaceCheck(ptr, checked((nuint)sizeof(T) * (nuint)count));
            bool mustCallRelease = false;
            try
            {
                DangerousAddRef(ref mustCallRelease);
                for (int i = 0; i < count; i++) array[index + i] = ((T*)ptr)[i];
            }
            finally
            {
                if (mustCallRelease) DangerousRelease();
            }
        }

        public void WriteArray<T>(ulong byteOffset, T[] array, int index, int count) where T : struct
        {
            if (array == null) throw new ArgumentNullException("array", "Buffer cannot be null.");
            if (index < 0) throw new ArgumentOutOfRangeException("index", "Non-negative number required.");
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            if (array.Length - index < count) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            if (_numBytes == Uninitialized) throw NotInitialized();
            byte* ptr = (byte*)handle + byteOffset;
            SpaceCheck(ptr, checked((nuint)sizeof(T) * (nuint)count));
            bool mustCallRelease = false;
            try
            {
                DangerousAddRef(ref mustCallRelease);
                for (int i = 0; i < count; i++) ((T*)ptr)[i] = array[index + i];
            }
            finally
            {
                if (mustCallRelease) DangerousRelease();
            }
        }

        public void ReadSpan<T>(ulong byteOffset, Span<T> buffer) where T : struct
        {
            if (_numBytes == Uninitialized) throw NotInitialized();
            byte* ptr = (byte*)handle + byteOffset;
            SpaceCheck(ptr, checked((nuint)sizeof(T) * (nuint)buffer.Length));
            bool mustCallRelease = false;
            try
            {
                DangerousAddRef(ref mustCallRelease);
                for (int i = 0; i < buffer.Length; i++) buffer[i] = ((T*)ptr)[i];
            }
            finally
            {
                if (mustCallRelease) DangerousRelease();
            }
        }

        public void WriteSpan<T>(ulong byteOffset, ReadOnlySpan<T> data) where T : struct
        {
            if (_numBytes == Uninitialized) throw NotInitialized();
            byte* ptr = (byte*)handle + byteOffset;
            SpaceCheck(ptr, checked((nuint)sizeof(T) * (nuint)data.Length));
            bool mustCallRelease = false;
            try
            {
                DangerousAddRef(ref mustCallRelease);
                for (int i = 0; i < data.Length; i++) ((T*)ptr)[i] = data[i];
            }
            finally
            {
                if (mustCallRelease) DangerousRelease();
            }
        }
#pragma warning restore CS8500

        private void SpaceCheck(byte* ptr, nuint sizeInBytes)
        {
            if (_numBytes < sizeInBytes || (nuint)(ptr - (byte*)handle) > _numBytes - sizeInBytes)
                throw new ArgumentException("Not enough space available in the buffer.");
        }

        public void AcquirePointer(ref byte* pointer)
        {
            if (_numBytes == Uninitialized) throw NotInitialized();
            pointer = null;
            bool junk = false;
            DangerousAddRef(ref junk);
            pointer = (byte*)handle;
        }

        public void ReleasePointer()
        {
            if (_numBytes == Uninitialized) throw NotInitialized();
            DangerousRelease();
        }

        public ulong ByteLength
        {
            get
            {
                if (_numBytes == Uninitialized) throw NotInitialized();
                return _numBytes;
            }
        }

        private static InvalidOperationException NotInitialized() =>
            new InvalidOperationException("You must call Initialize on this object instance before using it.");
    }

    [Shadows("Microsoft.Win32.SafeHandles.SafeMemoryMappedViewHandle")]
    public sealed unsafe class SafeMemoryMappedViewHandleImpl : SafeBufferImpl
    {
        internal SafeMemoryMappedViewHandleImpl(byte* view, long length) : base(true)
        {
            SetHandle((IntPtr)view);
            Initialize((ulong)length);
        }

        protected override bool ReleaseHandle()
        {
            Posix.UnmapView((byte*)handle, (long)ByteLength);
            handle = IntPtr.Zero;
            return true;
        }
    }
}
