// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Culture-aware number parsing: a port of .NET's TryParseNumber state machine and its helpers
// (src/libraries/Common/src/System/Number.Parsing.Common.cs) and of the conversions from its NumberBuffer
// (TryParseBinaryIntegerStyle, TryNumberBufferToBinaryInteger, TryParseFloat's special values,
// TryNumberToDecimal; System.Private.CoreLib/src/System/Number.Parsing.cs), dotnet/runtime release/11.0,
// commit bc123d0bd173; see THIRD-PARTY-NOTICES.md. The signs, separators, currency symbol and NaN/infinity
// symbols come from the provider's NumberFormatInfo. Digits become doubles through csharpc's correctly
// rounded DigitsToDouble (Number.Parsing.cs).
#nullable disable
using System;
using System.Globalization;
using NumberFormatInfo = CsharpcCoreLib.Globalization.NumberFormatInfo;

namespace CsharpcCoreLib
{
    /// <summary>.NET's NumberBuffer: value = 0.d1d2...dn * 10^Scale; Digits are ASCII and end with a 0 byte.</summary>
    internal ref struct ParseBuffer
    {
        public Span<byte> Digits;
        public int DigitsCount;
        public int Scale;
        public bool IsNegative;
        public bool HasNonZeroTail;
        public int Kind;

        public const int KindInteger = 0, KindFloatingPoint = 1, KindDecimal = 2;

        public ParseBuffer(int kind, Span<byte> digits)
        {
            Digits = digits;
            DigitsCount = 0;
            Scale = 0;
            IsNegative = false;
            HasNonZeroTail = false;
            Kind = kind;
            digits[0] = 0;
        }

        public byte Digit(int i) => i < Digits.Length ? Digits[i] : (byte)0;
    }

    internal static partial class Number
    {
        // ParsingStatus
        internal const int ParseFailed = 0, ParseOK = 1, ParseOverflow = 2;

        private static bool IsWhiteCh(uint ch) => (ch == 0x20) || ((ch - 0x09) <= (0x0D - 0x09));

        private static bool IsDigitCh(uint ch) => (ch - '0') <= 9;

        private static uint Ch(string s, int p, int end) => p < end ? s[p] : '\0';

        private static bool IsSpaceReplacingChar(uint c) => c == ' ' || c == ' ';

        private static uint NormalizeSpaceReplacingChar(uint c) => IsSpaceReplacingChar(c) ? ' ' : c;

        /// <summary>MatchChars: the index after `value` if s[p..] starts with it (NBSP and space are interchangeable), else -1.</summary>
        private static int MatchChars(string s, int p, int end, string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > end - p) return -1;
            for (int i = 0; i < value.Length; i++)
            {
                uint cp = s[p + i];
                uint val = value[i];
                if (cp != val && NormalizeSpaceReplacingChar(cp) != NormalizeSpaceReplacingChar(val)) return -1;
            }
            return p + value.Length;
        }

        private static int MatchNegativeSignChars(string s, int p, int end, NumberFormatInfo info)
        {
            int ret = MatchChars(s, p, end, info.NegativeSign);
            if (ret < 0 && info.AllowHyphenDuringParsing() && p < end && s[p] == '-') ret = p + 1;
            return ret;
        }

        private static int MatchSignChars(string s, int p, int end, NumberFormatInfo info, out bool isNegative)
        {
            if (info.HasInvariantNumberSigns)
            {
                if (p < end)
                {
                    char ch = s[p];
                    if (ch == '-') { isNegative = true; return p + 1; }
                    if (ch == '+') { isNegative = false; return p + 1; }
                }
                isNegative = false;
                return -1;
            }

            int positiveSignIndex = MatchChars(s, p, end, info.PositiveSign);
            if (positiveSignIndex >= 0 && info.PositiveSign.Length >= info.NegativeSign.Length)
            {
                isNegative = false;
                return positiveSignIndex;
            }

            int negativeSignIndex = MatchNegativeSignChars(s, p, end, info);
            if (positiveSignIndex < 0)
            {
                isNegative = negativeSignIndex >= 0;
                return negativeSignIndex;
            }

            if (negativeSignIndex < 0 || positiveSignIndex >= negativeSignIndex)
            {
                isNegative = false;
                return positiveSignIndex;
            }

            isNegative = true;
            return negativeSignIndex;
        }

