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
