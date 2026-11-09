// The shared engine of Decimal32, Decimal64 and Decimal128 (IEEE 754-2019 decimal floating point, binary integer decimal
// encoding, as System.Numerics.Decimal32/64/128 of .NET 11): decoding and encoding the bits, rounding a decimal digit string
// to the format (round half to even, subnormals, overflow to infinity, the cohort kept: 1.50 stays 150E-2), numeric comparison
// across cohorts, truncating and rounding to a power of ten, and the conversions to and from the integers, double and
// System.Decimal. Values are handled as a decimal digit string and a power of ten, so one code serves the three widths.
// Arithmetic and the math functions are not here (the shadows stop with an explicit NotSupportedException).
#nullable disable
using System;
using System.Globalization;
using NumberFormatInfo = CsharpcCoreLib.Globalization.NumberFormatInfo;

namespace CsharpcCoreLib
{
    internal readonly struct IeeeSpec
    {
        public readonly int Bits, Digits, Bias, MaxBiased, SmallSigBits;

        public IeeeSpec(int bits, int digits, int bias, int maxBiased, int smallSigBits)
        {
            Bits = bits; Digits = digits; Bias = bias; MaxBiased = maxBiased; SmallSigBits = smallSigBits;
        }

        public int EMin => -Bias;
        public int EMax => MaxBiased - Bias;
    }

    internal static class IeeeDecimal
    {
        public static readonly IeeeSpec D32 = new IeeeSpec(32, 7, 101, 191, 23);
        public static readonly IeeeSpec D64 = new IeeeSpec(64, 16, 398, 767, 53);
        public static readonly IeeeSpec D128 = new IeeeSpec(128, 34, 6176, 12287, 113);

        public const int Finite = 0, Infinite = 1, NotANumber = 2;

        private static UInt128 Pow10(int n)
        {
            UInt128 v = 1;
            for (int i = 0; i < n; i++) v *= 10;
            return v;
        }

        public static UInt128 MaxSignificand(IeeeSpec s) => Pow10(s.Digits) - 1;

        private static UInt128 Mask(int bits) => bits >= 128 ? UInt128.MaxValue : (((UInt128)1) << bits) - 1;

        public static void Decode(IeeeSpec s, UInt128 bits, out bool negative, out int kind, out UInt128 sig, out int exponent)
        {
            negative = ((bits >> (s.Bits - 1)) & 1) != 0;
            sig = 0; exponent = 0; kind = Finite;
            int top = (int)((bits >> (s.Bits - 6)) & 0x1F);
            if (top == 0x1E) { kind = Infinite; return; }
            if (top == 0x1F) { kind = NotANumber; return; }
            int expBits = s.Bits - 1 - s.SmallSigBits;
            if (((bits >> (s.Bits - 3)) & 3) == 3)
            {
                exponent = (int)((bits >> (s.SmallSigBits - 2)) & Mask(expBits)) - s.Bias;
                sig = (((UInt128)1) << s.SmallSigBits) | (bits & Mask(s.SmallSigBits - 2));
            }
            else
            {
                exponent = (int)((bits >> s.SmallSigBits) & Mask(expBits)) - s.Bias;
                sig = bits & Mask(s.SmallSigBits);
            }
            if (sig >= Pow10(s.Digits)) sig = 0; // a non-canonical significand is a zero
        }

        public static UInt128 Encode(IeeeSpec s, bool negative, UInt128 sig, int exponent)
        {
            UInt128 biased = (UInt128)(exponent + s.Bias);
            UInt128 bits;
            if (sig >> s.SmallSigBits == 0) bits = (biased << s.SmallSigBits) | sig;
            else bits = (((UInt128)3) << (s.Bits - 3)) | (biased << (s.SmallSigBits - 2)) | (sig & Mask(s.SmallSigBits - 2));
            return negative ? bits | (((UInt128)1) << (s.Bits - 1)) : bits;
        }

        public static UInt128 Infinity(IeeeSpec s, bool negative) =>
            (((UInt128)0x1E) << (s.Bits - 6)) | (negative ? ((UInt128)1) << (s.Bits - 1) : 0);

        /// <summary>The default NaN has its sign bit set, as double.NaN does.</summary>
        public static UInt128 NaN(IeeeSpec s) => (((UInt128)0x1F) << (s.Bits - 6)) | (((UInt128)1) << (s.Bits - 1));