        /// <summary>.NET's TryParseNumber over s[0..s.Length) (no AllowHexSpecifier / AllowBinarySpecifier).</summary>
        internal static bool TryParseNumber(string s, NumberStyles styles, ref ParseBuffer number, NumberFormatInfo info)
        {
            const int StateSign = 0x0001;
            const int StateParens = 0x0002;
            const int StateDigits = 0x0004;
            const int StateNonZero = 0x0008;
            const int StateDecimal = 0x0010;
            const int StateCurrency = 0x0020;

            int end = s.Length;
            string decSep;                  // decimal separator from NumberFormatInfo.
            string groupSep;                // group separator from NumberFormatInfo.
            string currSymbol = null;       // currency symbol from NumberFormatInfo.

            bool parsingCurrency = false;
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0)
            {
                currSymbol = info.CurrencySymbol;

                // The idea here is to match the currency separators and on failure match the number separators to keep the perf of VB's IsNumeric fast.
                // The values of decSep are setup to use the correct relevant separator (currency in the if part and decimal in the else part).
                decSep = info.CurrencyDecimalSeparator;
                groupSep = info.CurrencyGroupSeparator;
                parsingCurrency = true;
            }
            else
            {
                decSep = info.NumberDecimalSeparator;
                groupSep = info.NumberGroupSeparator;
            }

            int state = 0;
            int p = 0;
            uint ch = Ch(s, p, end);
            int next;

            while (true)
            {
                // Eat whitespace unless we've found a sign which isn't followed by a currency symbol.
                // "-Kr 1231.47" is legal but "- 1231.47" is not.
                if (!IsWhiteCh(ch) || (styles & NumberStyles.AllowLeadingWhite) == 0 || ((state & StateSign) != 0 && (state & StateCurrency) == 0 && info.NumberNegativePattern != 2))
                {
                    if (((styles & NumberStyles.AllowLeadingSign) != 0) && (state & StateSign) == 0 && ((next = MatchSignChars(s, p, end, info, out bool isNegative)) >= 0))
                    {
                        state |= StateSign;
                        number.IsNegative = isNegative;
                        p = next - 1;
                    }
                    else if (ch == '(' && ((styles & NumberStyles.AllowParentheses) != 0) && ((state & StateSign) == 0))
                    {
                        state |= StateSign | StateParens;
                        number.IsNegative = true;
                    }
                    else if (!string.IsNullOrEmpty(currSymbol) && (next = MatchChars(s, p, end, currSymbol)) >= 0)
                    {
                        state |= StateCurrency;
                        currSymbol = null;
                        // We already found the currency symbol. There should not be more currency symbols. Set
                        // currSymbol to NULL so that we won't search it again in the later code path.
                        p = next - 1;
                    }
                    else
                    {
                        break;
                    }
                }
                ch = Ch(s, ++p, end);
            }

            int digCount = 0;
            int digEnd = 0;
            int maxDigCount = number.Digits.Length - 1;
            int numberOfTrailingZeros = 0;

            while (true)
            {
                if (IsDigitCh(ch))
                {
                    state |= StateDigits;

                    if (ch != '0' || (state & StateNonZero) != 0)
                    {
                        if (digCount < maxDigCount)
                        {
                            number.Digits[digCount] = (byte)ch;
                            if ((ch != '0') || (number.Kind != ParseBuffer.KindInteger))
                            {
                                digEnd = digCount + 1;
                            }
                        }
                        else if (ch != '0')
                        {
                            // For decimal and binary floating-point numbers, we only
                            // need to store digits up to maxDigCount. However, we still
                            // need to keep track of whether any additional digits past
                            // maxDigCount were non-zero, as that can impact rounding
                            // for an input that falls evenly between two representable
                            // results.
                            number.HasNonZeroTail = true;
                        }

                        if ((state & StateDecimal) == 0)
                        {
                            number.Scale++;
                        }

                        if (digCount < maxDigCount)
                        {
                            // Handle a case like "53.0". We need to ignore trailing zeros in the fractional part for floating point numbers, so we keep a count of the number of trailing zeros and update digCount later
                            if (ch == '0')
                            {
                                numberOfTrailingZeros++;
                            }
                            else
                            {
                                numberOfTrailingZeros = 0;
                            }
                        }
                        digCount++;
                        state |= StateNonZero;
                    }
                    else if ((state & StateDecimal) != 0)
                    {
                        number.Scale--;
                    }
                }
                else if (((styles & NumberStyles.AllowDecimalPoint) != 0) && ((state & StateDecimal) == 0) && ((next = MatchChars(s, p, end, decSep)) >= 0 || (parsingCurrency && (state & StateCurrency) == 0 && (next = MatchChars(s, p, end, info.NumberDecimalSeparator)) >= 0)))
                {
                    state |= StateDecimal;
                    p = next - 1;
                }
                else if (((styles & NumberStyles.AllowThousands) != 0) && ((state & StateDigits) != 0) && ((state & StateDecimal) == 0) && ((next = MatchChars(s, p, end, groupSep)) >= 0 || (parsingCurrency && (state & StateCurrency) == 0 && (next = MatchChars(s, p, end, info.NumberGroupSeparator)) >= 0)))
                {
                    p = next - 1;
                }
                else
                {
                    break;
                }
                ch = Ch(s, ++p, end);
            }

