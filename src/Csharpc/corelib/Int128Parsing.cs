// Int128.Parse / TryParse and UInt128's: decimal digits with an optional sign and surrounding white space
// (NumberStyles.Integer, the default), under the culture's sign strings, with .NET's exceptions and
// messages. Other styles (thousands separators, hex, currency) are an explicit NotSupportedException:
// csharpc has no 128-bit number-style parser yet.
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    internal enum Int128ParseStatus { Ok, Format, Overflow }

    internal static class Int128Parser
    {
        // (2^128 - 1) / 10 and its remainder digit: a magnitude may take one more digit while below the former,
        // or equal to it with a digit up to 5.
        private static readonly UInt128 s_maxDiv10 = ((UInt128)0x1999999999999999UL << 64) | 0x9999999999999999UL;

        private static bool IsWhite(char c) => c == ' ' || (c >= '\t' && c <= '\r');

        /// <summary>Digits with NumberStyles.Integer's sign and white space; the magnitude and sign, or the failure.</summary>
        internal static Int128ParseStatus Parse(ReadOnlySpan<char> text, NumberStyles style, IFormatProvider provider, out UInt128 magnitude, out bool negative)
        {
            if (style != NumberStyles.Integer && style != NumberStyles.None && style != NumberStyles.AllowLeadingSign
                && style != (NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite) && style != NumberStyles.AllowLeadingWhite && style != NumberStyles.AllowTrailingWhite
                && style != (NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign) && style != (NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign))
                throw new NotSupportedException("128-bit integer parsing with NumberStyles." + style + " is not supported by csharpc yet.");
            magnitude = 0;
            negative = false;
            NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);
            int i = 0, end = text.Length;
            if ((style & NumberStyles.AllowLeadingWhite) != 0) while (i < end && IsWhite(text[i])) i++;
            if ((style & NumberStyles.AllowLeadingSign) != 0)
            {
                string positive = info.PositiveSign, minus = info.NegativeSign;
                if (minus.Length > 0 && text.Slice(i).StartsWith(minus.AsSpan())) { negative = true; i += minus.Length; }
                else if (positive.Length > 0 && text.Slice(i).StartsWith(positive.AsSpan())) i += positive.Length;
            }
            int digitsStart = i;
            bool overflow = false;
            UInt128 value = 0;
            for (; i < end && text[i] >= '0' && text[i] <= '9'; i++)
            {
                uint d = (uint)(text[i] - '0');
                if (value > s_maxDiv10 || (value == s_maxDiv10 && d > 5)) overflow = true;
                else value = value * 10 + d;
            }
            if (i == digitsStart) return Int128ParseStatus.Format;
            // trailing white space, and NUL characters, which .NET allows at the end
            if ((style & NumberStyles.AllowTrailingWhite) != 0) while (i < end && IsWhite(text[i])) i++;
            while (i < end && text[i] == '\0') i++;
            if (i != end) return Int128ParseStatus.Format;
            if (overflow) return Int128ParseStatus.Overflow;
            magnitude = value;
            return Int128ParseStatus.Ok;
        }

        internal static Int128ParseStatus ParseSigned(ReadOnlySpan<char> text, NumberStyles style, IFormatProvider provider, out Int128 result)
        {
            result = 0;
            var status = Parse(text, style, provider, out UInt128 magnitude, out bool negative);
            if (status != Int128ParseStatus.Ok) return status;
            UInt128 limit = (UInt128)1 << 127;
            if (negative ? magnitude > limit : magnitude >= limit) return Int128ParseStatus.Overflow;
            result = negative ? -(Int128)magnitude : (Int128)magnitude;
            return Int128ParseStatus.Ok;
        }

        internal static Int128ParseStatus ParseUnsigned(ReadOnlySpan<char> text, NumberStyles style, IFormatProvider provider, out UInt128 result)
        {
            result = 0;
            var status = Parse(text, style, provider, out UInt128 magnitude, out bool negative);
            if (status != Int128ParseStatus.Ok) return status;
            if (negative && magnitude != 0) return Int128ParseStatus.Overflow;
            result = magnitude;
            return Int128ParseStatus.Ok;
        }

        internal static Exception Failure(Int128ParseStatus status, string text, bool signed) =>
            status == Int128ParseStatus.Overflow
                ? new OverflowException("Value was either too large or too small for " + (signed ? "an Int128." : "a UInt128."))
                : new FormatException("The input string '" + text + "' was not in a correct format.");
    }

    [ShadowMembers("System.Int128")]
    internal static class Int128Members
    {
        /// <summary>ToString(IFormatProvider): the digits, with the provider's negative sign.</summary>
        public static string FormatWith(Int128 value, IFormatProvider provider)
        {
            string text = value.ToString();
            return value < 0 ? NumberFormatInfo.GetInstance(provider).NegativeSign + text.Substring(1) : text;
        }

        // IUtf8SpanFormattable (System.Text.Json writes Int128 properties through it): the digits, as ToString(provider); other formats
        // are not available for Int128 yet (ToString(format, provider) has no implementation either).
        [ShadowInstance] public static bool TryFormat(Int128 value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            format.Length == 0 ? NumberTryFormat.CopyUtf8(FormatWith(value, provider), utf8Destination, out bytesWritten)
                : throw new NotSupportedException("Int128.TryFormat with a format string is not supported by csharpc yet.");

        public static Int128 Parse(string s) => Parse(s, NumberStyles.Integer, null);
        public static Int128 Parse(string s, IFormatProvider provider) => Parse(s, NumberStyles.Integer, provider);
        public static Int128 Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static Int128 Parse(string s, NumberStyles style, IFormatProvider provider)
        {
            if (s == null) throw new ArgumentNullException("s");
            var status = Int128Parser.ParseSigned(s.AsSpan(), style, provider, out Int128 result);
            if (status != Int128ParseStatus.Ok) throw Int128Parser.Failure(status, s, true);
            return result;
        }

        public static Int128 Parse(ReadOnlySpan<char> s, IFormatProvider provider) => Parse(s, NumberStyles.Integer, provider);

        public static Int128 Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider provider = null)
        {
            var status = Int128Parser.ParseSigned(s, style, provider, out Int128 result);
            if (status != Int128ParseStatus.Ok) throw Int128Parser.Failure(status, s.ToString(), true);
            return result;
        }

        public static bool TryParse(string s, out Int128 result) => TryParse(s, NumberStyles.Integer, null, out result);
        public static bool TryParse(string s, IFormatProvider provider, out Int128 result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out Int128 result)
        {
            result = 0;
            return s != null && Int128Parser.ParseSigned(s.AsSpan(), style, provider, out result) == Int128ParseStatus.Ok;
        }

        // IUtf8SpanParsable: the text of the bytes, parsed as the chars would be.
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider, out Int128 result) =>
            TryParse(System.Text.Encoding.UTF8.GetString(utf8Text).AsSpan(), NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out Int128 result) => TryParse(s, NumberStyles.Integer, null, out result);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out Int128 result) => TryParse(s, NumberStyles.Integer, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider provider, out Int128 result) =>
            Int128Parser.ParseSigned(s, style, provider, out result) == Int128ParseStatus.Ok;
    }

    [ShadowMembers("System.UInt128")]
    internal static class UInt128Members
    {
        // The halves (internal upstream), which the framework's IL reads (Number.BigInteger.Multiply10).
        [ShadowInstance] public static ulong get_Lower(UInt128 value) => (ulong)value;
        [ShadowInstance] public static ulong get_Upper(UInt128 value) => (ulong)(value >> 64);

        public static string FormatWith(UInt128 value, IFormatProvider provider) => value.ToString();

        [ShadowInstance] public static bool TryFormat(UInt128 value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider provider = null) =>
            format.Length == 0 ? NumberTryFormat.CopyUtf8(value.ToString(), utf8Destination, out bytesWritten)
                : throw new NotSupportedException("UInt128.TryFormat with a format string is not supported by csharpc yet.");

        public static UInt128 Parse(string s) => Parse(s, NumberStyles.Integer, null);
        public static UInt128 Parse(string s, IFormatProvider provider) => Parse(s, NumberStyles.Integer, provider);
        public static UInt128 Parse(string s, NumberStyles style) => Parse(s, style, null);

        public static UInt128 Parse(string s, NumberStyles style, IFormatProvider provider)
        {
            if (s == null) throw new ArgumentNullException("s");
            var status = Int128Parser.ParseUnsigned(s.AsSpan(), style, provider, out UInt128 result);
            if (status != Int128ParseStatus.Ok) throw Int128Parser.Failure(status, s, false);
            return result;
        }

        public static UInt128 Parse(ReadOnlySpan<char> s, IFormatProvider provider) => Parse(s, NumberStyles.Integer, provider);

        public static UInt128 Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider provider = null)
        {
            var status = Int128Parser.ParseUnsigned(s, style, provider, out UInt128 result);
            if (status != Int128ParseStatus.Ok) throw Int128Parser.Failure(status, s.ToString(), false);
            return result;
        }

        public static bool TryParse(string s, out UInt128 result) => TryParse(s, NumberStyles.Integer, null, out result);
        public static bool TryParse(string s, IFormatProvider provider, out UInt128 result) => TryParse(s, NumberStyles.Integer, provider, out result);

        public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out UInt128 result)
        {
            result = 0;
            return s != null && Int128Parser.ParseUnsigned(s.AsSpan(), style, provider, out result) == Int128ParseStatus.Ok;
        }

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider provider, out UInt128 result) =>
            TryParse(System.Text.Encoding.UTF8.GetString(utf8Text).AsSpan(), NumberStyles.Integer, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out UInt128 result) => TryParse(s, NumberStyles.Integer, null, out result);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider provider, out UInt128 result) => TryParse(s, NumberStyles.Integer, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider provider, out UInt128 result) =>
            Int128Parser.ParseUnsigned(s, style, provider, out result) == Int128ParseStatus.Ok;
    }
}
