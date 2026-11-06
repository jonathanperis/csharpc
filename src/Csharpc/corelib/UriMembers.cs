// Static helpers of System.Uri that ASP.NET Core's PathString and URL decoding call (the Uri type
// itself is Net/Uri.cs).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Uri")]
    internal static class UriMembers
    {
        public static bool IsHexDigit(char character) => char.IsAsciiHexDigit(character);

        // RFC 3986 unreserved characters stay; everything else is percent-encoded as UTF-8 with uppercase
        // hex digits, an ill-formed surrogate as U+FFFD (UriHelper.EscapeStringToBuilder).
        public static string EscapeDataString(string stringToEscape)
        {
            if (stringToEscape == null) throw new ArgumentNullException("stringToEscape");
            return Escape(stringToEscape.AsSpan(), stringToEscape);
        }

        public static string EscapeDataString(ReadOnlySpan<char> charsToEscape) => Escape(charsToEscape, null);

        // Obsolete: keeps the reserved characters too ("!#$&'()*+,/:;=?@[]"), and escapes '%'.
        public static string EscapeUriString(string stringToEscape)
        {
            if (stringToEscape == null) throw new ArgumentNullException("stringToEscape");
            return Escape(stringToEscape.AsSpan(), stringToEscape, true);
        }

        // false, writing nothing, when `destination` is shorter than the input or than the result.
        public static bool TryEscapeDataString(ReadOnlySpan<char> charsToEscape, Span<char> destination, out int charsWritten)
        {
            charsWritten = 0;
            if (destination.Length < charsToEscape.Length) return false;
            string escaped = Escape(charsToEscape, null);
            if (escaped.Length > destination.Length) return false;
            escaped.AsSpan().CopyTo(destination);
            charsWritten = escaped.Length;
            return true;
        }

        private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.' || c == '_' || c == '~';

        private static bool IsUnreservedOrReserved(char c) => IsUnreserved(c) || "!#$&'()*+,/:;=?@[]".IndexOf(c) >= 0;

        private static string Escape(ReadOnlySpan<char> chars, string backing, bool keepReserved = false)
        {
            int first = 0;
            while (first < chars.Length && (keepReserved ? IsUnreservedOrReserved(chars[first]) : IsUnreserved(chars[first]))) first++;
            if (first == chars.Length) return backing ?? chars.ToString();
            var builder = new System.Text.StringBuilder(chars.Length + 16);
            builder.Append(chars.Slice(0, first));
            Span<byte> utf8 = stackalloc byte[4];
            for (int i = first; i < chars.Length;)
            {
                char c = chars[i];
                if (c < 0x80)
                {
                    if (keepReserved ? IsUnreservedOrReserved(c) : IsUnreserved(c)) builder.Append(c);
                    else PercentEncode(builder, (byte)c);
                    i++;
                    continue;
                }
                if (System.Text.Rune.DecodeFromUtf16(chars.Slice(i), out System.Text.Rune rune, out int consumed) != System.Buffers.OperationStatus.Done)
                    rune = System.Text.Rune.ReplacementChar;
                i += consumed;
                rune.TryEncodeToUtf8(utf8, out int written);
                for (int b = 0; b < written; b++) PercentEncode(builder, utf8[b]);
            }
            return builder.ToString();
        }

        private static void PercentEncode(System.Text.StringBuilder builder, byte value)
        {
            const string Hex = "0123456789ABCDEF";
            builder.Append('%').Append(Hex[value >> 4]).Append(Hex[value & 0xF]);
        }

        public static string HexEscape(char character)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(character, '\xff');
            const string Hex = "0123456789ABCDEF";
            return new string(new[] { '%', Hex[character >> 4], Hex[character & 0xF] });
        }

        // The character a "%XX" at `index` stands for, or the character there; `index` moves past what was read.
        public static char HexUnescape(string pattern, ref int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, pattern.Length);
            if (pattern[index] == '%' && pattern.Length - index >= 3 && char.IsAsciiHexDigit(pattern[index + 1]) && char.IsAsciiHexDigit(pattern[index + 2]))
            {
                char unescaped = (char)((HexValue(pattern[index + 1]) << 4) | HexValue(pattern[index + 2]));
                index += 3;
                return unescaped;
            }
            return pattern[index++];
        }

        public static int FromHex(char digit)
        {
            if (!char.IsAsciiHexDigit(digit)) throw new ArgumentException("Value does not fall within the expected range.", "digit");
            return HexValue(digit);
        }

        private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

        // As .NET: no argument checks (a null pattern or a negative index fail on the indexing).
        public static bool IsHexEncoding(string pattern, int index) =>
            pattern.Length - index >= 3 && pattern[index] == '%'
            && char.IsAsciiHexDigit(pattern[index + 1]) && char.IsAsciiHexDigit(pattern[index + 2]);
    }
}
