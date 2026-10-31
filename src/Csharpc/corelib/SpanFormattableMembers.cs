// ISpanFormattable / IUtf8SpanFormattable of decimal, char, DateTime and TimeSpan, for constrained calls through
// those interfaces (RazorSlices' Write overloads format values this way): exactly ToString(format, provider)'s
// text, copied when it fits (charsWritten/bytesWritten 0 otherwise), as .NET's TryFormat produce the same text.
#nullable disable
using System;
using System.Text;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Decimal")]
    internal static class DecimalTryFormat
    {
        [ShadowInstance] public static bool TryFormat(decimal value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance] public static bool TryFormat(decimal value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.DateTime")]
    internal static class DateTimeTryFormat
    {
        [ShadowInstance] public static bool TryFormat(DateTime value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance] public static bool TryFormat(DateTime value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.TimeSpan")]
    internal static class TimeSpanTryFormat
    {
        [ShadowInstance] public static bool TryFormat(TimeSpan value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance] public static bool TryFormat(TimeSpan value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    /// <summary>char's explicit implementations (Char.cs): the char itself, or its UTF-8 (new Rune(char) throws for a surrogate).</summary>
    [ShadowMembers("System.Char")]
    internal static class CharTryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(char value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
        {
            if (!destination.IsEmpty)
            {
                destination[0] = value;
                charsWritten = 1;
                return true;
            }
            charsWritten = 0;
            return false;
        }

        [ShadowInstance]
        public static bool TryFormat(char value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider provider) =>
            new Rune(value).TryEncodeToUtf8(utf8Destination, out bytesWritten);
    }
}