            bool negExp = false;
            number.DigitsCount = digEnd;
            number.Digits[digEnd] = 0;
            if ((state & StateDigits) != 0)
            {
                if ((ch == 'E' || ch == 'e') && ((styles & NumberStyles.AllowExponent) != 0))
                {
                    int temp = p;
                    ch = Ch(s, ++p, end);
                    if ((next = MatchSignChars(s, p, end, info, out negExp)) >= 0)
                    {
                        ch = Ch(s, p = next, end);
                    }
                    if (IsDigitCh(ch))
                    {
                        int exp = 0;
                        do
                        {
                            // Check if we are about to overflow past our limit of 9 digits
                            if (exp >= 100_000_000)
                            {
                                // Set exp to Int.MaxValue to signify the requested exponent is too large. This will lead to an OverflowException later.
                                exp = int.MaxValue;
                                number.Scale = 0;

                                // Finish parsing the number, a FormatException could still occur later on.
                                while (IsDigitCh(ch))
                                {
                                    ch = Ch(s, ++p, end);
                                }
                                break;
                            }

                            exp = (exp * 10) + (int)(ch - '0');
                            ch = Ch(s, ++p, end);
                        } while (IsDigitCh(ch));
                        if (negExp)
                        {
                            exp = -exp;
                        }
                        number.Scale += exp;
                    }
                    else
                    {
                        p = temp;
                        ch = Ch(s, p, end);
                    }
                }

                if (number.Kind == ParseBuffer.KindFloatingPoint && !number.HasNonZeroTail)
                {
                    // Adjust the number buffer for trailing zeros
                    int numberOfFractionalDigits = digEnd - number.Scale;
                    if (numberOfFractionalDigits > 0)
                    {
                        numberOfTrailingZeros = Math.Min(numberOfTrailingZeros, numberOfFractionalDigits);
                        number.DigitsCount = digEnd - numberOfTrailingZeros;
                        number.Digits[number.DigitsCount] = 0;
                    }
                }

                while (true)
                {
                    if (!IsWhiteCh(ch) || (styles & NumberStyles.AllowTrailingWhite) == 0)
                    {
                        if ((styles & NumberStyles.AllowTrailingSign) != 0 && ((state & StateSign) == 0) && ((next = MatchSignChars(s, p, end, info, out bool isNegative)) >= 0))
                        {
                            state |= StateSign;
                            number.IsNegative = isNegative;
                            p = next - 1;
                        }
                        else if (ch == ')' && ((state & StateParens) != 0))
                        {
                            state &= ~StateParens;
                        }
                        else if (!string.IsNullOrEmpty(currSymbol) && (next = MatchChars(s, p, end, currSymbol)) >= 0)
                        {
                            currSymbol = null;
                            p = next - 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                    ch = Ch(s, ++p, end);
                }
                if ((state & StateParens) == 0)
                {
                    if ((state & StateNonZero) == 0)
                    {
                        if (number.Kind != ParseBuffer.KindDecimal)
                        {
                            number.Scale = 0;
                        }
                        if ((number.Kind == ParseBuffer.KindInteger) && (state & StateDecimal) == 0)
                        {
                            number.IsNegative = false;
                        }
                    }

                    // For compatibility we still need to process any trailing
                    // nulls that exist and report them as having been consumed.
                    int index = p;
                    while (index < end && s[index] == '\0') index++;
                    if (index == end)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ==========================================================================================
        // Integers
        // ==========================================================================================

        private static int MaxDigitCount(int bits, bool isSigned) => bits switch
        {
            8 => 3,
            16 => 5,
            32 => 10,
            _ => isSigned ? 19 : 20,
        };

        /// <summary>.NET's TryParseBinaryInteger (NumberStyles validated by the caller): ParseOK, ParseFailed or ParseOverflow.</summary>
        internal static int TryParseInteger(string s, NumberStyles styles, NumberFormatInfo info, int bits, bool isSigned, out long result)
        {
            result = 0;
            if (s == null) return ParseFailed;
            if ((styles & ~NumberStyles.Integer) == 0) return TryParseIntegerStyle(s, styles, info, bits, isSigned, out result);
            if ((styles & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier)) != 0)
                return TryParseHexOrBinaryInteger(s, styles, bits, isSigned, out result);

            Span<byte> digits = stackalloc byte[MaxDigitCount(bits, isSigned) + 1];
            var number = new ParseBuffer(ParseBuffer.KindInteger, digits);
            if (!TryParseNumber(s, styles, ref number, info)) return ParseFailed;
            return TryNumberBufferToInteger(ref number, bits, isSigned, out result) ? ParseOK : ParseOverflow;
        }

        private static bool TryNumberBufferToInteger(ref ParseBuffer number, int bits, bool isSigned, out long value)
        {
            value = 0;
            int i = number.Scale;
            if (i > MaxDigitCount(bits, isSigned) || i < number.DigitsCount || (!isSigned && number.IsNegative) || number.HasNonZeroTail)
                return false;
            ulong limit = isSigned ? (1UL << (bits - 1)) - (number.IsNegative ? 0UL : 1UL) : (bits == 64 ? ulong.MaxValue : (1UL << bits) - 1);
            ulong n = 0;
            int p = 0;
            while (--i >= 0)
            {
                if (n > ulong.MaxValue / 10) return false;
                ulong m = n * 10;
                if (p < number.DigitsCount)
                {
                    ulong d = (ulong)(number.Digits[p++] - '0');
                    if (m > ulong.MaxValue - d) return false;
                    m += d;
                }
                n = m;
                if (n > limit) return false;
            }
            value = number.IsNegative ? (long)(0UL - n) : (long)n;
            return true;
        }

        /// <summary>.NET's TryParseBinaryIntegerStyle: styles within NumberStyles.Integer.</summary>
        private static int TryParseIntegerStyle(string value, NumberStyles styles, NumberFormatInfo info, int bits, bool isSigned, out long result)
        {
            result = 0;
            int end = value.Length;
            if (end == 0) return ParseFailed;
            int index = 0;
            uint num = value[0];
            if ((styles & NumberStyles.AllowLeadingWhite) != 0 && IsWhiteCh(num))
            {
                do
                {
                    index++;
                    if (index >= end) return ParseFailed;
                    num = value[index];
                }
                while (IsWhiteCh(num));
            }

            bool isNegative = false;
            if ((styles & NumberStyles.AllowLeadingSign) != 0)
            {
                int nextIndex = MatchSignChars(value, index, end, info, out isNegative);
                if (nextIndex >= 0)
                {
                    index = nextIndex;
                    if (index >= end) return ParseFailed;
                    num = value[index];
                }
            }

            if (!IsDigitCh(num)) return ParseFailed;
            ulong limit = isSigned ? (1UL << (bits - 1)) - (isNegative ? 0UL : 1UL) : (bits == 64 ? ulong.MaxValue : (1UL << bits) - 1);
            bool overflow = !isSigned && isNegative;
            ulong answer = 0;
            bool anyNonZero = false;
            while (index < end && IsDigitCh(num = value[index]))
            {
                uint d = num - '0';
                if (d != 0) anyNonZero = true;
                if (answer > (ulong.MaxValue - d) / 10) overflow = true;
                else answer = answer * 10 + d;
                if (answer > limit) overflow = true;
                index++;
            }
            // "-0" (all zeros) is not an overflow for unsigned types.
            if (!isSigned && !anyNonZero) overflow = false;

            if (index < end)
            {
                num = value[index];
                if (IsWhiteCh(num) && (styles & NumberStyles.AllowTrailingWhite) != 0)
                {
                    for (index++; index < end && IsWhiteCh(value[index]); index++) { }
                }
                // trailing nulls
                while (index < end && value[index] == '\0') index++;
                if (index < end) return ParseFailed;
            }
            if (overflow) return ParseOverflow;
            result = isNegative ? (long)(0UL - answer) : (long)answer;
            return ParseOK;
        }

        /// <summary>.NET's TryParseBinaryIntegerHexOrBinaryNumberStyle: two's complement of `bits` bits.</summary>
        private static int TryParseHexOrBinaryInteger(string value, NumberStyles styles, int bits, bool isSigned, out long result)
        {
            result = 0;
            bool hex = (styles & NumberStyles.AllowHexSpecifier) != 0;
            int shift = hex ? 4 : 1;
            int end = value.Length;
            int index = 0;
            if ((styles & NumberStyles.AllowLeadingWhite) != 0) while (index < end && IsWhiteCh(value[index])) index++;
            if (index == end) return ParseFailed;
            ulong answer = 0;
            bool overflow = false;
            bool any = false;
            int significant = 0;
            for (; index < end; index++)
            {
                char c = value[index];
                int d = hex
                    ? (c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1)
                    : (c == '0' || c == '1' ? c - '0' : -1);
                if (d < 0) break;
                any = true;
                if (significant > 0 || d != 0)
                {
                    significant++;
                    if (significant * shift > bits) overflow = true;
                }
                answer = (answer << shift) | (uint)d;
            }
            if (!any) return ParseFailed;
            if (index < end)
            {
                if ((styles & NumberStyles.AllowTrailingWhite) != 0) while (index < end && IsWhiteCh(value[index])) index++;
                while (index < end && value[index] == '\0') index++;
                if (index < end) return ParseFailed;
            }
            if (overflow) return ParseOverflow;
            if (bits < 64) answer &= (1UL << bits) - 1;
            // int.Parse("FFFFFFFF", HexNumber) == -1: reinterpret within the type's width
            if (isSigned && bits < 64 && ((answer >> (bits - 1)) & 1) != 0) result = (long)(answer | (ulong.MaxValue << bits));
            else result = (long)answer;
            return ParseOK;
        }

        private static string IntegerTypeOf(int bits, bool isSigned) => bits switch
        {
            8 => isSigned ? "SByte" : "Byte",
            16 => isSigned ? "Int16" : "UInt16",
            32 => isSigned ? "Int32" : "UInt32",
            _ => isSigned ? "Int64" : "UInt64",
        };

        /// <summary>int.Parse(s, styles, provider) & co.: throws like .NET.</summary>
        internal static long ParseIntegerWith(string s, int styles, IFormatProvider provider, int bits, bool isSigned)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleInteger(style);
            if (s == null) throw new ArgumentNullException("s");
            int status = TryParseInteger(s, style, NumberFormatInfo.GetInstance(provider), bits, isSigned, out long result);
            if (status == ParseOverflow)
                throw new OverflowException("Value was either too large or too small for " + IntegerTypeName(bits, isSigned) + ".");
            if (status != ParseOK) ThrowParseFormat(s);
            return result;
        }

        internal static bool TryParseIntegerWith(string s, int styles, IFormatProvider provider, int bits, bool isSigned, out long result)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleInteger(style);
            if (s == null) { result = 0; return false; }
            bool ok = TryParseInteger(s, style, NumberFormatInfo.GetInstance(provider), bits, isSigned, out result) == ParseOK;
            if (!ok) result = 0;
            return ok;
        }

        // ==========================================================================================
        // Floating point
        // ==========================================================================================

        private const int DoubleNumberBufferLength = 767 + 1 + 1;

        /// <summary>.NET's TryParseFloat (no hexadecimal floating point).</summary>
        internal static bool TryParseDoubleCore(string value, NumberStyles styles, NumberFormatInfo info, out double result, bool single = false)
        {
            result = 0;
            if (value == null) return false;
            if ((styles & NumberStyles.AllowHexSpecifier) == 0)
            {
                Span<byte> digits = stackalloc byte[DoubleNumberBufferLength];
                var number = new ParseBuffer(ParseBuffer.KindFloatingPoint, digits);
                if (TryParseNumber(value, styles, ref number, info))
                {
                    while (number.DigitsCount > 0 && number.Digits[number.DigitsCount - 1] == (byte)'0') number.DigitsCount--;
                    result = single
                        ? DigitsToSingle(number.Digits, number.DigitsCount, number.Scale, number.HasNonZeroTail, number.IsNegative)
                        : DigitsToDouble(number.Digits, number.DigitsCount, number.Scale, number.HasNonZeroTail, number.IsNegative);
                    return true;
                }
            }

            // Leading and trailing whitespace around a special value (Infinity/NaN) is always consumed.
            string valueTrim = value.TrimStart();
            if (TryMatchSpecialValueSymbol(valueTrim, info.PositiveInfinitySymbol)) { result = double.PositiveInfinity; return true; }
            if (TryMatchSpecialValueSymbol(valueTrim, info.NegativeInfinitySymbol)) { result = double.NegativeInfinity; return true; }
            if (TryMatchSpecialValueSymbol(valueTrim, info.NaNSymbol)) { result = double.NaN; return true; }

            string positiveSign = info.PositiveSign;
            if (positiveSign.Length > 0 && valueTrim.StartsWith(positiveSign, StringComparison.OrdinalIgnoreCase))
            {
                string afterSign = valueTrim.Substring(positiveSign.Length);
                if (TryMatchSpecialValueSymbol(afterSign, info.PositiveInfinitySymbol)) { result = double.PositiveInfinity; return true; }
                if (TryMatchSpecialValueSymbol(afterSign, info.NaNSymbol)) { result = double.NaN; return true; }
                result = 0;
                return false;
            }

            string negativeSign = info.NegativeSign;
            if (negativeSign.Length > 0 && valueTrim.StartsWith(negativeSign, StringComparison.OrdinalIgnoreCase))
            {
                if (TryMatchSpecialValueSymbol(valueTrim.Substring(negativeSign.Length), info.NaNSymbol)) { result = double.NaN; return true; }
                if (info.AllowHyphenDuringParsing() && valueTrim.Length > 0 && valueTrim[0] == '-'
                    && TryMatchSpecialValueSymbol(valueTrim.Substring(1), info.NaNSymbol)) { result = double.NaN; return true; }
            }

            result = 0;
            return false;
        }

        /// <summary>The symbol, case-insensitively, then only whitespace.</summary>
        private static bool TryMatchSpecialValueSymbol(string candidate, string symbol)
        {
            if (string.IsNullOrEmpty(symbol) || symbol.Length > candidate.Length) return false;
            if (string.Compare(candidate.Substring(0, symbol.Length), symbol, StringComparison.OrdinalIgnoreCase) != 0) return false;
            for (int i = symbol.Length; i < candidate.Length; i++)
                if (!char.IsWhiteSpace(candidate[i])) return false;
            return true;
        }

        internal static double ParseDoubleWith(string s, int styles, IFormatProvider provider)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
            if (s == null) throw new ArgumentNullException("s");
            if (!TryParseDoubleCore(s, style, NumberFormatInfo.GetInstance(provider), out double result)) ThrowParseFormat(s);
            return result;
        }

