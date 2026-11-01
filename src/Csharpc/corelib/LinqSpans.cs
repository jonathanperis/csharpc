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

        // .NET's MinFloat / MaxFloat over a span: NaN is the minimum (returned at once), Max skips leading NaNs;
        // ties keep the first value (-0.0 and 0.0 compare equal).
        internal static double Min(ReadOnlySpan<double> span)
        {
            double value = span[0];
            if (double.IsNaN(value)) return value;
            for (int i = 1; i < span.Length; i++)
            {
                double current = span[i];
                if (current < value) value = current;
                else if (double.IsNaN(current)) return current;
            }
            return value;
        }

        internal static double Max(ReadOnlySpan<double> span)
        {
            double value = span[0];
            int i = 1;
            while (double.IsNaN(value))
            {
                if (i == span.Length) return value;
                value = span[i++];
            }
            for (; i < span.Length; i++)
            {
                double current = span[i];
                if (current > value) value = current;
            }
            return value;
        }

        internal static float Min(ReadOnlySpan<float> span)
        {
            float value = span[0];
            if (float.IsNaN(value)) return value;
            for (int i = 1; i < span.Length; i++)
            {
                float current = span[i];
                if (current < value) value = current;
                else if (float.IsNaN(current)) return current;
            }
            return value;
        }

        internal static float Max(ReadOnlySpan<float> span)
        {
            float value = span[0];
            int i = 1;
            while (float.IsNaN(value))
            {
                if (i == span.Length) return value;
                value = span[i++];
            }
            for (; i < span.Length; i++)
            {
                float current = span[i];
                if (current > value) value = current;
            }
            return value;
        }

        /// <summary>Contains over a span: a bitwise search for types whose equality is their bits, else EqualityComparer&lt;T&gt;.Default.</summary>
        internal static bool Contains<T>(ReadOnlySpan<T> span, T value)
        {
            if (CoreIntrinsics.IsBitwiseEquatable<T>())
                return IndexOfBits(Address(span), span.Length, CoreIntrinsics.SizeOf<T>(), Unsafe.AsPointer(ref value)) >= 0;
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
