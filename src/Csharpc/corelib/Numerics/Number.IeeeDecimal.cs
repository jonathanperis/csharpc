// The text side of the IEEE 754 decimal types (Decimal32, Decimal64, Decimal128; IeeeDecimal.cs): parsing into a decimal
// digit string and a power of ten (the cohort is kept: "1.50" is 150E-2, "0.000" a zero with exponent -3), and laying
// digits out with the standard and custom numeric format strings. Built on the number parser and formatter of
// Number.Parsing.Culture.cs and Number.Formatting.cs.
#nullable disable
using System;
using System.Globalization;
using NumberFormatInfo = CsharpcCoreLib.Globalization.NumberFormatInfo;

namespace CsharpcCoreLib
{
    internal static partial class Number
    {
        internal static void ThrowFormatFor(string s) => ThrowParseFormat(s);

        /// <summary>
        /// Text of an IEEE decimal: finite (value = digits * 10^exponent, digits without leading zeros, empty for a zero; sticky
        /// when non-zero digits followed the ones kept), or special (1 infinity, 2 NaN). False when the text isn't a number.
        /// </summary>
        internal static bool TryParseIeeeDecimal(string value, NumberStyles styles, NumberFormatInfo info, out bool negative, out int special, out string digits, out int exponent, out bool sticky)
        {
            negative = false; special = 0; digits = ""; exponent = 0; sticky = false;
            if (value == null) return false;
            Span<byte> buffer = stackalloc byte[96];
            var number = new ParseBuffer(ParseBuffer.KindDecimal, buffer);
            if (TryParseNumber(value, styles, ref number, info))
            {
                negative = number.IsNegative;
                var chars = new char[number.DigitsCount];
                for (int i = 0; i < chars.Length; i++) chars[i] = (char)number.Digits[i];
                digits = new string(chars);
                exponent = number.Scale - number.DigitsCount;
                sticky = number.HasNonZeroTail;
                return true;
            }
            // Leading and trailing whitespace around a special value is always consumed.
            string trimmed = value.TrimStart();
            if (TryMatchSpecialValueSymbol(trimmed, info.PositiveInfinitySymbol)) { special = 1; return true; }
            if (TryMatchSpecialValueSymbol(trimmed, info.NegativeInfinitySymbol)) { special = 1; negative = true; return true; }
            if (TryMatchSpecialValueSymbol(trimmed, info.NaNSymbol)) { special = 2; return true; }
            string positiveSign = info.PositiveSign;
            if (positiveSign.Length > 0 && trimmed.StartsWith(positiveSign, StringComparison.OrdinalIgnoreCase))
            {
                string afterSign = trimmed.Substring(positiveSign.Length);
                if (TryMatchSpecialValueSymbol(afterSign, info.PositiveInfinitySymbol)) { special = 1; return true; }
                if (TryMatchSpecialValueSymbol(afterSign, info.NaNSymbol)) { special = 2; return true; }
                return false;
            }
            string negativeSign = info.NegativeSign;
            if (negativeSign.Length > 0 && trimmed.StartsWith(negativeSign, StringComparison.OrdinalIgnoreCase))
            {
                if (TryMatchSpecialValueSymbol(trimmed.Substring(negativeSign.Length), info.NaNSymbol)) { special = 2; return true; }
                if (info.AllowHyphenDuringParsing() && trimmed.Length > 0 && trimmed[0] == '-' && TryMatchSpecialValueSymbol(trimmed.Substring(1), info.NaNSymbol)) { special = 2; return true; }
            }
            return false;
        }

        /// <summary>
        /// A finite IEEE decimal (digits * 10^exponent; digits keep their trailing zeros, empty for zero) formatted with a standard
        /// or custom format string. "G" without a precision shows every digit of the significand and goes scientific when the
        /// exponent is beyond them; the other standard formats round half to even on the digits.
        /// </summary>
        internal static string FormatIeeeDecimal(bool negative, string digits, int exponent, string format, NumberFormatInfo nfi)
        {
            Span<byte> storage = stackalloc byte[DigitStorage];
            var g = new DigitBuffer(storage);
            g.Neg = negative;
            g.IsFloat = true; // a negative zero keeps its sign
            for (int i = 0; i < digits.Length; i++) g.D[i] = (byte)digits[i];
            g.N = digits.Length;
            g.Exp10 = digits.Length == 0 ? 0 : digits.Length + exponent;
            char fmt = ParseFormatSpecifier(format, out int precision);
            if (fmt == 'R' || fmt == 'r') fmt = (char)(fmt - ('R' - 'G'));
            Span<char> chars = stackalloc char[64];
            var sb = new CharBuilder(chars);
            if (fmt == '\0')
            {
                while (g.N > 0 && g.D[g.N - 1] == (byte)'0') g.N--;
                if (g.N == 0) g.Exp10 = 0;
                FormatCustom(ref sb, ref g, format, nfi, true);
                return sb.Build();
            }
            if (fmt == 'D' || fmt == 'd' || fmt == 'X' || fmt == 'x' || fmt == 'B' || fmt == 'b') ThrowBadFormat();
            if ((fmt == 'G' || fmt == 'g') && (precision <= 0 || g.N == 0))
            {
                if (g.Neg) sb.Append(nfi.NegativeSign);
                if (g.N == 0) { g.D[0] = (byte)'0'; g.N = 1; g.Exp10 = 1 + exponent; } // a zero shows its exponent: 0.000, 0E+03
                FormatGeneral(ref sb, ref g, g.N, fmt == 'G' ? 'E' : 'e', false, nfi);
                return sb.Build();
            }
            FormatStandard(ref sb, ref g, fmt, precision, true, nfi); // ties go to even, as for the binary floating-point types
            return sb.Build();
        }
    }
}