        internal static bool TryParseDoubleWith(string s, int styles, IFormatProvider provider, out double result)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
            return TryParseDoubleCore(s, style, NumberFormatInfo.GetInstance(provider), out result);
        }

        // float: the digits round straight to float (DigitsToSingle), not through a rounded double.
        internal static float ParseSingleWith(string s, int styles, IFormatProvider provider)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
            if (s == null) throw new ArgumentNullException("s");
            if (!TryParseDoubleCore(s, style, NumberFormatInfo.GetInstance(provider), out double result, single: true)) ThrowParseFormat(s);
            return (float)result;
        }

        internal static bool TryParseSingleWith(string s, int styles, IFormatProvider provider, out float result)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
            bool ok = TryParseDoubleCore(s, style, NumberFormatInfo.GetInstance(provider), out double d, single: true);
            result = (float)d;
            return ok;
        }

        // ==========================================================================================
        // Decimal
        // ==========================================================================================

        private const int DecimalPrecision = 29;

        internal static int TryParseDecimalCore(string value, NumberStyles styles, NumberFormatInfo info, out decimal result)
        {
            result = 0;
            if (value == null) return ParseFailed;
            Span<byte> digits = stackalloc byte[DecimalPrecision + 1 + 1];
            var number = new ParseBuffer(ParseBuffer.KindDecimal, digits);
            if (!TryParseNumber(value, styles, ref number, info)) return ParseFailed;
            return TryNumberToDecimal(ref number, ref result) ? ParseOK : ParseOverflow;
        }

        /// <summary>.NET's TryNumberToDecimal.</summary>
        private static bool TryNumberToDecimal(ref ParseBuffer number, ref decimal value)
        {
            int p = 0;
            int e = number.Scale;
            bool sign = number.IsNegative;
            uint c = number.Digit(p);
            if (c == 0)
            {
                // To avoid risking an app-compat issue with pre 4.5 (where some app was illegally using Reflection to examine the internal scale bits), we'll only force
                // the scale to 0 if the scale was previously positive (previously, such cases were unparsable to a bug.)
                value = new decimal(0, 0, 0, sign, (byte)Math.Clamp(-e, 0, 28));
                return true;
            }

            if (e > DecimalPrecision)
                return false;

            ulong low64 = 0;
            while (e > -28)
            {
                e--;
                low64 *= 10;
                low64 += c - '0';
                c = number.Digit(++p);
                if (low64 >= ulong.MaxValue / 10)
                    break;
                if (c == 0)
                {
                    while (e > 0)
                    {
                        e--;
                        low64 *= 10;
                        if (low64 >= ulong.MaxValue / 10)
                            break;
                    }
                    break;
                }
            }

            uint high = 0;
            while ((e > 0 || (c != 0 && e > -28)) &&
              (high < uint.MaxValue / 10 || (high == uint.MaxValue / 10 && (low64 < 0x99999999_99999999 || (low64 == 0x99999999_99999999 && c <= '5')))))
            {
                // multiply by 10
                ulong tmpLow = (uint)low64 * 10UL;
                ulong tmp64 = ((uint)(low64 >> 32) * 10UL) + (tmpLow >> 32);
                low64 = (uint)tmpLow + (tmp64 << 32);
                high = (uint)(tmp64 >> 32) + (high * 10);

                if (c != 0)
                {
                    c -= '0';
                    low64 += c;
                    if (low64 < c)
                        high++;
                    c = number.Digit(++p);
                }
                e--;
            }

            if (c >= '5')
            {
                bool round = true;
                if ((c == '5') && ((low64 & 1) == 0))
                {
                    c = number.Digit(++p);

                    bool hasZeroTail = !number.HasNonZeroTail;

                    // We might still have some additional digits, in which case they need
                    // to be considered as part of hasZeroTail.
                    while ((c != 0) && hasZeroTail)
                    {
                        hasZeroTail &= c == '0';
                        c = number.Digit(++p);
                    }

                    // When the next digit is 5, the number is even, and all following
                    // digits are zero we don't need to round.
                    if (hasZeroTail) round = false;
                }

                if (round && ++low64 == 0 && ++high == 0)
                {
                    low64 = 0x99999999_9999999A;
                    high = uint.MaxValue / 10;
                    e++;
                }
            }

            if (e > 0)
                return false;

            if (e <= -DecimalPrecision)
            {
                // Parsing a large scale zero can give you more precision than fits in the decimal.
                // This should only happen for actual zeros or very small numbers that round to zero.
                value = new decimal(0, 0, 0, sign, DecimalPrecision - 1);
            }
            else
            {
                value = new decimal((int)low64, (int)(low64 >> 32), (int)high, sign, (byte)-e);
            }
            return true;
        }

        internal static decimal ParseDecimalWith(string s, int styles, IFormatProvider provider)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            if (s == null) throw new ArgumentNullException("s");
            int status = TryParseDecimalCore(s, style, NumberFormatInfo.GetInstance(provider), out decimal result);
            if (status == ParseOverflow) throw new OverflowException("Value was either too large or too small for a Decimal.");
            if (status != ParseOK) ThrowParseFormat(s);
            return result;
        }

        internal static bool TryParseDecimalWith(string s, int styles, IFormatProvider provider, out decimal result)
        {
            NumberStyles style = (NumberStyles)styles;
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            bool ok = TryParseDecimalCore(s, style, NumberFormatInfo.GetInstance(provider), out result) == ParseOK;
            if (!ok) result = 0;
            return ok;
        }
    }
}
