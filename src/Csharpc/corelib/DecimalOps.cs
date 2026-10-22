// System.Decimal arithmetic, conversions and parsing in C# (formerly src/runtime/decimal.c; ADR 0008).
//
// Layout (the CLR's): { int flags; uint hi32; ulong lo64 }, flags = scale (0..28) in bits 16-23 and
// the sign in bit 31; value = (-1)^sign * (hi32:lo64) / 10^scale. Every operation computes the exact
// result with BigNum and then fits it into 96 bits / scale <= 28 by dividing by a power of ten with
// round-half-to-even, the contract of the CLR's DecCalc.ScaleResult. Scales follow .NET: a+b keeps
// max(scale), a*b sums them, a/b uses the smallest scale that represents the quotient exactly (up to
// 28 digits). The codegen's decimal operators call these methods (FunctionCodeGen.Bcl.cs); boxed
// decimals' Equals/GetHashCode in the C runtime use the exports.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class DecimalOps
    {
        private const int Limbs = 16;  // 96-bit operands scaled by 10^28 or multiplied stay far below 512 bits

        private static int Scale(decimal d) => (CoreIntrinsics.DecimalFlags(d) >> 16) & 0xFF;
        private static bool IsNegative(decimal d) => CoreIntrinsics.DecimalFlags(d) < 0;
        private static bool IsZero(decimal d) => CoreIntrinsics.DecimalLo(d) == 0 && CoreIntrinsics.DecimalHi(d) == 0;

        private static void Overflow() => throw new OverflowException("Value was either too large or too small for a Decimal.");

        private static void Load(decimal d, ref BigNum b)
        {
            ulong lo = CoreIntrinsics.DecimalLo(d);
            b.W[0] = (uint)lo;
            b.W[1] = (uint)(lo >> 32);
            b.W[2] = CoreIntrinsics.DecimalHi(d);
            b.Len = 3;
            b.Normalize();
        }

        private static decimal Pack(ref BigNum m, int scale, bool negative)
        {
            ulong lo = m.Len > 0 ? m.W[0] : 0U;
            if (m.Len > 1) lo |= (ulong)m.W[1] << 32;
            uint hi = m.Len > 2 ? m.W[2] : 0U;
            int flags = (scale << 16) | (negative && m.Len != 0 ? int.MinValue : 0);
            return CoreIntrinsics.MakeDecimal(flags, hi, lo);
        }

        private static bool IsOdd(ref BigNum m) => m.Len > 0 && (m.W[0] & 1) != 0;

        /// <summary>q = a / b, r = a % b (b != 0); a is preserved.</summary>
        private static void DivMod(ref BigNum a, ref BigNum b, ref BigNum q, ref BigNum r)
        {
            if (b.Len == 1)
            {
                q.CopyFrom(ref a);
                r.SetUInt64(q.DivRemSmall(b.W[0]));
                return;
            }
            int bits = a.BitLength();
            int words = (bits + 31) / 32;
            for (int i = 0; i < words; i++) q.W[i] = 0;
            q.Len = words;
            r.Len = 0;
            for (int bit = bits - 1; bit >= 0; bit--)
            {
                r.ShiftLeft(1);
                if (((a.W[bit >> 5] >> (bit & 31)) & 1) != 0) r.AddSmall(1);
                if (BigNum.Compare(ref r, ref b) >= 0)
                {
                    r.Subtract(ref b);
                    q.W[bit >> 5] |= 1U << (bit & 31);
                }
            }
            q.Normalize();
        }

        private static void MultiplyMagnitudes(ref BigNum a, ref BigNum b, ref BigNum r)
        {
            int n = a.Len + b.Len;
            for (int i = 0; i < n; i++) r.W[i] = 0;
            for (int i = 0; i < a.Len; i++)
            {
                ulong carry = 0;
                for (int j = 0; j < b.Len; j++)
                {
                    ulong t = (ulong)a.W[i] * b.W[j] + r.W[i + j] + carry;
                    r.W[i + j] = (uint)t;
                    carry = t >> 32;
                }
                r.W[i + b.Len] = (uint)carry;
            }
            r.Len = n;
            r.Normalize();
        }

        private static void AddMagnitudes(ref BigNum a, ref BigNum b)
        {
            int n = a.Len > b.Len ? a.Len : b.Len;
            ulong carry = 0;
            for (int i = 0; i < n; i++)
            {
                ulong t = (ulong)(i < a.Len ? a.W[i] : 0U) + (i < b.Len ? b.W[i] : 0U) + carry;
                a.W[i] = (uint)t;
                carry = t >> 32;
            }
            a.Len = n;
            if (carry != 0) a.W[a.Len++] = (uint)carry;
        }

        /// <summary>Fits an exact (m, scale) into 96 bits and scale &lt;= 28, rounding half to even.</summary>
        private static decimal Fit(ref BigNum m, int scale, bool negative)
        {
            if (scale <= 28 && m.Len <= 3) return Pack(ref m, scale, negative);
            int k = scale > 28 ? scale - 28 : 0;
            if (k == 0) k = 1;
            int excess = m.BitLength() - 96;  // each decimal digit is ~3.32 bits: skip hopeless tries
            if (excess > 0 && excess * 30103 / 100000 > k) k = excess * 30103 / 100000;
            Span<uint> ds = stackalloc uint[Limbs];
            Span<uint> qs = stackalloc uint[Limbs];
            Span<uint> rs = stackalloc uint[Limbs];
            var divisor = new BigNum(ds);
            var q = new BigNum(qs);
            var rem = new BigNum(rs);
            for (;; k++)
            {
                if (k > scale) Overflow();
                divisor.SetUInt64(1);
                divisor.MulPow10(k);
                DivMod(ref m, ref divisor, ref q, ref rem);
                rem.ShiftLeft(1);
                int c = BigNum.Compare(ref rem, ref divisor);
                if (c > 0 || (c == 0 && IsOdd(ref q))) q.AddSmall(1);
                if (q.Len <= 3) return Pack(ref q, scale - k, negative);
            }
        }

        // ==========================================================================================
        // Arithmetic
        // ==========================================================================================

        private static int Align(decimal a, decimal b, ref BigNum ma, ref BigNum mb)
        {
            int sa = Scale(a), sb = Scale(b);
            Load(a, ref ma);
            Load(b, ref mb);
            if (sa < sb) ma.MulPow10(sb - sa);
            else if (sb < sa) mb.MulPow10(sa - sb);
            return sa > sb ? sa : sb;
        }

        private static decimal AddSigned(decimal a, decimal b, bool negateB)
        {
            Span<uint> sa = stackalloc uint[Limbs];
            Span<uint> sb = stackalloc uint[Limbs];
            var ma = new BigNum(sa);
            var mb = new BigNum(sb);
            int scale = Align(a, b, ref ma, ref mb);
            bool na = IsNegative(a), nb = IsNegative(b) ^ negateB;
            if (na == nb)
            {
                AddMagnitudes(ref ma, ref mb);
                return Fit(ref ma, scale, na);
            }
            if (BigNum.Compare(ref ma, ref mb) >= 0)
            {
                ma.Subtract(ref mb);
                return Fit(ref ma, scale, na);
            }
            mb.Subtract(ref ma);
            return Fit(ref mb, scale, nb);
        }

        internal static decimal Add(decimal a, decimal b) => AddSigned(a, b, false);
        internal static decimal Subtract(decimal a, decimal b) => AddSigned(a, b, true);

        internal static decimal Multiply(decimal a, decimal b)
        {
            Span<uint> sa = stackalloc uint[Limbs];
            Span<uint> sb = stackalloc uint[Limbs];
            Span<uint> sp = stackalloc uint[Limbs];
            var ma = new BigNum(sa);
            var mb = new BigNum(sb);
            var p = new BigNum(sp);
            Load(a, ref ma);
            Load(b, ref mb);
            MultiplyMagnitudes(ref ma, ref mb, ref p);
            return Fit(ref p, Scale(a) + Scale(b), IsNegative(a) != IsNegative(b));
        }

        internal static decimal Divide(decimal a, decimal b)
        {
            if (IsZero(b)) throw new DivideByZeroException();
            Span<uint> sa = stackalloc uint[Limbs];
            Span<uint> sb = stackalloc uint[Limbs];
            Span<uint> sq = stackalloc uint[Limbs];
            Span<uint> sr = stackalloc uint[Limbs];
            Span<uint> sq10 = stackalloc uint[Limbs];
            Span<uint> sr10 = stackalloc uint[Limbs];
            Span<uint> sd = stackalloc uint[Limbs];
            Span<uint> sn = stackalloc uint[Limbs];
            var ma = new BigNum(sa);
            var mb = new BigNum(sb);
            Load(a, ref ma);
            Load(b, ref mb);
            bool negative = IsNegative(a) != IsNegative(b);
            int scale = Scale(a) - Scale(b);
            if (scale < 0) { ma.MulPow10(-scale); scale = 0; }
            if (ma.IsZero) return Pack(ref ma, scale, false);

            var q = new BigNum(sq);
            var rem = new BigNum(sr);
            DivMod(ref ma, ref mb, ref q, ref rem);
            // Extend the quotient digit by digit while it's inexact, the scale allows and it still fits.
            var q10 = new BigNum(sq10);
            var r10 = new BigNum(sr10);
            var digit = new BigNum(sd);
            var nrem = new BigNum(sn);
            while (!rem.IsZero && scale < 28)
            {
                q10.CopyFrom(ref q);
                q10.MulSmall(10);
                r10.CopyFrom(ref rem);
                r10.MulSmall(10);
                DivMod(ref r10, ref mb, ref digit, ref nrem);
                AddMagnitudes(ref q10, ref digit);
                if (q10.Len > 3) break;
                q.CopyFrom(ref q10);
                rem.CopyFrom(ref nrem);
                scale++;
            }
            if (!rem.IsZero)
            {
                rem.ShiftLeft(1);
                int c = BigNum.Compare(ref rem, ref mb);
                if (c > 0 || (c == 0 && IsOdd(ref q))) q.AddSmall(1);
            }
            if (q.IsZero) scale = 0;  // .NET: 1e-28m / 3 == 0 (scale 0)
            return Fit(ref q, scale, negative);
        }

        internal static decimal Remainder(decimal a, decimal b)
        {
            if (IsZero(b)) throw new DivideByZeroException();
            if (IsZero(a)) return a;
            // As DecCalc.VarDecMod: |a| < |b| gives a unchanged; |a| == |b| gives a zero with the larger scale.
            int cmp = Compare(Abs(a), Abs(b));
            if (cmp < 0) return a;
            if (cmp == 0)
            {
                int scale0 = Scale(a) > Scale(b) ? Scale(a) : Scale(b);
                return CoreIntrinsics.MakeDecimal((scale0 << 16) | (CoreIntrinsics.DecimalFlags(a) & int.MinValue), 0, 0);
            }
            Span<uint> sa = stackalloc uint[Limbs];
            Span<uint> sb = stackalloc uint[Limbs];
            Span<uint> sq = stackalloc uint[Limbs];
            Span<uint> sr = stackalloc uint[Limbs];
            var ma = new BigNum(sa);
            var mb = new BigNum(sb);
            int scale = Align(a, b, ref ma, ref mb);
            if (mb.IsZero) throw new DivideByZeroException();
            var q = new BigNum(sq);
            var rem = new BigNum(sr);
            DivMod(ref ma, ref mb, ref q, ref rem);
            return Fit(ref rem, scale, IsNegative(a));
        }

        internal static int Compare(decimal a, decimal b)
        {
            Span<uint> sa = stackalloc uint[Limbs];
            Span<uint> sb = stackalloc uint[Limbs];
            var ma = new BigNum(sa);
            var mb = new BigNum(sb);
            Align(a, b, ref ma, ref mb);
            bool na = IsNegative(a) && !ma.IsZero, nb = IsNegative(b) && !mb.IsZero;
            if (na != nb) return na ? -1 : 1;
            int c = BigNum.Compare(ref ma, ref mb);
            return na ? -c : c;
        }

        [RuntimeExport("csrt_decimal_compare", "csrt_Boxed_Decimal_MT")]
        internal static int CompareAt(ref decimal a, ref decimal b) => Compare(a, b);

        private static decimal Abs(decimal a) =>
            CoreIntrinsics.MakeDecimal(CoreIntrinsics.DecimalFlags(a) & int.MaxValue, CoreIntrinsics.DecimalHi(a), CoreIntrinsics.DecimalLo(a));

        internal static decimal Negate(decimal a) =>
            IsZero(a) ? a : CoreIntrinsics.MakeDecimal(CoreIntrinsics.DecimalFlags(a) ^ int.MinValue, CoreIntrinsics.DecimalHi(a), CoreIntrinsics.DecimalLo(a));

        internal static int Hash(decimal d)
        {
            // Equal values (1.0m == 1.00m) must hash alike: hash the normalized value.
            Span<uint> sm = stackalloc uint[4];
            Span<uint> st = stackalloc uint[4];
            var m = new BigNum(sm);
            var t = new BigNum(st);
            Load(d, ref m);
            int scale = Scale(d);
            while (scale > 0)
            {
                t.CopyFrom(ref m);
                if (t.DivRemSmall(10) != 0) break;
                m.CopyFrom(ref t);
                scale--;
            }
            uint h = (m.Len > 0 ? m.W[0] : 0U) ^ (m.Len > 1 ? m.W[1] : 0U) ^ (m.Len > 2 ? m.W[2] : 0U) ^ (uint)scale;
            if (IsNegative(d) && !m.IsZero) h ^= 0x80000000u;
            return (int)h;
        }

        [RuntimeExport("csrt_decimal_get_hash_code", "csrt_Boxed_Decimal_MT")]
        internal static int HashAt(ref decimal d) => Hash(d);

        /// <summary>Math.Round / decimal.Round / Floor / Ceiling / Truncate. mode = MidpointRounding:
        /// ToEven 0, AwayFromZero 1, ToZero 2, ToNegativeInfinity 3, ToPositiveInfinity 4.</summary>
        internal static decimal Round(decimal a, int decimals, int mode)
        {
            int scale = Scale(a);
            if (decimals < 0 || decimals > 28)
                throw new ArgumentOutOfRangeException("decimals", "Decimal can only round to between 0 and 28 digits of precision.");
            if (scale <= decimals) return a;
            Span<uint> sm = stackalloc uint[Limbs];
            Span<uint> sd = stackalloc uint[Limbs];
            Span<uint> sq = stackalloc uint[Limbs];
            Span<uint> sr = stackalloc uint[Limbs];
            var m = new BigNum(sm);
            var divisor = new BigNum(sd);
            var q = new BigNum(sq);
            var rem = new BigNum(sr);
            Load(a, ref m);
            divisor.SetUInt64(1);
            divisor.MulPow10(scale - decimals);
            DivMod(ref m, ref divisor, ref q, ref rem);
            bool negative = IsNegative(a);
            bool up = false;
            if (!rem.IsZero)
            {
                rem.ShiftLeft(1);
                int c = BigNum.Compare(ref rem, ref divisor);
                up = mode switch
                {
                    1 => c >= 0,
                    2 => false,
                    3 => negative,
                    4 => !negative,
                    _ => c > 0 || (c == 0 && IsOdd(ref q)),
                };
            }
            if (up) q.AddSmall(1);
            return Pack(ref q, decimals, negative);
        }

        // ==========================================================================================
        // Conversions
        // ==========================================================================================

        internal static decimal FromInt64(long v)
        {
            ulong magnitude = v < 0 ? 0UL - (ulong)v : (ulong)v;
            return CoreIntrinsics.MakeDecimal(v < 0 ? int.MinValue : 0, 0, magnitude);
        }

        internal static decimal FromUInt64(ulong v) => CoreIntrinsics.MakeDecimal(0, 0, v);

        /// <summary>.NET 11 converts doubles exactly: the binary value's decimal expansion, rounded
        /// (half even) to what fits. (decimal)0.1 == 0.1000000000000000055511151231.</summary>
        internal static decimal FromDouble(double v)
        {
            if (v != v || v >= 7.9228162514264338e28 || v <= -7.9228162514264338e28) Overflow();
            if (v == 0) return CoreIntrinsics.MakeDecimal(0, 0, 0);
            Span<byte> storage = stackalloc byte[800];
            var g = new DigitBuffer(storage);
            Number.ExactDigitsOf(v, ref g);
            Span<uint> sm = stackalloc uint[Limbs];
            Span<uint> st = stackalloc uint[Limbs];
            var m = new BigNum(sm);
            var t = new BigNum(st);
            m.Len = 0;
            for (int i = 0; i < g.Exp10; i++) m.MulAdd10(i < g.N ? (uint)(g.D[i] - '0') : 0U);
            int scale = 0;
            int firstDropped = -1;
            bool droppedNonZero = false;
            for (int pos = g.Exp10; pos < g.N; pos++)
            {
                uint digit = pos < 0 ? 0U : (uint)(g.D[pos] - '0');
                if (firstDropped >= 0) { if (digit != 0) droppedNonZero = true; continue; }
                if (scale < 28)
                {
                    t.CopyFrom(ref m);
                    t.MulAdd10(digit);
                    if (t.Len <= 3) { m.CopyFrom(ref t); scale++; continue; }
                }
                firstDropped = (int)digit;
            }
            // An inexact result keeps all its digits ((decimal)-1e-20 == -0.0000000000000000000100000000).
            if (firstDropped >= 0 && (firstDropped > 5 || (firstDropped == 5 && (droppedNonZero || IsOdd(ref m))))) m.AddSmall(1);
            if (m.IsZero) return CoreIntrinsics.MakeDecimal(0, 0, 0);  // below 0.5e-28: plain 0
            return Fit(ref m, scale, v < 0);
        }

        internal static decimal FromSingle(float v) => FromDouble(v);

        /// <summary>(double)decimal, correctly rounded ((double)(1m/3m) == 0.3333333333333333).</summary>
        internal static double ToDouble(decimal d)
        {
            Span<byte> digits = stackalloc byte[32];
            int n = MantissaDigits(d, digits);
            int scale = Scale(d);
            int trimmed = n;
            while (trimmed > 0 && digits[trimmed - 1] == (byte)'0') trimmed--;
            return Number.DigitsToDoubleOf(digits, trimmed, n - scale, IsNegative(d));
        }

        internal static double ToDoubleAt(ref decimal d) => ToDouble(d);
        internal static float ToSingleAt(ref decimal d) => (float)ToDouble(d);

        private static int MantissaDigits(decimal d, Span<byte> digits)
        {
            Span<uint> sm = stackalloc uint[4];
            var m = new BigNum(sm);
            Load(d, ref m);
            if (m.IsZero) return 0;
            Span<byte> tmp = stackalloc byte[32];
            int n = 0;
            while (!m.IsZero) tmp[n++] = (byte)('0' + m.DivRemSmall(10));
            for (int i = 0; i < n; i++) digits[i] = tmp[n - 1 - i];
            return n;
        }

        /// <summary>Truncating conversion to an integer type; kind: 1 sbyte, 2 byte, 3 short, 4 ushort,
        /// 5 int, 6 uint, 7 long, 8 ulong, 9 char. Out of range throws OverflowException (always checked).</summary>
        internal static long ToIntegerAt(ref decimal d, int kind)
        {
            Span<uint> sm = stackalloc uint[4];
            var m = new BigNum(sm);
            Load(d, ref m);
            for (int s = Scale(d); s > 0; s--) m.DivRemSmall(10);
            bool negative = IsNegative(d) && !m.IsZero;
            long min = kind switch { 1 => -128, 3 => -32768, 5 => int.MinValue, 7 => long.MinValue, _ => 0 };
            ulong max = kind switch { 1 => 127, 2 => 255, 3 => 32767, 4 => 65535, 5 => int.MaxValue, 6 => uint.MaxValue, 7 => long.MaxValue, 8 => ulong.MaxValue, _ => 65535 };
            bool ok = m.Len <= 2;
            ulong magnitude = (m.Len > 0 ? m.W[0] : 0U) | ((ulong)(m.Len > 1 ? m.W[1] : 0U) << 32);
            if (ok) ok = negative ? min < 0 && magnitude <= 0UL - (ulong)min : magnitude <= max;
            if (!ok)
            {
                string name = kind switch
                {
                    1 => "a signed byte", 2 => "an unsigned byte", 3 => "an Int16", 4 => "a UInt16", 5 => "an Int32", 6 => "a UInt32",
                    7 => "an Int64", 8 => "a UInt64", _ => "a character",
                };
                throw new OverflowException("Value was either too large or too small for " + name + ".");
            }
            return negative ? (long)(0UL - magnitude) : (long)magnitude;
        }

        // ==========================================================================================
        // Parsing: NumberStyles.Number (invariant): whitespace, sign, thousands separators, decimal point
        // ==========================================================================================

        private static int TryParseCore(string s, out decimal result)
        {
            result = CoreIntrinsics.MakeDecimal(0, 0, 0);
            if (s == null) return 0;
            int i = 0, n = s.Length;
            while (i < n && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
            bool negative = false;
            if (i < n && (s[i] == '-' || s[i] == '+')) { negative = s[i] == '-'; i++; }
            Span<uint> sm = stackalloc uint[Limbs];
            Span<uint> st = stackalloc uint[Limbs];
            var m = new BigNum(sm);
            var t = new BigNum(st);
            m.Len = 0;
            int scale = 0;
            bool point = false, any = false, dropped = false;
            int firstDropped = -1;
            for (; i < n; i++)
            {
                char c = s[i];
                if (c == ',' && !point && any) continue;
                if (c == '.' && !point) { point = true; continue; }
                if (c < '0' || c > '9') break;
                any = true;
                uint digit = (uint)(c - '0');
                t.CopyFrom(ref m);
                t.MulAdd10(digit);
                if (t.Len <= 3 && (!point || scale < 28))
                {
                    m.CopyFrom(ref t);
                    if (point) scale++;
                }
                else if (point)
                {
                    if (firstDropped < 0) firstDropped = (int)digit;
                    else if (digit != 0) dropped = true;
                }
                else return 2;
            }
            while (i < n && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
            if (!any || i != n) return 0;
            if (firstDropped >= 0 && (firstDropped > 5 || (firstDropped == 5 && (dropped || IsOdd(ref m)))))
            {
                m.AddSmall(1);
                if (m.Len > 3) return 2;
            }
            result = Pack(ref m, scale, negative);
            return 1;
        }

        internal static decimal Parse(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            int status = TryParseCore(s, out decimal result);
            if (status == 1) return result;
            if (status == 2) Overflow();
            throw new FormatException("The input string was not in a correct format.");
        }

        internal static bool TryParse(string s, ref decimal result)
        {
            bool ok = TryParseCore(s, out decimal value) == 1;
            result = value;
            return ok;
        }
    }
}
