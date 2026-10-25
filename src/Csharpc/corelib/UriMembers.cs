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

        private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.' || c == '_' || c == '~';

        private static string Escape(ReadOnlySpan<char> chars, string backing)
        {
            int first = 0;
            while (first < chars.Length && IsUnreserved(chars[first])) first++;
            if (first == chars.Length) return backing ?? chars.ToString();
            var builder = new System.Text.StringBuilder(chars.Length + 16);
            builder.Append(chars.Slice(0, first));
            Span<byte> utf8 = stackalloc byte[4];
            for (int i = first; i < chars.Length;)
            {
                char c = chars[i];
                if (c < 0x80)
                {
                    if (IsUnreserved(c)) builder.Append(c);
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

        // As .NET: no argument checks (a null pattern or a negative index fail on the indexing).
        public static bool IsHexEncoding(string pattern, int index) =>
            pattern.Length - index >= 3 && pattern[index] == '%'
            && char.IsAsciiHexDigit(pattern[index + 1]) && char.IsAsciiHexDigit(pattern[index + 2]);
    }
}
