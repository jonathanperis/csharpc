// System.Runtime.InteropServices.MemoryMarshal over spans' raw memory (csharpc's GC never moves
// objects, so an element address stays valid while the span is reachable). Semantics and exceptions
// follow dotnet/runtime's MemoryMarshal.cs: casts require element types without references (.NET throws
// ArgumentException for those), lengths are checked, Read/Write check the span's length.
// GetReference and GetArrayDataReference are lowered directly (FunctionCodeGen.IlCalls.cs).
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Runtime.InteropServices.MemoryMarshal")]
    internal static unsafe class MemoryMarshalImpl
    {
        /// <summary>
        /// The array behind a Memory: csharpc's Memory&lt;T&gt; is a bare { pointer, length }, so the array is the heap
        /// object the pointer is in, when that is a T[] (not a string, native or MemoryManager memory).
        /// </summary>
        public static bool TryGetArray<T>(ReadOnlyMemory<T> memory, out ArraySegment<T> segment)
        {
            if (memory.IsEmpty)
            {
                // .NET reports an empty array-backed memory as an empty segment of its array; csharpc's empty
                // memory has no array left: the empty segment of an empty array.
                segment = new ArraySegment<T>(Array.Empty<T>());
                return true;
            }
            byte* data = (byte*)CoreIntrinsics.SpanAddress(memory.Span);
            byte* start = (byte*)CoreIntrinsics.csrt_gc_object_base(data);
            if (start != null && *(nint*)start == CoreIntrinsics.csrt_type_handle(typeof(T[])))
            {
                T[] array = CoreIntrinsics.ReinterpretAs<T[]>(CoreIntrinsics.PointerToObject((nint)start));
                int offset = (int)((data - (start + 16)) / CoreIntrinsics.SizeOf<T>());
                segment = new ArraySegment<T>(array, offset, memory.Length);
                return true;
            }
            segment = default;
            return false;
        }

        /// <summary>Memory over a pinned array: every array is pinned for csharpc's non-moving collector.</summary>
        public static Memory<T> CreateFromPinnedArray<T>(T[] array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0) throw new ArgumentOutOfRangeException();
                return default;
            }
            return new Memory<T>(array, start, length);
        }

        private static void Copy(byte* from, byte* to, int count)
        {
            for (int i = 0; i < count; i++) to[i] = from[i];
        }

        private static void ThrowIfReferences<T>()
        {
            if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new ArgumentException("Cannot use type '" + typeof(T) + "'. Only value types without pointers or references are supported.");
        }

        public static ReadOnlySpan<byte> AsBytes<T>(ReadOnlySpan<T> span) where T : struct
        {
            ThrowIfReferences<T>();
            return new ReadOnlySpan<byte>((void*)CoreIntrinsics.SpanAddress(span), checked(span.Length * CoreIntrinsics.SizeOf<T>()));
        }

        public static Span<byte> AsBytes<T>(Span<T> span) where T : struct
        {
            ThrowIfReferences<T>();
            return new Span<byte>((void*)CoreIntrinsics.SpanAddress<T>(span), checked(span.Length * CoreIntrinsics.SizeOf<T>()));
        }

        public static Span<TTo> Cast<TFrom, TTo>(Span<TFrom> span) where TFrom : struct where TTo : struct
        {
            ThrowIfReferences<TFrom>();
            ThrowIfReferences<TTo>();
            long bytes = (long)span.Length * CoreIntrinsics.SizeOf<TFrom>();
            return new Span<TTo>((void*)CoreIntrinsics.SpanAddress<TFrom>(span), checked((int)(bytes / CoreIntrinsics.SizeOf<TTo>())));
        }

        public static ReadOnlySpan<TTo> Cast<TFrom, TTo>(ReadOnlySpan<TFrom> span) where TFrom : struct where TTo : struct
        {
            ThrowIfReferences<TFrom>();
            ThrowIfReferences<TTo>();
            long bytes = (long)span.Length * CoreIntrinsics.SizeOf<TFrom>();
            return new ReadOnlySpan<TTo>((void*)CoreIntrinsics.SpanAddress(span), checked((int)(bytes / CoreIntrinsics.SizeOf<TTo>())));
        }

        public static Span<T> CreateSpan<T>(ref T reference, int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            return new Span<T>((void*)CoreIntrinsics.RefAddress(in reference), length);
        }

        public static ReadOnlySpan<T> CreateReadOnlySpan<T>(ref readonly T reference, int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            return new ReadOnlySpan<T>((void*)CoreIntrinsics.RefAddress(in reference), length);
        }

        public static T Read<T>(ReadOnlySpan<byte> source) where T : unmanaged
        {
            ThrowIfReferences<T>();
            if (CoreIntrinsics.SizeOf<T>() > source.Length) throw new ArgumentOutOfRangeException("length");
            T value = default;
            Copy((byte*)CoreIntrinsics.SpanAddress(source), (byte*)CoreIntrinsics.RefAddress(in value), CoreIntrinsics.SizeOf<T>());
            return value;
        }

        public static bool TryRead<T>(ReadOnlySpan<byte> source, out T value) where T : unmanaged
        {
            ThrowIfReferences<T>();
            if (CoreIntrinsics.SizeOf<T>() > source.Length) { value = default; return false; }
            value = default;
            Copy((byte*)CoreIntrinsics.SpanAddress(source), (byte*)CoreIntrinsics.RefAddress(in value), CoreIntrinsics.SizeOf<T>());
            return true;
        }

        public static void Write<T>(Span<byte> destination, in T value) where T : unmanaged
        {
            ThrowIfReferences<T>();
            if (CoreIntrinsics.SizeOf<T>() > destination.Length) throw new ArgumentOutOfRangeException("length");
            Copy((byte*)CoreIntrinsics.RefAddress(in value), (byte*)CoreIntrinsics.SpanAddress<byte>(destination), CoreIntrinsics.SizeOf<T>());
        }

        public static bool TryWrite<T>(Span<byte> destination, in T value) where T : unmanaged
        {
            ThrowIfReferences<T>();
            if (CoreIntrinsics.SizeOf<T>() > destination.Length) return false;
            Copy((byte*)CoreIntrinsics.RefAddress(in value), (byte*)CoreIntrinsics.SpanAddress<byte>(destination), CoreIntrinsics.SizeOf<T>());
            return true;
        }
    }
}
