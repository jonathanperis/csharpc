// MemoryExtensions.ToLowerInvariant / ToUpperInvariant(ReadOnlySpan<char>, Span<char>): the invariant case
// mapping into a destination span (X.509 name and curve-name handling in System.Security.Cryptography).
// .NET: -1 when the destination is too short, an ArgumentException for overlapping spans; the culture is not consulted.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.MemoryExtensions")]
    internal static class MemoryExtensionsCase
    {
        public static int ToLowerInvariant(ReadOnlySpan<char> source, Span<char> destination)
        {
            if (source.Overlaps(destination)) throw new InvalidOperationException("This operation is invalid on overlapping buffers.");
            if (destination.Length < source.Length) return -1;
            for (int i = 0; i < source.Length; i++) destination[i] = char.ToLowerInvariant(source[i]);
            return source.Length;
        }

        // CoreLib-internal MemoryExtensions members the framework's own IL calls (issue #191 part B).
        public static int IndexOfAnyWhiteSpace(ReadOnlySpan<char> span)
        {
            for (int i = 0; i < span.Length; i++) if (char.IsWhiteSpace(span[i])) return i;
            return -1;
        }

        /// <summary>The span without leading and trailing ASCII white space (the UTF-8 form of Trim: CoreLib's Number.SpanTrim over bytes).</summary>
        public static ReadOnlySpan<byte> TrimUtf8(ReadOnlySpan<byte> span)
        {
            int start = 0, end = span.Length;
            while (start < end && (span[start] == (byte)' ' || (span[start] >= (byte)'\t' && span[start] <= (byte)'\r'))) start++;
            while (end > start && (span[end - 1] == (byte)' ' || (span[end - 1] >= (byte)'\t' && span[end - 1] <= (byte)'\r'))) end--;
            return span.Slice(start, end - start);
        }

        public static int ToUpperInvariant(ReadOnlySpan<char> source, Span<char> destination)
        {
            if (source.Overlaps(destination)) throw new InvalidOperationException("This operation is invalid on overlapping buffers.");
            if (destination.Length < source.Length) return -1;
            for (int i = 0; i < source.Length; i++) destination[i] = char.ToUpperInvariant(source[i]);
            return source.Length;
        }
    }
}
