// Vectorized searches over byte and char spans (src/runtime/span.c): MemoryExtensions.IndexOfAny and
// IndexOfAnyExcept with up to three values, the calls Kestrel's HTTP parser makes for every header.
// T is byte or char here (the callers' typeof tests fold after monomorphization).
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class SpanSearch
    {
        [DllImport("__Internal", EntryPoint = "csrt_span_index_of_any_u8")]
        private static extern int AnyU8(byte* p, int length, byte a, byte b, byte c, bool except);

        [DllImport("__Internal", EntryPoint = "csrt_span_index_of_any_u16")]
        private static extern int AnyU16(ushort* p, int length, ushort a, ushort b, ushort c, bool except);  // not char: P/Invoke would narrow it to an ANSI byte

        internal static int IndexOfAny<T>(ReadOnlySpan<T> span, T a, T b, T c, bool except)
        {
            // The collector doesn't move objects: the span's memory stays put during the call.
            void* p = Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));
            if (typeof(T) == typeof(byte))
                return AnyU8((byte*)p, span.Length, Unsafe.As<T, byte>(ref a), Unsafe.As<T, byte>(ref b), Unsafe.As<T, byte>(ref c), except);
            return AnyU16((ushort*)p, span.Length, Unsafe.As<T, ushort>(ref a), Unsafe.As<T, ushort>(ref b), Unsafe.As<T, ushort>(ref c), except);
        }
    }
}
