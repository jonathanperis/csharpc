// System.Text.Ascii's transcoding, equality and case conversion (Kestrel parses and writes HTTP headers
// with them), with .NET's results: an operation stops at the first non-ASCII element (InvalidData) or
// when the destination is full (DestinationTooSmall), having written what it converted so far.
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Text.Ascii")]
    internal static unsafe class AsciiMembers
    {

        // ---- transcoding ----------------------------------------------------------------------------
        public static OperationStatus ToUtf16(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
        {
            int n = Math.Min(source.Length, destination.Length);
            int i = 0;
            for (; i < n && source[i] <= 0x7F; i++) destination[i] = (char)source[i];
            charsWritten = i;
            return i < n ? OperationStatus.InvalidData : source.Length <= destination.Length ? OperationStatus.Done : OperationStatus.DestinationTooSmall;
        }

        public static OperationStatus FromUtf16(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten)
        {
            int n = Math.Min(source.Length, destination.Length);
            int i = 0;
            for (; i < n && source[i] <= 0x7F; i++) destination[i] = (byte)source[i];
            bytesWritten = i;
            return i < n ? OperationStatus.InvalidData : source.Length <= destination.Length ? OperationStatus.Done : OperationStatus.DestinationTooSmall;
        }

        // ---- equality: equal lengths, every element ASCII and equal (ignoring ASCII case) ----------
        public static bool Equals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] > 0x7F || left[i] != right[i]) return false;
            return true;
        }

        public static bool Equals(ReadOnlySpan<byte> left, ReadOnlySpan<char> right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] > 0x7F || left[i] != right[i]) return false;
            return true;
        }

        public static bool Equals(ReadOnlySpan<char> left, ReadOnlySpan<byte> right) => Equals(right, left);

        public static bool Equals(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] > 0x7F || left[i] != right[i]) return false;
            return true;
        }

        private static bool EqualIgnoringCase(uint a, uint b)
        {
            if (a > 0x7F || b > 0x7F) return false;
            if (a == b) return true;
            uint lowerA = a - 'A' <= 'Z' - 'A' ? a | 0x20 : a;
            uint lowerB = b - 'A' <= 'Z' - 'A' ? b | 0x20 : b;
            return lowerA == lowerB;
        }

        public static bool EqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!EqualIgnoringCase(left[i], right[i])) return false;
            return true;
        }

        public static bool EqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<char> right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!EqualIgnoringCase(left[i], right[i])) return false;
            return true;
        }

        public static bool EqualsIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<byte> right) => EqualsIgnoreCase(right, left);

        public static bool EqualsIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!EqualIgnoringCase(left[i], right[i])) return false;
            return true;
        }

        // ---- case conversion --------------------------------------------------------------------------
        private static uint Change(uint c, bool upper) =>
            upper ? (c - 'a' <= 'z' - 'a' ? c & ~0x20u : c) : (c - 'A' <= 'Z' - 'A' ? c | 0x20 : c);

        private static void CheckOverlap(void* source, int sourceBytes, void* destination, int destinationBytes)
        {
            byte* s = (byte*)source, d = (byte*)destination;
            if (sourceBytes > 0 && destinationBytes > 0 && s < d + destinationBytes && d < s + sourceBytes)
                throw new InvalidOperationException("This operation is invalid on overlapping buffers.");
        }

        public static OperationStatus ToUpper(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) => Change(source, destination, true, out bytesWritten);
        public static OperationStatus ToLower(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) => Change(source, destination, false, out bytesWritten);
        public static OperationStatus ToUpper(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten) => Change(source, destination, true, out charsWritten);
        public static OperationStatus ToLower(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten) => Change(source, destination, false, out charsWritten);
        public static OperationStatus ToUpper(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten) => Change(source, destination, true, out charsWritten);
        public static OperationStatus ToLower(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten) => Change(source, destination, false, out charsWritten);
        public static OperationStatus ToUpper(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten) => Change(source, destination, true, out bytesWritten);
        public static OperationStatus ToLower(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten) => Change(source, destination, false, out bytesWritten);

        private static OperationStatus Change(ReadOnlySpan<byte> source, Span<byte> destination, bool upper, out int written)
        {
            fixed (byte* s = source) fixed (byte* d = destination) CheckOverlap(s, source.Length, d, destination.Length);
            int n = Math.Min(source.Length, destination.Length), i = 0;
            for (; i < n && source[i] <= 0x7F; i++) destination[i] = (byte)Change(source[i], upper);
            written = i;
            return Status(i, n, source.Length, destination.Length);
        }

        private static OperationStatus Change(ReadOnlySpan<char> source, Span<char> destination, bool upper, out int written)
        {
            fixed (char* s = source) fixed (char* d = destination) CheckOverlap(s, source.Length * 2, d, destination.Length * 2);
            int n = Math.Min(source.Length, destination.Length), i = 0;
            for (; i < n && source[i] <= 0x7F; i++) destination[i] = (char)Change(source[i], upper);
            written = i;
            return Status(i, n, source.Length, destination.Length);
        }

        private static OperationStatus Change(ReadOnlySpan<byte> source, Span<char> destination, bool upper, out int written)
        {
            fixed (byte* s = source) fixed (char* d = destination) CheckOverlap(s, source.Length, d, destination.Length * 2);
            int n = Math.Min(source.Length, destination.Length), i = 0;
            for (; i < n && source[i] <= 0x7F; i++) destination[i] = (char)Change(source[i], upper);
            written = i;
            return Status(i, n, source.Length, destination.Length);
        }

        private static OperationStatus Change(ReadOnlySpan<char> source, Span<byte> destination, bool upper, out int written)
        {
            fixed (char* s = source) fixed (byte* d = destination) CheckOverlap(s, source.Length * 2, d, destination.Length);
            int n = Math.Min(source.Length, destination.Length), i = 0;
            for (; i < n && source[i] <= 0x7F; i++) destination[i] = (byte)Change(source[i], upper);
            written = i;
            return Status(i, n, source.Length, destination.Length);
        }

        private static OperationStatus Status(int converted, int attempted, int sourceLength, int destinationLength) =>
            converted < attempted ? OperationStatus.InvalidData : sourceLength <= destinationLength ? OperationStatus.Done : OperationStatus.DestinationTooSmall;

        public static OperationStatus ToUpperInPlace(Span<byte> value, out int bytesWritten) => InPlace(value, true, out bytesWritten);
        public static OperationStatus ToLowerInPlace(Span<byte> value, out int bytesWritten) => InPlace(value, false, out bytesWritten);
        public static OperationStatus ToUpperInPlace(Span<char> value, out int charsWritten) => InPlace(value, true, out charsWritten);
        public static OperationStatus ToLowerInPlace(Span<char> value, out int charsWritten) => InPlace(value, false, out charsWritten);

        private static OperationStatus InPlace(Span<byte> value, bool upper, out int written)
        {
            int i = 0;
            for (; i < value.Length && value[i] <= 0x7F; i++) value[i] = (byte)Change(value[i], upper);
            written = i;
            return i == value.Length ? OperationStatus.Done : OperationStatus.InvalidData;
        }

        private static OperationStatus InPlace(Span<char> value, bool upper, out int written)
        {
            int i = 0;
            for (; i < value.Length && value[i] <= 0x7F; i++) value[i] = (char)Change(value[i], upper);
            written = i;
            return i == value.Length ? OperationStatus.Done : OperationStatus.InvalidData;
        }
    }
}
