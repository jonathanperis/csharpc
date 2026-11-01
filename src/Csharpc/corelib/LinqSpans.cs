// Enumerable over arrays and List<T> as spans, as .NET's Enumerable does (TryGetSpan): the aggregates read
// the elements directly instead of through IEnumerator<T>, in the same order and with the same overflow,
// NaN and empty-sequence rules. Integer sums, min/max and searches run in src/runtime/span.c (portable
// vector code; csharpc has no SIMD in C# yet).
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe class LinqSpans
    {
        /// <summary>
        /// .NET's Enumerable.TryGetSpan: an array of a value type (reference-type arrays, which may be covariant, are
        /// left to the enumerator: the same elements either way) or a List&lt;TSource&gt; (exactly: a derived class may
        /// re-implement IEnumerable&lt;T&gt;).
        /// </summary>
        internal static bool TryGetSpan<TSource>(IEnumerable<TSource> source, out ReadOnlySpan<TSource> span)
        {
            if (CoreIntrinsics.IsValueType<TSource>() && source is TSource[] array)
            {
                span = array;
                return true;
            }
            if (CoreIntrinsics.IsExactClass<ListImpl<TSource>>(source))
            {
                ListImpl<TSource> list = CoreIntrinsics.ReinterpretAs<ListImpl<TSource>>(source);
                span = new ReadOnlySpan<TSource>(list._items, 0, list._size);
                return true;
            }
            span = default;
            return false;
        }

        private static void* Address<T>(ReadOnlySpan<T> span) => Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));

        [DllImport("__Internal", EntryPoint = "csrt_linq_sum_i32")]
        private static extern int SumI32(int* p, int length, int* result);

        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_i32")]
        private static extern int MinMaxI32(int* p, int length, int max);

        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_i64")]
        private static extern long MinMaxI64(long* p, int length, int max);

        [DllImport("__Internal", EntryPoint = "csrt_span_index_of")]
        private static extern int IndexOfBits(void* p, int length, long elementSize, void* value);

        internal static int Sum(ReadOnlySpan<int> span)
        {
            int result;
            if (SumI32((int*)Address(span), span.Length, &result) != 0) throw new OverflowException();
            return result;
        }

        // Vector<long> has two lanes at 16 bytes: .NET sums longs one at a time then (Count > 2 is required).
        internal static long Sum(ReadOnlySpan<long> span)
        {
            long sum = 0;
            for (int i = 0; i < span.Length; i++) sum = checked(sum + span[i]);
            return sum;
        }

        // Floating-point sums are sequential (the order decides the rounding), floats accumulated in double.
        internal static double Sum(ReadOnlySpan<double> span)
        {
            double sum = 0;
            for (int i = 0; i < span.Length; i++) sum += span[i];
            return sum;
        }

        internal static double Sum(ReadOnlySpan<float> span)
        {
            double sum = 0;
            for (int i = 0; i < span.Length; i++) sum += span[i];
            return sum;
        }

        internal static decimal Sum(ReadOnlySpan<decimal> span)
        {
            decimal sum = 0;
            for (int i = 0; i < span.Length; i++) sum += span[i];
            return sum;
        }

        /// <summary>Average's sum of ints (.NET's Sum&lt;int, long&gt;: element types differ, so never vectorized).</summary>
        internal static long SumWide(ReadOnlySpan<int> span)
        {
            long sum = 0;
            for (int i = 0; i < span.Length; i++) sum = checked(sum + span[i]);
            return sum;
        }

        internal static int MinMax(ReadOnlySpan<int> span, bool max) => MinMaxI32((int*)Address(span), span.Length, max ? 1 : 0);

        internal static long MinMax(ReadOnlySpan<long> span, bool max) => MinMaxI64((long*)Address(span), span.Length, max ? 1 : 0);

        // .NET's MinFloat / MaxFloat over a span (src/runtime/span.c): NaN is the minimum (the first one after the
        // first element: .NET's span loop tests span[0] last), Max skips NaNs; of equal extremes the first (-0.0
        // and 0.0 compare equal).
        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_f64")]
        private static extern double MinMaxF64(double* p, int length, int max, int allNaNFirst);

        [DllImport("__Internal", EntryPoint = "csrt_linq_min_max_f32")]
        private static extern float MinMaxF32(float* p, int length, int max, int allNaNFirst);

        internal static double Min(ReadOnlySpan<double> span) => MinMaxF64((double*)Address(span), span.Length, 0, 0);
        internal static double Max(ReadOnlySpan<double> span) => MinMaxF64((double*)Address(span), span.Length, 1, 0);
        internal static float Min(ReadOnlySpan<float> span) => MinMaxF32((float*)Address(span), span.Length, 0, 0);
        internal static float Max(ReadOnlySpan<float> span) => MinMaxF32((float*)Address(span), span.Length, 1, 0);

        /// <summary>
        /// MemoryExtensions.Min/Max (corelib StringExtras.cs) of a non-empty span of int, long, double or float with the
        /// default comparer: CompareTo's order, which for doubles is the same as Enumerable's except that a span of
        /// only NaNs gives its first element. False for other element types.
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

        /// <summary>Contains over a span: a bitwise search for types whose equality is their bits, else EqualityComparer&lt;T&gt;.Default.</summary>
        internal static bool Contains<T>(ReadOnlySpan<T> span, T value)
        {
            if (CoreIntrinsics.IsBitwiseEquatable<T>()) return IndexOfBitwise(span, value) >= 0;
            for (int i = 0; i < span.Length; i++)
                if (CoreIntrinsics.AreEqual(span[i], value)) return true;
            return false;
        }

        /// <summary>MemoryExtensions.SequenceEqual(span, other, comparer): lengths, then bits or the comparer, element by element.</summary>
        internal static bool SequenceEqual<T>(ReadOnlySpan<T> first, ReadOnlySpan<T> second, IEqualityComparer<T> comparer)
        {
            if (first.Length != second.Length) return false;
            if (comparer == null || (object)comparer == (object)EqualityComparer<T>.Default)
            {
                if (CoreIntrinsics.IsBitwiseEquatable<T>())
                    return first.Length == 0 || new ReadOnlySpan<byte>(Address(first), first.Length * CoreIntrinsics.SizeOf<T>())
                        .SequenceEqual(new ReadOnlySpan<byte>(Address(second), second.Length * CoreIntrinsics.SizeOf<T>()));
                for (int i = 0; i < first.Length; i++)
                    if (!CoreIntrinsics.AreEqual(first[i], second[i])) return false;
                return true;
            }
            for (int i = 0; i < first.Length; i++)
                if (!comparer.Equals(first[i], second[i])) return false;
            return true;
        }
    }
}
