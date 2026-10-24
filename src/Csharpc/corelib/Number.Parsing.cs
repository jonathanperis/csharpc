// Number parsing in C# (formerly src/runtime/parse.c and the int/double parsers in strings.c):
// int/long/double Parse/TryParse with NumberStyles for the invariant culture, a correctly rounded
// decimal -> binary conversion (exact big-integer division, no strtod), Math.Round(double, digits,
// mode) on the exact decimal value, and Int128 formatting.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static partial class Number
    {
        private const int AllowLeadingWhite = 1, AllowTrailingWhite = 2, AllowLeadingSign = 4, AllowDecimalPoint = 32,
            AllowThousands = 64, AllowExponent = 128, AllowHexSpecifier = 512;
        private const int StatusOk = 1, StatusFormat = 0, StatusOverflow = 2;
        private const int MaxParseDigits = 800;  // more than the 767 significant digits any double needs

        private static bool IsWhite(char c) => c == ' ' || (c >= '\t' && c <= '\r');

        // ==========================================================================================
        // Integers
        // ==========================================================================================

        /// <summary>Returns 1 on success, 0 on format error, 2 on overflow. Hex parses as two's complement of `bits`.</summary>
        [RuntimeExport("csrt_parse_i64_styled", "csrt_enum_parse")]
        [RuntimeExport("csrt_parse_i64_styled", "csrt_enum_parse_or_throw")]
        internal static int ParseInt64Styled(string s, int styles, int bits, bool isSigned, ref long result)
        {
            result = 0;
            if (s == null) return StatusFormat;
            int i = 0, end = s.Length;
            if ((styles & AllowLeadingWhite) != 0) while (i < end && IsWhite(s[i])) i++;
            if ((styles & AllowTrailingWhite) != 0) while (end > i && IsWhite(s[end - 1])) end--;
            if (i == end) return StatusFormat;
            if ((styles & AllowHexSpecifier) != 0)
            {
                ulong v = 0;
                bool overflow = false;
                for (; i < end; i++)
                {
                    char c = s[i];
                    int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (d < 0) return StatusFormat;
                    if ((v >> (bits - 4)) != 0) overflow = true;
                    v = (v << 4) | (uint)d;
                }
                if (overflow) return StatusOverflow;
                // int.Parse("FFFFFFFF", HexNumber) == -1: reinterpret within the type's width
                if (isSigned && bits < 64 && ((v >> (bits - 1)) & 1) != 0) result = (long)(v | (ulong.MaxValue << bits));
                else result = (long)v;
                return StatusOk;
            }
            bool negative = false;
            if ((styles & AllowLeadingSign) != 0 && (s[i] == '-' || s[i] == '+')) { negative = s[i] == '-'; i++; }
            if (i == end) return StatusFormat;
            ulong value = 0;
            bool tooBig = false, anyDigit = false;
            for (; i < end; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    anyDigit = true;
                    if (value > (ulong.MaxValue - 9) / 10) tooBig = true;
                    else value = value * 10 + (ulong)(c - '0');
                    continue;
                }
                if (c == ',' && (styles & AllowThousands) != 0 && anyDigit) continue;
                // "123.000" is accepted with AllowDecimalPoint when the fraction is all zeros
                if (c == '.' && (styles & AllowDecimalPoint) != 0)
                {
                    bool nonZeroFraction = false;
                    for (i++; i < end; i++)
                    {
                        if (s[i] < '0' || s[i] > '9') return StatusFormat;
                        if (s[i] != '0') nonZeroFraction = true;
                    }
                    if (nonZeroFraction) tooBig = true;
                    break;
                }
                return StatusFormat;
            }
            if (!anyDigit) return StatusFormat;
            if (tooBig) return StatusOverflow;
            if (isSigned)
            {
                ulong limit = bits == 64 ? (ulong)long.MaxValue : (1UL << (bits - 1)) - 1;
                if (value > limit + (negative ? 1UL : 0UL)) return StatusOverflow;
                result = negative ? (long)(0 - value) : (long)value;
            }
            else
            {
                if (negative && value != 0) return StatusOverflow;
                ulong limit = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
                if (value > limit) return StatusOverflow;
                result = (long)value;
            }
            return StatusOk;
        }

        private static string IntegerTypeName(int bits, bool isSigned) => bits switch
        {
            8 => isSigned ? "a signed byte" : "an unsigned byte",
            16 => isSigned ? "an Int16" : "a UInt16",
            32 => isSigned ? "an Int32" : "a UInt32",
            _ => isSigned ? "an Int64" : "a UInt64",
        };

        private static void ThrowParseFormat(string s) =>
            throw new FormatException("The input string '" + s + "' was not in a correct format.");

        internal static long ParseInt64StyledOrThrow(string s, int styles, int bits, bool isSigned)
        {
            if (s == null) throw new ArgumentNullException("s");
            long v = 0;
            int status = ParseInt64Styled(s, styles, bits, isSigned, ref v);
            if (status == StatusOk) return v;
            if (status == StatusOverflow)
                throw new OverflowException("Value was either too large or too small for " + IntegerTypeName(bits, isSigned) + ".");
            ThrowParseFormat(s);
            return 0;
        }

        // int.Parse(string) / long.Parse(string) / TryParse (Convert.ToInt32(string) & co.): NumberStyles.Integer,
        // current culture (Number.Parsing.Culture.cs).
        internal static int ParseInt32(string s) => (int)ParseIntegerWith(s, 7, null, 32, true);
        internal static long ParseInt64(string s) => ParseIntegerWith(s, 7, null, 64, true);

        internal static bool TryParseInt32(string s, ref int result)
        {
            bool ok = TryParseIntegerWith(s, 7, null, 32, true, out long v);
            result = (int)v;
            return ok;
        }

        internal static bool TryParseInt64(string s, ref long result) => TryParseIntegerWith(s, 7, null, 64, true, out result);

        // ==========================================================================================
        // Floating point
        // ==========================================================================================

        private static bool EqualsIgnoreCase(string s, int start, int end, string word)
        {
            if (end - start != word.Length) return false;
            for (int i = 0; i < word.Length; i++)
            {
                char c = s[start + i];
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                char w = word[i];
                if (w >= 'A' && w <= 'Z') w = (char)(w + 32);
                if (c != w) return false;
            }
            return true;
        }

        /// <summary>Returns 1 on success, 0 on format error (out-of-range values become ±Infinity / ±0, as in .NET Core 3.0+).</summary>
        internal static int ParseDoubleStyled(string s, int styles, ref double result)
        {
            result = 0;
            if (s == null) return StatusFormat;
            int i = 0, end = s.Length;
            while (i < end && IsWhite(s[i])) i++;
            while (end > i && IsWhite(s[end - 1])) end--;
            if (i == end) return StatusFormat;
            int start = i;
            bool negative = false;
            if ((styles & AllowLeadingSign) != 0 && (s[i] == '-' || s[i] == '+')) { negative = s[i] == '-'; i++; }

            Span<byte> digits = stackalloc byte[MaxParseDigits];
            int n = 0, exp10 = 0;
            bool any = false, truncatedNonZero = false;
            // integer part
            for (; i < end; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    any = true;
                    if (n == 0 && c == '0') continue;  // leading zeros
                    if (n < MaxParseDigits) digits[n++] = (byte)c;
                    else if (c != '0') truncatedNonZero = true;
                    exp10++;
                    continue;
                }
                if (c == ',' && (styles & AllowThousands) != 0 && any) continue;
                break;
            }
            if (i < end && s[i] == '.' && (styles & AllowDecimalPoint) != 0)
            {
                for (i++; i < end; i++)
                {
                    char c = s[i];
                    if (c < '0' || c > '9') break;
                    any = true;
                    if (n == 0 && c == '0') { exp10--; continue; }
                    if (n < MaxParseDigits) digits[n++] = (byte)c;
                    else if (c != '0') truncatedNonZero = true;
                }
            }
            if (any && i < end && (s[i] == 'e' || s[i] == 'E') && (styles & AllowExponent) != 0)
            {
                int j = i + 1;
                bool expNegative = false;
                if (j < end && (s[j] == '-' || s[j] == '+')) { expNegative = s[j] == '-'; j++; }
                if (j < end && s[j] >= '0' && s[j] <= '9')
                {
                    int e = 0;
                    for (; j < end && s[j] >= '0' && s[j] <= '9'; j++) if (e < 100000) e = e * 10 + (s[j] - '0');
                    exp10 += expNegative ? -e : e;
                    i = j;
                }
            }
            if (!any || i != end)
            {
                // NaN / Infinity (case-insensitive, optionally signed)
                int k = start;
                bool neg = false;
                if (k < end && (s[k] == '-' || s[k] == '+')) { neg = s[k] == '-'; k++; }
                if (EqualsIgnoreCase(s, k, end, "NaN")) { result = double.NaN; return StatusOk; }
                if (EqualsIgnoreCase(s, k, end, "Infinity"))
                {
                    result = neg ? double.NegativeInfinity : double.PositiveInfinity;
                    return StatusOk;
                }
                return StatusFormat;
            }
            while (n > 0 && digits[n - 1] == (byte)'0') n--;
            result = DigitsToDouble(digits, n, exp10, truncatedNonZero, negative);
            return StatusOk;
        }

        internal static double ParseDoubleStyledOrThrow(string s, int styles) => ParseDoubleWith(s, styles, null);

        // double.Parse(string) / TryParse (Convert.ToDouble(string)): NumberStyles.Float | AllowThousands, current culture.
        internal static double ParseDouble(string s) => ParseDoubleWith(s, 231, null);

        internal static bool TryParseDouble(string s, ref double result) => TryParseDoubleWith(s, 231, null, out result);

        internal static double DigitsToDoubleOf(Span<byte> digits, int n, int exp10, bool negative) =>
            DigitsToDouble(digits, n, exp10, false, negative);

        /// <summary>
        /// Correctly rounded (nearest, ties to even) double for ±0.d1d2...dn * 10^exp10. Exact: the
        /// value is a big-integer ratio from which 64 quotient bits plus a sticky bit are extracted.
        /// </summary>
        /// <summary>
        /// The correctly rounded float for the same digits: the double is computed rounding to odd, which
        /// makes the narrowing to float round correctly (53 bits >= 2 * 24 + 2), with no double rounding.
        /// </summary>
        internal static float DigitsToSingle(Span<byte> digits, int n, int exp10, bool sticky, bool negative) =>
            (float)DigitsToDouble(digits, n, exp10, sticky, negative, roundToOdd: true);

        private static double DigitsToDouble(Span<byte> digits, int n, int exp10, bool sticky, bool negative, bool roundToOdd = false)
        {
            long signBit = negative ? long.MinValue : 0;
            if (n == 0) return CoreIntrinsics.BitsToDouble(signBit);
            if (exp10 > 310) return negative ? double.NegativeInfinity : double.PositiveInfinity;
            if (exp10 < -330) return CoreIntrinsics.BitsToDouble(signBit);
            int e = exp10 - n;  // value = D * 10^e, D = the n digits as an integer

            // Fast path (Clinger): D and 10^|e| are exact doubles, so one IEEE operation rounds correctly.
            if (n <= 15 && !sticky && !roundToOdd && e >= -22 && e <= 22)
            {
                long d = 0;
                for (int i = 0; i < n; i++) d = d * 10 + (digits[i] - '0');
                double p = 1;
                for (int i = 0; i < (e < 0 ? -e : e); i++) p *= 10;
                double r = e < 0 ? d / p : d * p;
                return negative ? -r : r;
            }

            Span<uint> ns = stackalloc uint[132];
            Span<uint> ds = stackalloc uint[132];
            var num = new BigNum(ns);
            var den = new BigNum(ds);
            num.SetUInt64(0);
            for (int i = 0; i < n; i++)
            {
                if (num.Len == 0) num.SetUInt64((ulong)(digits[i] - '0'));
                else num.MulAdd10((uint)(digits[i] - '0'));
            }
            if (sticky) { num.MulAdd10(1); e--; }  // a nonzero tail beyond the kept digits
            den.SetUInt64(1);
            if (e >= 0) num.MulPow10(e);
            else den.MulPow10(-e);

            // q = floor(num * 2^shift / den) in [2^62, 2^64).
            int shift = 63 - (num.BitLength() - den.BitLength());
            if (shift > 0) num.ShiftLeft(shift);
            else if (shift < 0) den.ShiftLeft(-shift);
            den.ShiftLeft(63);
            ulong q = 0;
            for (int bit = 63; bit >= 0; bit--)
            {
                if (BigNum.Compare(in num, in den) >= 0) { num.Subtract(in den); q |= 1UL << bit; }
                den.ShiftRightOne();
            }
            bool restNonZero = !num.IsZero;
            return AssembleDouble(q, shift, restNonZero, negative, roundToOdd);
        }

        /// <summary>Rounds q * 2^-shift (plus a sticky bit) to the nearest double, ties to even.</summary>
        private static double AssembleDouble(ulong q, int shift, bool sticky, bool negative, bool roundToOdd = false)
        {
            long signBit = negative ? long.MinValue : 0;
            int bitLength = 0;
            for (ulong t = q; t != 0; t >>= 1) bitLength++;
            int leadExp = bitLength - 1 - shift;
            if (leadExp > 1023) return negative ? double.NegativeInfinity : double.PositiveInfinity;
            int lsbExp = leadExp - 52;
            if (lsbExp < -1074) lsbExp = -1074;
            int drop = lsbExp + shift;  // >= 10, since q has at least 63 bits
            ulong m;
            bool roundBit, rest;
            if (drop < 64)
            {
                m = q >> drop;
                roundBit = ((q >> (drop - 1)) & 1) != 0;
                rest = (q & ((1UL << (drop - 1)) - 1)) != 0 || sticky;
            }
            else if (drop == 64)
            {
                m = 0;
                roundBit = (q >> 63) != 0;
                rest = (q & (ulong.MaxValue >> 1)) != 0 || sticky;
            }
            else return CoreIntrinsics.BitsToDouble(signBit);
            if (roundToOdd) { if (roundBit || rest) m |= 1; }
            else if (roundBit && (rest || (m & 1) != 0)) m++;
            if (m == 1UL << 53) { m >>= 1; lsbExp++; }
            long bits;
            if (m >= 1UL << 52)
            {
                int biased = lsbExp + 52 + 1023;
                if (biased >= 2047) return negative ? double.NegativeInfinity : double.PositiveInfinity;
                bits = ((long)biased << 52) | (long)(m & ((1UL << 52) - 1));
            }
            else bits = (long)m;  // subnormal (or zero)
            return CoreIntrinsics.BitsToDouble(bits | signBit);
        }

        // ==========================================================================================
        // Math.Round(double, digits[, mode]): on the exact decimal value (2.675 is 2.67499999... -> 2.67)
        // ==========================================================================================

        internal static double RoundDigits(double value, int digits) => RoundMode(value, digits, 0);

        /// <summary>mode: 0 ToEven, 1 AwayFromZero, 2 ToZero, 3 ToNegativeInfinity, 4 ToPositiveInfinity.</summary>
        internal static double RoundMode(double value, int digits, int mode)
        {
            if (digits < 0) throw new ArgumentOutOfRangeException("digits", "Rounding digits must be greater than or equal to 0.");
            if ((uint)mode > 4)
                throw new ArgumentException("The value '" + Int32ToDecStr(mode) + "' is not valid for this usage of the type MidpointRounding.", "mode");
            if (digits > 400) digits = 400;  // beyond the last significant digit of any double
            if (value != value || value >= 1e16 || value <= -1e16) return value;
            if (value == 0) return value;
            Span<byte> storage = stackalloc byte[DigitStorage];
            var g = new DigitBuffer(storage);
            g.IsFloat = true;
            g.Neg = value < 0;
            Decompose(value, out ulong f, out int e, out bool _);
            ExactDigits(f, e, ref g);
            int pos = g.Exp10 + digits;
            if (pos < g.N)
            {
                bool up;
                if (mode == 0 || mode == 1)
                {
                    if (pos < 0) up = false;
                    else
                    {
                        byte d = g.D[pos];
                        up = d > (byte)'5' || (d == (byte)'5' && (mode == 1 || pos + 1 < g.N || (pos > 0 && ((g.D[pos - 1] - '0') & 1) == 1)));
                    }
                }
                else if (mode == 2) up = false;
                else up = mode == 3 ? g.Neg : !g.Neg;  // a nonzero part is dropped
                if (pos < 0)
                {
                    // Every digit is dropped: the result is 0 or one unit of 10^-digits.
                    if (up) { g.D[0] = (byte)'1'; g.N = 1; g.Exp10 = 1 - digits; }
                    else g.N = 0;
                }
                else
                {
                    int i = pos;
                    if (up)
                    {
                        while (i > 0 && g.D[i - 1] == (byte)'9') i--;
                        if (i > 0) g.D[i - 1]++;
                        else { g.Exp10++; g.D[0] = (byte)'1'; i = 1; }
                    }
                    g.N = i;
                }
            }
            while (g.N > 0 && g.D[g.N - 1] == (byte)'0') g.N--;
            return DigitsToDouble(g.D, g.N, g.Exp10, false, g.Neg);
        }

        // ==========================================================================================
        // Int128 / UInt128 -> string
        // ==========================================================================================

        internal static string FormatInt128(ulong lo, ulong hi, bool isSigned)
        {
            bool negative = isSigned && (long)hi < 0;
            if (negative)
            {
                lo = ~lo + 1;
                hi = ~hi + (lo == 0 ? 1UL : 0UL);
            }
            if (hi == 0) return negative ? "-" + UInt64ToDecStr(lo) : UInt64ToDecStr(lo);
            Span<uint> words = stackalloc uint[5];
            var n = new BigNum(words);
            n.W[0] = (uint)lo; n.W[1] = (uint)(lo >> 32); n.W[2] = (uint)hi; n.W[3] = (uint)(hi >> 32); n.Len = 4;
            n.Normalize();
            Span<char> buf = stackalloc char[48];
            int at = buf.Length;
            while (!n.IsZero)
            {
                uint chunk = n.DivRemSmall(1000000000);
                for (int i = 0; i < 9 && (chunk != 0 || !n.IsZero); i++) { buf[--at] = (char)('0' + (int)(chunk % 10)); chunk /= 10; }
            }
            if (negative) buf[--at] = '-';
            string s = CoreIntrinsics.FastAllocateString(buf.Length - at);
            Span<char> dst = CoreIntrinsics.StringChars(s);
            for (int i = at; i < buf.Length; i++) dst[i - at] = buf[i];
            return s;
        }
    }
}
