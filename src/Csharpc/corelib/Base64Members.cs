// System.Buffers.Text.Base64's sizing and validation members (dotnet/runtime's Base64.cs and Base64Validator):
// whitespace (space, tab, CR, LF) is ignored, '=' pads the last quartet by one or two chars.
#nullable disable
using System;

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
