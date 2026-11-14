// The members of System.Decimal that the framework's IL served for BFloat16 and Decimal32/64/128 calls and the reference pack doesn't
// declare (#405): decimal.IsNegative (public generic-math surface csharpc lacked), the parts High/Low/Mid and DecDivMod1E9 that
// Number.DecimalToNumber reads, and VarBFloat16FromDec.
//
// decimal -> BFloat16, correctly rounded: the CoreLib-internal decimal.VarBFloat16FromDec that the framework's BFloat16 IL calls for its
// explicit conversion from decimal. CoreLib's own version lives in decimal's private DecCalc, which a slice cannot carry; this is
// the same contract: round the exact value mantissa / 10^scale once, to nearest with ties to even. Going through double would round twice
// (decimal -> 53 bits -> 8 bits), which is wrong when the 53-bit rounding lands exactly on a BFloat16 tie: (BFloat16)(decimal)(2^90 +
// 2^82 + 1) is 2^90 + 2^83, while the double 2^90 + 2^82 then ties down to 2^90.
#nullable disable
using System;
using System.Numerics;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Decimal")]
    internal static class DecimalContractMembers
    {
        public static bool IsNegative(decimal value) => CoreIntrinsics.DecimalFlags(value) < 0;

        [ShadowInstance] public static uint get_High(decimal value) => CoreIntrinsics.DecimalHi(value);
        [ShadowInstance] public static uint get_Low(decimal value) => (uint)CoreIntrinsics.DecimalLo(value);
        [ShadowInstance] public static uint get_Mid(decimal value) => (uint)(CoreIntrinsics.DecimalLo(value) >> 32);

        /// <summary>Divides the 96-bit mantissa by 10^9 in place and returns the remainder; the sign and the scale stay (DecCalc.DecDivMod1E9).</summary>
        public static uint DecDivMod1E9(ref decimal value)
        {
            const uint TenToPowerNine = 1_000_000_000;
            int flags = CoreIntrinsics.DecimalFlags(value);
            ulong lo64 = CoreIntrinsics.DecimalLo(value);
            uint hi = CoreIntrinsics.DecimalHi(value), mid = (uint)(lo64 >> 32), lo = (uint)lo64;
            ulong high64 = ((ulong)hi << 32) + mid;
            ulong div64 = high64 / TenToPowerNine;
            ulong num = ((high64 - (uint)div64 * TenToPowerNine) << 32) + lo;
            uint div = (uint)(num / TenToPowerNine);
            value = new decimal((int)div, (int)(uint)div64, (int)(uint)(div64 >> 32), flags < 0, (byte)((flags >> 16) & 0xFF));
            return (uint)num - div * TenToPowerNine;
        }

        public static BFloat16 VarBFloat16FromDec(in decimal value)
        {
            int flags = CoreIntrinsics.DecimalFlags(value);
            bool negative = flags < 0;
            int scale = (flags >> 16) & 0xFF;
            UInt128 mantissa = ((UInt128)CoreIntrinsics.DecimalHi(value) << 64) | CoreIntrinsics.DecimalLo(value);
            if (mantissa == 0) return negative ? BFloat16.NegativeZero : BFloat16.Zero;

            // value = mantissa / 10^scale (mantissa < 2^96, 10^scale < 2^94). Pick k so that q = mantissa * 2^k / 10^scale has 9 bits
            // (one more than BFloat16's 8-bit significand, so the quotient's top bit and the rounding bit are both in it): estimate from
            // the bit lengths, then adjust by one. Both N and D stay below 2^128: N ~ q * D < 2^9 * 2^94, D < 2^94 * 2^(96-8).
            UInt128 power = UInt128.One;
            for (int i = 0; i < scale; i++) power *= 10;
            int k = 9 - (BitLength(mantissa) - BitLength(power));
            UInt128 q, r;
            while (true)
            {
                UInt128 n = k >= 0 ? mantissa << k : mantissa;
                UInt128 d = k >= 0 ? power : power << -k;
                (q, r) = UInt128.DivRem(n, d);
                if (q >= 1u << 9) { k--; continue; }
                if (q < 1u << 8) { k++; continue; }
                break;
            }
            // q has 9 significant bits: keep 8, round the last one (and the remainder) to nearest, ties to even.
            uint significand = (uint)(q >> 1);
            bool half = ((uint)q & 1) != 0;
            bool aboveHalf = half && r != 0;
            if (half && (aboveHalf || (significand & 1) != 0)) significand++;
            int exponent = 8 - k;                      // value ~ significand * 2^(exponent - 7), significand in [2^7, 2^8): 1.f * 2^exponent
            if (significand == 1u << 8) { significand >>= 1; exponent++; }
            // Every decimal's magnitude (1e-28 .. 7.9e28) is a normal BFloat16 (2^-126 .. 3.4e38): no subnormal or overflow case.
            uint bits = (negative ? 0x8000u : 0) | ((uint)(exponent + 127) << 7) | (significand & 0x7F);
            // A float with the same top 16 bits converts to BFloat16 exactly (its low 16 bits are zero).
            return (BFloat16)BitConverter.Int32BitsToSingle((int)(bits << 16));
        }

        private static int BitLength(UInt128 x) => 128 - (int)UInt128.LeadingZeroCount(x);
    }
}
