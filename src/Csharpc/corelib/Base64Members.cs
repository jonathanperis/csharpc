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

        private static int Sextet(int c) =>
            c >= 'A' && c <= 'Z' ? c - 'A' : c >= 'a' && c <= 'z' ? c - 'a' + 26 : c >= '0' && c <= '9' ? c - '0' + 52 : c == '+' ? 62 : c == '/' ? 63 : -1;

        private static bool IsBase64Space(byte c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

        // Base64.DecodeFromUtf8 (dotnet/runtime's Base64Decoder, .NET 8+): white space is skipped, '=' pads the last quartet,
        // an invalid quartet stops before it (InvalidData), an incomplete one at the end of a non-final block asks for more.
        public static OperationStatus DecodeFromUtf8(ReadOnlySpan<byte> utf8, Span<byte> bytes, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
        {
            int start = 0, written = 0;
            int[] sextets = new int[4];
            while (true)
            {
                int j = start, count = 0, padding = 0;
                bool invalid = false;
                while (j < utf8.Length && count + padding < 4)
                {
                    byte c = utf8[j];
                    if (IsBase64Space(c)) { j++; continue; }
                    if (c == '=')
                    {
                        if (count < 2) { invalid = true; break; }
                        padding++; j++;
                        continue;
                    }
                    int sextet = padding == 0 ? Sextet(c) : -1;
                    if (sextet < 0) { invalid = true; break; }
                    sextets[count++] = sextet; j++;
                }
                if (!invalid && padding > 0 && count + padding != 4) invalid = true;
                if (invalid) { bytesConsumed = start; bytesWritten = written; return OperationStatus.InvalidData; }
                if (count == 0)
                {
                    bytesConsumed = j; bytesWritten = written;
                    return OperationStatus.Done;
                }
                if (count + padding < 4)
                {
                    bytesConsumed = start; bytesWritten = written;
                    return isFinalBlock ? OperationStatus.InvalidData : OperationStatus.NeedMoreData;
                }
                int produced = count == 4 ? 3 : count == 3 ? 2 : 1;
                if (bytes.Length - written < produced) { bytesConsumed = start; bytesWritten = written; return OperationStatus.DestinationTooSmall; }
                int group = (sextets[0] << 18) | (sextets[1] << 12) | (count > 2 ? sextets[2] << 6 : 0) | (count > 3 ? sextets[3] : 0);
                bytes[written] = (byte)(group >> 16);
                if (produced > 1) bytes[written + 1] = (byte)(group >> 8);
                if (produced > 2) bytes[written + 2] = (byte)group;
                written += produced;
                start = j;
                if (padding > 0)
                {
                    // Only white space may follow the padding.
                    for (int k = j; k < utf8.Length; k++)
                        if (!IsBase64Space(utf8[k])) { bytesConsumed = j; bytesWritten = written; return OperationStatus.InvalidData; }
                    bytesConsumed = utf8.Length; bytesWritten = written;
                    return OperationStatus.Done;
                }
            }
        }

        public static OperationStatus DecodeFromUtf8InPlace(Span<byte> buffer, out int bytesWritten)
        {
            byte[] copy = buffer.ToArray();
            OperationStatus status = DecodeFromUtf8(copy, buffer, out _, out int written, true);
            bytesWritten = status == OperationStatus.Done ? written : 0;
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
