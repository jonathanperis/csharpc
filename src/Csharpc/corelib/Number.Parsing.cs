// What remains of the corelib's own number code once number parsing and formatting are the framework's (#426): the correctly
// rounded decimal -> double conversion decimal uses (DecimalOps), Math.Round(double, digits, mode) on the exact decimal value, and
// the Int128 entry points of the compiler's own paths. The primitives' Parse/TryParse are hosted from their IL, and Enum.Parse's
// integers go through them (EnumRuntime).
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static partial class Number
    {
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
        // Int128 / UInt128 -> string
        // ==========================================================================================

        /// <summary>Boxed Int128's / UInt128's ToString (the runtime passes the payload's low and high words).</summary>
        [RuntimeExport("csrt_int128_to_string", "csrt_Boxed_Int128_MT")]
        internal static string Int128ToString(ulong lo, ulong hi) => FormatInt128(lo, hi, true);

        [RuntimeExport("csrt_uint128_to_string", "csrt_Boxed_UInt128_MT")]
        internal static string UInt128ToString(ulong lo, ulong hi) => FormatInt128(lo, hi, false);

        /// <summary>IFormattable.ToString on a boxed Int128 / UInt128: CoreLib's own 128-bit formatter.</summary>
        internal static string FormatBoxedInt128(ulong lo, ulong hi, bool isSigned, string format, IFormatProvider provider) =>
            isSigned ? UpstreamNumber.FormatInt128(null, new Int128(hi, lo), format, provider) : UpstreamNumber.FormatUInt128(null, new UInt128(hi, lo), format, provider);

        internal static string FormatInt128(ulong lo, ulong hi, bool isSigned) => FormatBoxedInt128(lo, hi, isSigned, null, null);
    }
}
