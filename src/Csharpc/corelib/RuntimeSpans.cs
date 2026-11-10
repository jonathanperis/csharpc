// Span searches and reductions that run in src/runtime/span.c (portable vector code): Array.IndexOf over bitwise-equatable
// elements and MemoryExtensions.Min/Max of the primitives. System.Linq is the framework's own (ADR 0023), which reaches its
// vector paths through Vector<T> instead.
#nullable disable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class RuntimeSpans
    {
        private static void* Address<T>(ReadOnlySpan<T> span) => Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));

        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_i32")]
        private static extern int MinMaxI32(int* p, int length, int max);

        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_i64")]
        private static extern long MinMaxI64(long* p, int length, int max);

        [DllImport("__Internal", EntryPoint = "csrt_span_index_of")]
        private static extern int IndexOfBits(void* p, int length, long elementSize, void* value);

        // .NET's MinFloat / MaxFloat over a span (src/runtime/span.c): NaN is the minimum (the first one after the
        // first element: .NET's span loop tests span[0] last), Max skips NaNs; of equal extremes the first (-0.0
        // and 0.0 compare equal).
        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_f64")]
        private static extern double MinMaxF64(double* p, int length, int max, int allNaNFirst);

        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_f32")]
        private static extern float MinMaxF32(float* p, int length, int max, int allNaNFirst);

        /// <summary>
        /// MemoryExtensions.Min/Max (corelib StringExtras.cs) of a non-empty span of int, long, double or float with the
        /// default comparer: CompareTo's order, which for doubles gives a span of only NaNs its first element.
        /// False for other element types.
        /// </summary>
        internal static bool TryMinMaxPrimitive<T>(ReadOnlySpan<T> span, bool max, out T result)
        {
            void* p = Address(span);
            if (typeof(T) == typeof(int)) { int r = MinMaxI32((int*)p, span.Length, max ? 1 : 0); result = Unsafe.As<int, T>(ref r); return true; }
            if (typeof(T) == typeof(long)) { long r = MinMaxI64((long*)p, span.Length, max ? 1 : 0); result = Unsafe.As<long, T>(ref r); return true; }
            if (typeof(T) == typeof(double)) { double r = MinMaxF64((double*)p, span.Length, max ? 1 : 0, 1); result = Unsafe.As<double, T>(ref r); return true; }
            if (typeof(T) == typeof(float)) { float r = MinMaxF32((float*)p, span.Length, max ? 1 : 0, 1); result = Unsafe.As<float, T>(ref r); return true; }
            result = default;
            return false;
        }

        /// <summary>The first index of `value` in a span of a bitwise-equatable type (callers test IsBitwiseEquatable).</summary>
        internal static int IndexOfBitwise<T>(ReadOnlySpan<T> span, T value) =>
            IndexOfBits(Address(span), span.Length, CoreIntrinsics.SizeOf<T>(), Unsafe.AsPointer(ref value));
    }
}
