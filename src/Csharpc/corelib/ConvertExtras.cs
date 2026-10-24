// Convert's hex and Base64 text conversions, with .NET's argument checks and messages (Roslyn writes
// checksums as hex and embeds Base64 in diagnostics and source-generator caches).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Convert")]
    internal static class ConvertExtras
    {
        private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        public static string ToHexString(byte[] inArray)
        {
            if (inArray == null) throw new ArgumentNullException("inArray");
            return ToHexString(new ReadOnlySpan<byte>(inArray));
        }

        public static string ToHexString(byte[] inArray, int offset, int length)
        {
            CheckRange(inArray, offset, length);
            return ToHexString(new ReadOnlySpan<byte>(inArray, offset, length));
        }

        // ArgumentNullException.ThrowIfNull + ArgumentOutOfRangeException.ThrowIfNegative / ThrowIfGreaterThan, as .NET checks.
        private static void CheckRange(byte[] inArray, int offset, int length)
        {
            if (inArray == null) throw new ArgumentNullException("inArray");
            if (length < 0) throw new ArgumentOutOfRangeException("length", length, "length ('" + length + "') must be a non-negative value.");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", offset, "offset ('" + offset + "') must be a non-negative value.");
            int limit = inArray.Length - length;
            if (offset > limit) throw new ArgumentOutOfRangeException("offset", offset, "offset ('" + offset + "') must be less than or equal to '" + limit + "'.");
        }

        public static string ToHexString(ReadOnlySpan<byte> bytes) => Hex(bytes, "0123456789ABCDEF");
        public static string ToHexStringLower(byte[] inArray)
        {
            if (inArray == null) throw new ArgumentNullException("inArray");
            return Hex(new ReadOnlySpan<byte>(inArray), "0123456789abcdef");
        }
        public static string ToHexStringLower(ReadOnlySpan<byte> bytes) => Hex(bytes, "0123456789abcdef");

        private static string Hex(ReadOnlySpan<byte> bytes, string digits)
        {
            if (bytes.Length == 0) return string.Empty;
            if (bytes.Length > int.MaxValue / 2) throw new ArgumentOutOfRangeException("bytes", "The input array length must not exceed Int32.MaxValue / 2.");
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[2 * i] = digits[bytes[i] >> 4];
                chars[2 * i + 1] = digits[bytes[i] & 15];
            }
            return new string(chars);
        }

        public static byte[] FromHexString(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            return FromHexString(s.AsSpan());
        }

        public static byte[] FromHexString(ReadOnlySpan<char> chars)
        {
            if (chars.Length == 0) return Array.Empty<byte>();
            if ((chars.Length & 1) != 0) throw new FormatException("The input is not a valid hex string as its length is not a multiple of 2.");
            byte[] result = new byte[chars.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexValue(chars[2 * i]), lo = HexValue(chars[2 * i + 1]);
                if ((hi | lo) < 0) throw new FormatException("The input is not a valid hex string as it contains a non-hex character.");
                result[i] = (byte)(hi << 4 | lo);
            }
            return result;
        }

        private static int HexValue(char c) =>
            c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        public static string ToBase64String(byte[] inArray)
        {
            if (inArray == null) throw new ArgumentNullException("inArray");
            return Base64(new ReadOnlySpan<byte>(inArray));
        }

        public static string ToBase64String(byte[] inArray, Base64FormattingOptions options)
        {
            if (inArray == null) throw new ArgumentNullException("inArray");
            return Base64(new ReadOnlySpan<byte>(inArray), options);
        }

        public static string ToBase64String(byte[] inArray, int offset, int length) => ToBase64String(inArray, offset, length, Base64FormattingOptions.None);

        public static string ToBase64String(byte[] inArray, int offset, int length, Base64FormattingOptions options)
        {
            CheckRange(inArray, offset, length);
            return Base64(new ReadOnlySpan<byte>(inArray, offset, length), options);
        }

        public static string ToBase64String(ReadOnlySpan<byte> bytes, Base64FormattingOptions options) => Base64(bytes, options);

        private static string Base64(ReadOnlySpan<byte> bytes, Base64FormattingOptions options)
        {
            if ((uint)options > (uint)Base64FormattingOptions.InsertLineBreaks)
                throw new ArgumentException("Illegal enum value: " + (int)options + ".", "options");
            string text = Base64(bytes);
            if (options != Base64FormattingOptions.InsertLineBreaks || text.Length <= 76) return text;
            // MIME: "\r\n" after every 76 characters.
            var sb = new System.Text.StringBuilder(text.Length + text.Length / 76 * 2);
            for (int i = 0; i < text.Length; i += 76)
            {
                if (i > 0) sb.Append("\r\n");
                sb.Append(text, i, Math.Min(76, text.Length - i));
            }
            return sb.ToString();
        }

        private static string Base64(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0) return string.Empty;
            char[] chars = new char[(bytes.Length + 2) / 3 * 4];
            int o = 0, i = 0;
            for (; i + 2 < bytes.Length; i += 3)
            {
                int v = bytes[i] << 16 | bytes[i + 1] << 8 | bytes[i + 2];
                chars[o++] = Base64Alphabet[v >> 18];
                chars[o++] = Base64Alphabet[(v >> 12) & 63];
                chars[o++] = Base64Alphabet[(v >> 6) & 63];
                chars[o++] = Base64Alphabet[v & 63];
            }
            if (i < bytes.Length)
            {
                int v = bytes[i] << 16 | (i + 1 < bytes.Length ? bytes[i + 1] << 8 : 0);
                chars[o++] = Base64Alphabet[v >> 18];
                chars[o++] = Base64Alphabet[(v >> 12) & 63];
                chars[o++] = i + 1 < bytes.Length ? Base64Alphabet[(v >> 6) & 63] : '=';
                chars[o++] = '=';
            }
            return new string(chars);
        }

        public static byte[] FromBase64String(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            // .NET ignores white space (space, tab, CR, LF) anywhere in the input.
            int[] values = new int[s.Length];
            int count = 0, padding = 0;
            foreach (char c in s)
            {
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
                if (c == '=')
                {
                    padding++;
                    values[count++] = 0;
                    continue;
                }
                int v = Base64Alphabet.IndexOf(c);
                if (v < 0 || padding > 0) throw InvalidBase64();
                values[count++] = v;
            }
            if (count % 4 != 0 || padding > 2) throw InvalidBase64();
            byte[] result = new byte[count / 4 * 3 - padding];
            int r = 0;
            for (int i = 0; i < count; i += 4)
            {
                int v = values[i] << 18 | values[i + 1] << 12 | values[i + 2] << 6 | values[i + 3];
                if (r < result.Length) result[r++] = (byte)(v >> 16);
                if (r < result.Length) result[r++] = (byte)(v >> 8);
                if (r < result.Length) result[r++] = (byte)v;
            }
            return result;
        }

        private static FormatException InvalidBase64() =>
            new FormatException("The input is not a valid Base-64 string as it contains a non-base 64 character, more than two padding characters, or an illegal character among the padding characters.");
    }
}
