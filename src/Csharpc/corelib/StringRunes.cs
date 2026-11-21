// string.EnumerateRunes() and MemoryExtensions.EnumerateRunes(span): the members that return the framework's rune enumerators (Rune and its
// enumerators come from CoreLib's IL, src/Csharpc/upstream-types/System.Text.Rune.txt; String's members stay the corelib's).
#nullable disable
using System.Runtime.CompilerServices;
using System.Text;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.String")]
    internal static class StringRunes
    {
        [ShadowInstance] public static StringRuneEnumerator EnumerateRunes(string s) => NewStringRuneEnumerator(s);

        // StringRuneEnumerator(string) is internal to CoreLib.
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        private static extern StringRuneEnumerator NewStringRuneEnumerator(string value);
    }

    [ShadowMembers("System.MemoryExtensions")]
    internal static class SpanRunes
    {
        public static SpanRuneEnumerator EnumerateRunes(System.ReadOnlySpan<char> span) => NewSpanRuneEnumerator(span);
        public static SpanRuneEnumerator EnumerateRunes(System.Span<char> span) => NewSpanRuneEnumerator(span);

        // SpanRuneEnumerator(ReadOnlySpan<char>) is internal to CoreLib.
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        private static extern SpanRuneEnumerator NewSpanRuneEnumerator(System.ReadOnlySpan<char> buffer);
    }
}
