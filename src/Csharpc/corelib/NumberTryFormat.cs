// TryFormat over ToString's text, for the corelib types whose TryFormat still formats a string first (DateOnly, TimeOnly, Int128's
// IUtf8SpanFormattable); the primitives' TryFormat members are the framework's own, hosted from their IL (#426).
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    internal static class NumberTryFormat
    {
        public static bool Copy(string text, Span<char> destination, out int charsWritten)
        {
            if (text.Length > destination.Length) { charsWritten = 0; return false; }
            text.AsSpan().CopyTo(destination);
            charsWritten = text.Length;
            return true;
        }

        public static bool CopyUtf8(string text, Span<byte> destination, out int bytesWritten)
        {
            // Number text is ASCII (invariant digits) unless a culture's symbols are not.
            int count = Encoding.UTF8.GetByteCount(text);
            if (count > destination.Length) { bytesWritten = 0; return false; }
            bytesWritten = Encoding.UTF8.GetBytes(text, destination);
            return true;
        }

        public static string Format(ReadOnlySpan<char> format) => format.Length == 0 ? null : new string(format);
    }
}
