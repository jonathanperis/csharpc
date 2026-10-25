// System.Buffers.Text.Utf8Formatter for the integers (Utf8Formatter.Integer.cs): 'G'/'D' (with a precision:
// zero-padded to that many digits) and 'X'/'x', in invariant digits; false when the destination is too small.
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Buffers.Text.Utf8Formatter")]
    internal static class Utf8FormatterMembers
    {
        public static bool TryFormat(int value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-(long)value) : (ulong)value, (uint)value, destination, out bytesWritten, format);
        public static bool TryFormat(long value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-value) : (ulong)value, (ulong)value, destination, out bytesWritten, format);
        public static bool TryFormat(uint value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(ulong value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(short value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-(long)value) : (ulong)value, (ushort)value, destination, out bytesWritten, format);
        public static bool TryFormat(ushort value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(byte value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(false, value, value, destination, out bytesWritten, format);
        public static bool TryFormat(sbyte value, Span<byte> destination, out int bytesWritten, StandardFormat format = default) =>
            TryFormatInteger(value < 0, value < 0 ? (ulong)(-(long)value) : (ulong)value, (byte)value, destination, out bytesWritten, format);

        private static bool TryFormatInteger(bool negative, ulong magnitude, ulong bits, Span<byte> destination, out int bytesWritten, StandardFormat format)
        {
            bytesWritten = 0;
            char symbol = format.IsDefault ? 'G' : format.Symbol;
            int precision = format.HasPrecision ? format.Precision : 0;
            string digits;
            switch (symbol)
            {
                case 'G' or 'g' or 'D' or 'd':
                    digits = magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 'X' or 'x':
                    digits = bits.ToString(symbol == 'X' ? "X" : "x", System.Globalization.CultureInfo.InvariantCulture);
                    negative = false;
                    break;
                default:
                    throw new NotSupportedException("Utf8Formatter standard format '" + symbol + "' is not supported by csharpc yet.");
            }
            if (digits.Length < precision) digits = new string('0', precision - digits.Length) + digits;
            int length = digits.Length + (negative ? 1 : 0);
            if (destination.Length < length) return false;
            int at = 0;
            if (negative) destination[at++] = (byte)'-';
            for (int i = 0; i < digits.Length; i++) destination[at++] = (byte)digits[i];
            bytesWritten = length;
            return true;
        }
    }
}