        public static string DigitsOf(UInt128 sig)
        {
            if (sig == 0) return "";
            var chars = new char[40];
            int at = chars.Length;
            while (sig != 0) { chars[--at] = (char)('0' + (int)(sig % 10)); sig /= 10; }
            return new string(chars, at, chars.Length - at);
        }

        private static UInt128 FromDigits(string digits, int length)
        {
            UInt128 v = 0;
            for (int i = 0; i < length; i++) v = v * 10 + (uint)(digits[i] - '0');
            return v;
        }

        private static bool IsZero(UInt128 sig) => sig == 0;

        /// <summary>
        /// The format's bits for digits * 10^exponent (digits without leading zeros, empty for zero; <paramref name="sticky"/> when
        /// more non-zero digits follow): rounded half to even to the format's digits and exponent range.
        /// </summary>
        public static UInt128 Round(IeeeSpec s, bool negative, string digits, int exponent, bool sticky)
        {
            int count = digits.Length;
            if (count == 0)
                return Encode(s, negative, 0, Math.Min(Math.Max(exponent, s.EMin), s.EMax));
            int drop = Math.Max(Math.Max(count - s.Digits, s.EMin - exponent), 0);
            UInt128 sig;
            int e = exponent + drop;
            if (drop == 0) sig = FromDigits(digits, count);
            else
            {
                bool up = false;
                UInt128 kept = 0;
                if (drop <= count)
                {
                    int keptCount = count - drop;
                    kept = FromDigits(digits, keptCount);
                    char round = digits[keptCount];
                    bool rest = sticky;
                    for (int i = keptCount + 1; i < count && !rest; i++) rest = digits[i] != '0';
                    up = round > '5' || (round == '5' && (rest || (kept & 1) == 1));
                }
                sig = kept + (up ? (UInt128)1 : 0);
                if (sig == Pow10(s.Digits)) { sig /= 10; e++; }
            }
            if (sig == 0) return Encode(s, negative, 0, Math.Min(e, s.EMax));
            if (e > s.EMax)
            {
                int pad = e - s.EMax;
                if (DigitsOf(sig).Length + pad > s.Digits) return Infinity(s, negative);
                sig *= Pow10(pad);
                e = s.EMax;
            }
            return Encode(s, negative, sig, e);
        }

        // ---- text ----
        public static bool TryParse(IeeeSpec s, string text, NumberStyles style, IFormatProvider provider, out UInt128 bits, bool throwOnFailure)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            if (text == null)
            {
                if (throwOnFailure) throw new ArgumentNullException("s");
                bits = 0;
                return false;
            }
            if (!Number.TryParseIeeeDecimal(text, style, NumberFormatInfo.GetInstance(provider), out bool negative, out int special, out string digits, out int exponent, out bool sticky))
            {
                if (throwOnFailure) Number.ThrowFormatFor(text);
                bits = 0;
                return false;
            }
            bits = special == 1 ? Infinity(s, negative) : special == 2 ? NaN(s) : Round(s, negative, digits, exponent, sticky);
            return true;
        }

        public static string Format(IeeeSpec s, UInt128 bits, string format, IFormatProvider provider)
        {
            NumberFormatInfo nfi = NumberFormatInfo.GetInstance(provider);
            Decode(s, bits, out bool negative, out int kind, out UInt128 sig, out int exponent);
            if (kind == NotANumber) return nfi.NaNSymbol;
            if (kind == Infinite) return negative ? nfi.NegativeInfinitySymbol : nfi.PositiveInfinitySymbol;
            return Number.FormatIeeeDecimal(negative, DigitsOf(sig), exponent, format, nfi);
        }

        // ---- comparison ----
        /// <summary>-1, 0 or 1 for two non-NaN values; the cohort does not matter (1.0 == 1.00), the zeros are equal.</summary>
        public static int Compare(IeeeSpec s, UInt128 a, UInt128 b)
        {
            Decode(s, a, out bool an, out int ak, out UInt128 asig, out int ae);
            Decode(s, b, out bool bn, out int bk, out UInt128 bsig, out int be);
            bool az = ak == Finite && asig == 0, bz = bk == Finite && bsig == 0;
            if (az && bz) return 0;
            if (az) return bn ? 1 : -1;
            if (bz) return an ? -1 : 1;
            if (an != bn) return an ? -1 : 1;
            int magnitude = CompareMagnitude(ak, asig, ae, bk, bsig, be);
            return an ? -magnitude : magnitude;
        }

