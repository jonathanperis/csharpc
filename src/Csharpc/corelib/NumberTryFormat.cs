// TryFormat of the numeric primitives (ISpanFormattable / IUtf8SpanFormattable): the text ToString(format,
// provider) gives — what .NET's TryFormat writes — copied when it fits; nothing written (0) when it doesn't.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class NumberTryFormat
    {
        internal static bool Copy(string text, Span<char> destination, out int charsWritten)
        {
            if (text.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            text.AsSpan().CopyTo(destination);
            charsWritten = text.Length;
            return true;
        }

        internal static bool CopyUtf8(string text, Span<byte> destination, out int bytesWritten)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            if (bytes.Length > destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            bytes.AsSpan().CopyTo(destination);
            bytesWritten = bytes.Length;
            return true;
        }

        internal static string Format(ReadOnlySpan<char> format) => format.Length == 0 ? null : format.ToString();
    }

    [ShadowMembers("System.Int32")]
    internal static class Int32TryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(int value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(int value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.UInt32")]
    internal static class UInt32TryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(uint value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(uint value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.Int64")]
    internal static class Int64TryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(long value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(long value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.UInt64")]
    internal static class UInt64TryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(ulong value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(ulong value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.Int16")]
    internal static class Int16TryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(short value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(short value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.UInt16")]
    internal static class UInt16TryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(ushort value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(ushort value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.Byte")]
    internal static class ByteTryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(byte value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(byte value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.SByte")]
    internal static class SByteTryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(sbyte value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(sbyte value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.Double")]
    internal static class DoubleTryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(double value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(double value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }

    [ShadowMembers("System.Single")]
    internal static class SingleTryFormat
    {
        [ShadowInstance]
        public static bool TryFormat(float value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.Copy(value.ToString(NumberTryFormat.Format(format), provider), destination, out charsWritten);

        [ShadowInstance]
        public static bool TryFormat(float value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            NumberTryFormat.CopyUtf8(value.ToString(NumberTryFormat.Format(format), provider), utf8Destination, out bytesWritten);
    }
}
