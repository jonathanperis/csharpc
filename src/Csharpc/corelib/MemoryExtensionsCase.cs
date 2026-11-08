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

        public static int ToUpperInvariant(ReadOnlySpan<char> source, Span<char> destination)
        {
            if (source.Overlaps(destination)) throw new InvalidOperationException("This operation is invalid on overlapping buffers.");
            if (destination.Length < source.Length) return -1;
            for (int i = 0; i < source.Length; i++) destination[i] = char.ToUpperInvariant(source[i]);
            return source.Length;
        }
    }
}