        private static int CompareMagnitude(int ak, UInt128 asig, int ae, int bk, UInt128 bsig, int be)
        {
            if (ak == Infinite || bk == Infinite) return ak == bk ? 0 : ak == Infinite ? 1 : -1;
            string da = DigitsOf(asig), db = DigitsOf(bsig);
            int adjustedA = da.Length + ae, adjustedB = db.Length + be;
            if (adjustedA != adjustedB) return adjustedA < adjustedB ? -1 : 1;
            int length = Math.Max(da.Length, db.Length);
            for (int i = 0; i < length; i++)
            {
                char x = i < da.Length ? da[i] : '0', y = i < db.Length ? db[i] : '0';
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        public static bool IsNaN(IeeeSpec s, UInt128 bits) => ((bits >> (s.Bits - 6)) & 0x1F) == 0x1F;
        public static bool IsInfinity(IeeeSpec s, UInt128 bits) => ((bits >> (s.Bits - 6)) & 0x1F) == 0x1E;
        public static bool IsNegative(IeeeSpec s, UInt128 bits) => ((bits >> (s.Bits - 1)) & 1) != 0;

        public static bool IsZero(IeeeSpec s, UInt128 bits)
        {
            Decode(s, bits, out _, out int kind, out UInt128 sig, out _);
            return kind == Finite && sig == 0;
        }

        /// <summary>Numeric equality (a NaN equals nothing, itself included).</summary>
        public static bool NumericEquals(IeeeSpec s, UInt128 a, UInt128 b) => !IsNaN(s, a) && !IsNaN(s, b) && Compare(s, a, b) == 0;

        /// <summary>Equal values hash alike whatever their cohort.</summary>
        public static int HashCode(IeeeSpec s, UInt128 bits)
        {
            Decode(s, bits, out bool negative, out int kind, out UInt128 sig, out int exponent);
            if (kind == NotANumber) return unchecked((int)(((uint)0x1F) << 26));
            if (kind == Infinite) return unchecked((int)(negative ? 0xF8000000u : 0x78000000u));
            if (sig == 0) return 0;
            string digits = DigitsOf(sig).TrimEnd('0');
            exponent += DigitsOf(sig).Length - digits.Length;
            unchecked
            {
                int h = negative ? 17 : 31;
                foreach (char c in digits) h = h * 397 + c;
                return h * 397 + exponent;
            }
        }

        // ---- rounding to a power of ten ----
        /// <summary>Rounds a finite value to a multiple of 10^target (when its exponent is below it) by a MidpointRounding mode (0 even, 1 away, 2 toward zero, 3 down, 4 up).</summary>
        public static UInt128 RoundToExponent(IeeeSpec s, UInt128 bits, int target, int mode)
        {
            Decode(s, bits, out bool negative, out int kind, out UInt128 sig, out int exponent);
            if (kind != Finite || sig == 0 || exponent >= target) return bits;
            string digits = DigitsOf(sig);
            int drop = target - exponent;
            int keptCount = digits.Length - drop;
            UInt128 kept = keptCount > 0 ? FromDigits(digits, keptCount) : 0;
            int cmpHalf; // the dropped digits against exactly half a unit
            bool nonZero = true;
            if (keptCount < 0) cmpHalf = -1;
            else
            {
                char round = digits[keptCount];
                bool rest = false;
                for (int i = keptCount + 1; i < digits.Length && !rest; i++) rest = digits[i] != '0';
                cmpHalf = round > '5' || (round == '5' && rest) ? 1 : round == '5' ? 0 : -1;
            }
            bool up = mode switch
            {
                0 => cmpHalf > 0 || (cmpHalf == 0 && (kept & 1) == 1),
                1 => cmpHalf >= 0,
                2 => false,
                3 => nonZero && negative,
                _ => nonZero && !negative,
            };
            UInt128 rounded = kept + (up ? (UInt128)1 : 0);
            return Round(s, negative, DigitsOf(rounded), target, false);
        }

        // ---- conversions ----
        public static UInt128 Convert(IeeeSpec from, UInt128 bits, IeeeSpec to)
        {
            Decode(from, bits, out bool negative, out int kind, out UInt128 sig, out int exponent);
            if (kind == NotANumber) return NaN(to);
            if (kind == Infinite) return Infinity(to, negative);
            return Round(to, negative, DigitsOf(sig), exponent, false);
        }

        public static UInt128 FromUInt128(IeeeSpec s, bool negative, UInt128 magnitude) => Round(s, negative, DigitsOf(magnitude), 0, false);

        public static UInt128 FromInt128(IeeeSpec s, Int128 value) =>
            value < 0 ? FromUInt128(s, true, (UInt128)(-(value + 1)) + 1) : FromUInt128(s, false, (UInt128)value);

        /// <summary>The exact value of a double (not its shortest text) rounded to the format.</summary>
        public static UInt128 FromDouble(IeeeSpec s, double value)
        {
            if (double.IsNaN(value)) return NaN(s);
            bool negative = BitConverter.DoubleToInt64Bits(value) < 0;
            if (double.IsInfinity(value)) return Infinity(s, negative);
            if (value == 0) return Encode(s, negative, 0, 0);
            Span<byte> storage = stackalloc byte[800];
            var g = new DigitBuffer(storage);
            Number.ExactDigitsOf(Math.Abs(value), ref g);
            var chars = new char[g.N];
            for (int i = 0; i < g.N; i++) chars[i] = (char)g.D[i];
            // The exact digits come without trailing zeros; an integer-valued double has them back (1e20 is 21 digits, so 1.000000E+20).
            int trailing = g.Exp10 - g.N;
            string digits = new string(chars);
            if (trailing > 0) { digits += new string('0', trailing); trailing = 0; }
            return Round(s, negative, digits, trailing, false);
        }

        public static double ToDouble(IeeeSpec s, UInt128 bits)
        {
            Decode(s, bits, out bool negative, out int kind, out UInt128 sig, out int exponent);
            if (kind == NotANumber) return double.NaN;
            if (kind == Infinite) return negative ? double.NegativeInfinity : double.PositiveInfinity;
            if (sig == 0) return negative ? -0.0 : 0.0;
            string digits = DigitsOf(sig);
            var buffer = new byte[digits.Length + 1];
            for (int i = 0; i < digits.Length; i++) buffer[i] = (byte)digits[i];
            return Number.DigitsToDoubleOf(buffer, digits.Length, digits.Length + exponent, negative);
        }

        public static float ToSingle(IeeeSpec s, UInt128 bits)
        {
            Decode(s, bits, out bool negative, out int kind, out UInt128 sig, out int exponent);
            if (kind == NotANumber) return float.NaN;
            if (kind == Infinite) return negative ? float.NegativeInfinity : float.PositiveInfinity;
            if (sig == 0) return negative ? -0.0f : 0.0f;
            string digits = DigitsOf(sig);
            var buffer = new byte[digits.Length + 1];
            for (int i = 0; i < digits.Length; i++) buffer[i] = (byte)digits[i];
            return Number.DigitsToSingle(buffer, digits.Length, digits.Length + exponent, false, negative);
        }

        /// <summary>System.Decimal's value: its scale is the cohort's (1.50 stays 1.50) as far as 28 places go; throws OverflowException beyond its range.</summary>
        public static decimal ToDecimal(IeeeSpec s, UInt128 bits)
        {
            Decode(s, bits, out bool negative, out int kind, out UInt128 sig, out int exponent);
            if (kind != Finite) throw new OverflowException("Value was either too large or too small for a Decimal.");
            if (sig == 0)
            {
                // a zero's scale (0.00) is kept, up to the 28 places System.Decimal has
                int zeroScale = Math.Min(Math.Max(-exponent, 0), 28);
                return negative ? decimal.Negate(new decimal(0, 0, 0, false, (byte)zeroScale)) : new decimal(0, 0, 0, false, (byte)zeroScale);
            }
            string digits = DigitsOf(sig);
            if (exponent > 0)
            {
                if (digits.Length + exponent > 29) throw new OverflowException("Value was either too large or too small for a Decimal.");
                digits += new string('0', exponent);
                exponent = 0;
            }
            // too many places: round half to even to 28 (System.Decimal's own conversion from text rounds the same way)
            int scale = -exponent;
            if (scale > 28 || digits.Length > 29)
            {
                int cut = Math.Max(scale - 28, digits.Length - 29);
                if (cut >= digits.Length + 1) return 0m;
                UInt128 roundedBits = RoundToExponent(s, bits, exponent + cut, 0);
                Decode(s, roundedBits, out negative, out kind, out sig, out int roundedExponent);
                digits = DigitsOf(sig);
                if (roundedExponent < exponent + cut)
                {
                    digits += new string('0', exponent + cut - roundedExponent);
                    roundedExponent = exponent + cut;
                }
                exponent = roundedExponent;
                scale = -exponent;
                if (sig == 0) return 0m;
            }
            if (!UInt128.TryParse(digits, out UInt128 value) || value > ((((UInt128)1) << 96) - 1)) throw new OverflowException("Value was either too large or too small for a Decimal.");
            decimal result = new decimal((int)(uint)value, (int)(uint)(value >> 32), (int)(uint)(value >> 64), negative, (byte)scale);
            return result;
        }

        public static UInt128 FromDecimal(IeeeSpec s, decimal value)
        {
            int[] parts = decimal.GetBits(value);
            bool negative = (parts[3] & int.MinValue) != 0;
            int scale = (parts[3] >> 16) & 0xFF;
            UInt128 magnitude = ((UInt128)(uint)parts[2] << 64) | ((UInt128)(uint)parts[1] << 32) | (uint)parts[0];
            return Round(s, negative, DigitsOf(magnitude), -scale, false);
        }

        /// <summary>
        /// The value truncated toward zero as a sign and a magnitude. 0: exact; 1: a NaN; 2: an infinity or a magnitude beyond 128 bits.
        /// </summary>
        public static int Truncate(IeeeSpec s, UInt128 bits, out bool negative, out UInt128 magnitude)
        {
            Decode(s, bits, out negative, out int kind, out UInt128 sig, out int exponent);
            magnitude = 0;
            if (kind == NotANumber) return 1;
            if (kind == Infinite) return 2;
            if (sig == 0) return 0;
            string digits = DigitsOf(sig);
            if (exponent < 0)
            {
                int keep = digits.Length + exponent;
                if (keep <= 0) return 0;
                magnitude = FromDigits(digits, keep);
                return 0;
            }
            if (digits.Length + exponent > 39) return 2;
            digits += new string('0', exponent);
            if (digits.Length == 39 && string.CompareOrdinal(digits, "340282366920938463463374607431768211455") > 0) return 2;
            magnitude = FromDigits(digits, digits.Length);
            return 0;
        }

        private static Exception Overflow() => new OverflowException("Arithmetic operation resulted in an overflow.");

        /// <summary>The value as a signed integer in [min, max]: saturating (a NaN is 0), or throwing OverflowException when checked.</summary>
        public static Int128 ToSigned(IeeeSpec s, UInt128 bits, Int128 min, Int128 max, bool check)
        {
            int status = Truncate(s, bits, out bool negative, out UInt128 magnitude);
            if (status == 1) { if (check) throw Overflow(); return 0; }
            bool over = status == 2;
            Int128 value = 0;
            if (!over)
            {
                if (negative) { if (magnitude > (UInt128)Int128.MaxValue + 1) over = true; else value = magnitude == (UInt128)Int128.MaxValue + 1 ? Int128.MinValue : -(Int128)magnitude; }
                else if (magnitude > (UInt128)Int128.MaxValue) over = true;
                else value = (Int128)magnitude;
            }
            if (over) value = negative ? min : max;
            if (value < min || value > max)
            {
                if (check) throw Overflow();
                return value < min ? min : max;
            }
            if (over && check) throw Overflow();
            return value;
        }

        public static UInt128 ToUnsigned(IeeeSpec s, UInt128 bits, UInt128 max, bool check)
        {
            int status = Truncate(s, bits, out bool negative, out UInt128 magnitude);
            if (status == 1) { if (check) throw Overflow(); return 0; }
            if (status == 2) { if (check) throw Overflow(); return negative ? (UInt128)0 : max; }
            if (magnitude == 0) return 0;
            if (negative) { if (check) throw Overflow(); return 0; }
            if (magnitude > max) { if (check) throw Overflow(); return max; }
            return magnitude;
        }
    }
}
