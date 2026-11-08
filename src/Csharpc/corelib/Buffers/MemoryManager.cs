// System.Buffers.MemoryManager<T>: the base class of owners of memory that is not an array (an unmanaged
// buffer, a pooled block). csharpc's Memory<T> is a bare { pointer, length } (objects never move, ADR 0004),
// so the Memory a manager hands out is built from the pointer of its span; Pin returns the manager's own
// handle. After dotnet/runtime (release/11.0) src/libraries/System.Memory/src/System/Buffers/MemoryManager.cs, MIT.
#nullable disable
using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Buffers.MemoryManager`1")]
    public abstract class MemoryManagerImpl<T> : IMemoryOwner<T>, IPinnable
    {
        public virtual Memory<T> Memory
        {
            get
            {
                Span<T> span = GetSpan();
                return CoreIntrinsics.MemoryOf(ref MemoryMarshal.GetReference(span), span.Length);
            }
        }

        public abstract Span<T> GetSpan();

        public abstract MemoryHandle Pin(int elementIndex = 0);

        public abstract void Unpin();

        protected Memory<T> CreateMemory(int length)
        {
            Span<T> span = GetSpan();
            if ((uint)length > (uint)span.Length) throw new ArgumentOutOfRangeException("length");
            return CoreIntrinsics.MemoryOf(ref MemoryMarshal.GetReference(span), length);
        }

        protected Memory<T> CreateMemory(int start, int length)
        {
            Span<T> span = GetSpan();
            if ((uint)start > (uint)span.Length || (uint)length > (uint)(span.Length - start)) throw new ArgumentOutOfRangeException("length");
            return CoreIntrinsics.MemoryOf(ref MemoryMarshal.GetReference(span.Slice(start, length)), length);
        }

        protected internal virtual bool TryGetArray(out ArraySegment<T> segment)
        {
            segment = default;
            return false;
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected abstract void Dispose(bool disposing);
    }
}
