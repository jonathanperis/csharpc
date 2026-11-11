// MemoryExtensions.TryWrite(Span<char>, ref TryWriteInterpolatedStringHandler, out int): the handler is CoreLib's own (the upstream slice,
// src/Csharpc/upstream-types.txt); TryWrite reads the two fields it sets, as upstream's does.
#nullable disable
using System;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.MemoryExtensions")]
    internal static class TryWriteMembers
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_success")]
        private static extern ref bool Success(ref MemoryExtensions.TryWriteInterpolatedStringHandler handler);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_pos")]
        private static extern ref int Position(ref MemoryExtensions.TryWriteInterpolatedStringHandler handler);

        public static bool TryWrite(Span<char> destination, ref MemoryExtensions.TryWriteInterpolatedStringHandler handler, out int charsWritten)
        {
            if (Success(ref handler))
            {
                charsWritten = Position(ref handler);
                return true;
            }
            charsWritten = 0;
            return false;
        }

        public static bool TryWrite(Span<char> destination, IFormatProvider provider, ref MemoryExtensions.TryWriteInterpolatedStringHandler handler, out int charsWritten) =>
            TryWrite(destination, ref handler, out charsWritten);
    }
}
