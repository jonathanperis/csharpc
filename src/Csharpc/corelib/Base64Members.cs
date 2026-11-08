// System.Buffers.Text.Base64's sizing and validation members (dotnet/runtime's Base64.cs and Base64Validator):
// whitespace (space, tab, CR, LF) is ignored, '=' pads the last quartet by one or two chars.
#nullable disable
using System;
using System.Buffers;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Buffers.Text.Base64")]
    internal static class Base64Members
    {
        private const int MaximumEncodeLength = (int.MaxValue / 4) * 3; // 1610612733

        public static int GetMaxEncodedToUtf8Length(int length)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)length, (uint)MaximumEncodeLength, "(uint)bytesLength");
            return ((length + 2) / 3) * 4;
        }

        public static int GetMaxDecodedFromUtf8Length(int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length, "base64Length");
            return (length >> 2) * 3;
        }

        // Base64.EncodeToUtf8 (dotnet/runtime's Base64Encoder): whole 3-byte groups while the destination has room for the
        // four characters; a 1 or 2 byte tail is padded with '=' when this is the final block, else left for the next call.
        public static OperationStatus EncodeToUtf8(ReadOnlySpan<byte> bytes, Span<byte> utf8, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            int read = 0, written = 0;
            while (bytes.Length - read >= 3)
            {
                if (utf8.Length - written < 4)
                {
                    bytesConsumed = read; bytesWritten = written;
                    return OperationStatus.DestinationTooSmall;
                }
                int group = (bytes[read] << 16) | (bytes[read + 1] << 8) | bytes[read + 2];
                utf8[written] = (byte)alphabet[group >> 18];
                utf8[written + 1] = (byte)alphabet[(group >> 12) & 63];
                utf8[written + 2] = (byte)alphabet[(group >> 6) & 63];
                utf8[written + 3] = (byte)alphabet[group & 63];
                read += 3; written += 4;
            }
            int rest = bytes.Length - read;
            OperationStatus status = OperationStatus.Done;
            if (rest > 0)
            {
                if (!isFinalBlock) status = OperationStatus.NeedMoreData;
                else if (utf8.Length - written < 4) status = OperationStatus.DestinationTooSmall;
                else
                {
                    int group = bytes[read] << 16 | (rest == 2 ? bytes[read + 1] << 8 : 0);
                    utf8[written] = (byte)alphabet[group >> 18];
                    utf8[written + 1] = (byte)alphabet[(group >> 12) & 63];
                    utf8[written + 2] = rest == 2 ? (byte)alphabet[(group >> 6) & 63] : (byte)'=';
                    utf8[written + 3] = (byte)'=';
                    read += rest; written += 4;
                }
            }
            bytesConsumed = read; bytesWritten = written;
            return status;
        }

        public static bool IsValid(ReadOnlySpan<char> base64Text) => IsValid(base64Text, out _);
        public static bool IsValid(ReadOnlySpan<byte> utf8Base64Text) => IsValid(utf8Base64Text, out _);

        public static bool IsValid(ReadOnlySpan<char> base64Text, out int decodedLength)
        {
            int[] units = new int[base64Text.Length];
            for (int i = 0; i < units.Length; i++) units[i] = base64Text[i];
            return Validate(units, out decodedLength);
        }

        public static bool IsValid(ReadOnlySpan<byte> utf8Base64Text, out int decodedLength)
        {
            int[] units = new int[utf8Base64Text.Length];
            for (int i = 0; i < units.Length; i++) units[i] = utf8Base64Text[i];
            return Validate(units, out decodedLength);
        }

        private static bool Validate(int[] text, out int decodedLength)
        {
            decodedLength = 0;
            int significant = 0, padding = 0;
            foreach (int c in text)
            {
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
                if (c == '=') { padding++; if (padding > 2) return false; significant++; continue; }
                if (padding > 0) return false; // data after padding
                bool alphabet = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/';
                if (!alphabet) return false;
                significant++;
            }
            if (significant % 4 != 0) return false;
            decodedLength = significant / 4 * 3 - padding;
            return true;
        }
    }
}
