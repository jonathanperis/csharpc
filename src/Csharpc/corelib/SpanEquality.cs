// MemoryExtensions searches and comparisons with element equality (EqualityComparer<T>.Default, i.e.
// IEquatable<T>), as in .NET. The codegen (FunctionCodeGen.Spans.cs) compares the bytes of integers,
// chars, bools and enums itself; every other element type comes here, where equality is semantic:
// equal strings at different addresses, NaN == NaN, -0.0 == 0.0, user Equals.
// The IEqualityComparer<T> overloads (.NET 10+) are shadows of MemoryExtensions.
#nullable disable
using System;
using System.Collections.Generic;

namespace CsharpcCoreLib
{
    internal static class SpanEqualityOps
    {
        public static int IndexOf<T>(ReadOnlySpan<T> span, T value)
        {
            for (int i = 0; i < span.Length; i++)
                if (CoreIntrinsics.AreEqual(span[i], value)) return i;
            return -1;
        }

        public static int LastIndexOf<T>(ReadOnlySpan<T> span, T value)
        {
            for (int i = span.Length - 1; i >= 0; i--)
                if (CoreIntrinsics.AreEqual(span[i], value)) return i;
            return -1;
        }

        public static bool Contains<T>(ReadOnlySpan<T> span, T value) => IndexOf(span, value) >= 0;

        public static int IndexOfSpan<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value)
        {
            for (int i = 0; i <= span.Length - value.Length; i++)
                if (EqualAt(span, i, value, null)) return i;
            return -1;
        }

        public static bool SequenceEqual<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> other) =>
            span.Length == other.Length && EqualAt(span, 0, other, null);

        public static bool StartsWith<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value) =>
            value.Length <= span.Length && EqualAt(span, 0, value, null);

        public static bool EndsWith<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value) =>
            value.Length <= span.Length && EqualAt(span, span.Length - value.Length, value, null);

        public static bool StartsWithValue<T>(ReadOnlySpan<T> span, T value) => span.Length != 0 && CoreIntrinsics.AreEqual(span[0], value);

        public static bool EndsWithValue<T>(ReadOnlySpan<T> span, T value) => span.Length != 0 && CoreIntrinsics.AreEqual(span[span.Length - 1], value);

        internal static bool Equal<T>(T a, T b, IEqualityComparer<T> comparer) =>
            comparer != null ? comparer.Equals(a, b) : CoreIntrinsics.AreEqual(a, b);

        internal static bool EqualAt<T>(ReadOnlySpan<T> span, int start, ReadOnlySpan<T> value, IEqualityComparer<T> comparer)
        {
            for (int j = 0; j < value.Length; j++)
                if (!Equal(span[start + j], value[j], comparer)) return false;
            return true;
        }
    }

    [ShadowMembers("System.MemoryExtensions")]
    internal static class MemoryExtensionsComparers
    {
        // Overlaps (MemoryExtensions.cs): the byte offset between the starts, compared unsigned.
        public static bool Overlaps<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> other)
        {
            if (span.IsEmpty || other.IsEmpty) return false;
            nint byteOffset = System.Runtime.CompilerServices.Unsafe.ByteOffset(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span), ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other));
            nint size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            return (nuint)byteOffset < (nuint)(span.Length * size) || (nuint)byteOffset > (nuint)(-(other.Length * size));
        }

        public static bool Overlaps<T>(Span<T> span, ReadOnlySpan<T> other) => Overlaps((ReadOnlySpan<T>)span, other);

        public static bool Overlaps<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> other, out int elementOffset)
        {
            if (span.IsEmpty || other.IsEmpty)
            {
                elementOffset = 0;
                return false;
            }
            nint byteOffset = System.Runtime.CompilerServices.Unsafe.ByteOffset(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span), ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other));
            nint size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            if ((nuint)byteOffset < (nuint)(span.Length * size) || (nuint)byteOffset > (nuint)(-(other.Length * size)))
            {
                if (byteOffset % size != 0) throw new ArgumentException("Overlapping spans have mismatching alignment.");
                elementOffset = (int)(byteOffset / size);
                return true;
            }
            elementOffset = 0;
            return false;
        }

        public static bool Overlaps<T>(Span<T> span, ReadOnlySpan<T> other, out int elementOffset) => Overlaps((ReadOnlySpan<T>)span, other, out elementOffset);

        public static bool SequenceEqual<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> other, IEqualityComparer<T> comparer = null) =>
            span.Length == other.Length && SpanEqualityOps.EqualAt(span, 0, other, comparer);

        public static bool SequenceEqual<T>(Span<T> span, ReadOnlySpan<T> other, IEqualityComparer<T> comparer = null) =>
            SequenceEqual((ReadOnlySpan<T>)span, other, comparer);

        public static bool Contains<T>(ReadOnlySpan<T> span, T value, IEqualityComparer<T> comparer = null) => IndexOf(span, value, comparer) >= 0;

        public static int IndexOf<T>(ReadOnlySpan<T> span, T value, IEqualityComparer<T> comparer = null)
        {
            for (int i = 0; i < span.Length; i++)
                if (SpanEqualityOps.Equal(span[i], value, comparer)) return i;
            return -1;
        }

        public static int IndexOf<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T> comparer = null)
        {
            for (int i = 0; i <= span.Length - value.Length; i++)
                if (SpanEqualityOps.EqualAt(span, i, value, comparer)) return i;
            return -1;
        }

        public static int LastIndexOf<T>(ReadOnlySpan<T> span, T value, IEqualityComparer<T> comparer = null)
        {
            for (int i = span.Length - 1; i >= 0; i--)
                if (SpanEqualityOps.Equal(span[i], value, comparer)) return i;
            return -1;
        }

        public static int LastIndexOf<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T> comparer = null)
        {
            if (value.Length == 0) return span.Length;
            for (int i = span.Length - value.Length; i >= 0; i--)
                if (SpanEqualityOps.EqualAt(span, i, value, comparer)) return i;
            return -1;
        }

        public static bool StartsWith<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T> comparer = null) =>
            value.Length <= span.Length && SpanEqualityOps.EqualAt(span, 0, value, comparer);

        public static bool EndsWith<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T> comparer = null) =>
            value.Length <= span.Length && SpanEqualityOps.EqualAt(span, span.Length - value.Length, value, comparer);

        public static bool StartsWith<T>(ReadOnlySpan<T> span, T value, IEqualityComparer<T> comparer = null) =>
            span.Length != 0 && SpanEqualityOps.Equal(span[0], value, comparer);

        public static bool EndsWith<T>(ReadOnlySpan<T> span, T value, IEqualityComparer<T> comparer = null) =>
            span.Length != 0 && SpanEqualityOps.Equal(span[span.Length - 1], value, comparer);

        public static int CommonPrefixLength<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> other, IEqualityComparer<T> comparer)
        {
            int length = Math.Min(span.Length, other.Length);
            for (int i = 0; i < length; i++)
                if (!SpanEqualityOps.Equal(span[i], other[i], comparer)) return i;
            return length;
        }

        public static int CommonPrefixLength<T>(Span<T> span, ReadOnlySpan<T> other, IEqualityComparer<T> comparer) =>
            CommonPrefixLength((ReadOnlySpan<T>)span, other, comparer);
    }
}
