// System.Numerics.BigInteger in C# (formerly src/runtime/bigint.c; ADR 0008).
//
// A BigInteger value is a reference to an immutable uint[] (null means zero, so default(BigInteger)
// and zeroed arrays are correct): element 0 is the sign (1 or -1), elements 1.. the little-endian
// magnitude without leading zero limbs. One primitive array per value: a single allocation the GC
// never scans. Schoolbook multiplication, Knuth's algorithm D for division. The codegen
// (BclValueTypes.cs) still names the csrt_bigint_* entry points; CoreLibRuntime redirects them here.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class BigIntegerOps
    {
        private static int SignOf(uint[] a) => a == null ? 0 : (int)a[0];
        private static int LengthOf(uint[] a) => a == null ? 0 : a.Length - 1;

        /// <summary>A value from sign and limbs[offset .. offset+length) (leading zeros trimmed).</summary>
        private static uint[] Make(int sign, uint[] limbs, int offset, int length)
        {
            while (length > 0 && limbs[offset + length - 1] == 0) length--;
            if (length == 0) return null;
            uint[] r = new uint[length + 1];
            r[0] = (uint)sign;
            for (int i = 0; i < length; i++) r[i + 1] = limbs[offset + i];
            return r;
        }

        internal static uint[] FromUInt64(ulong v)
        {
            if (v == 0) return null;
            return (v >> 32) == 0 ? new uint[] { 1, (uint)v } : new uint[] { 1, (uint)v, (uint)(v >> 32) };
        }

        internal static uint[] FromInt64(long v)
        {
            if (v == 0) return null;
            ulong magnitude = v < 0 ? 0UL - (ulong)v : (ulong)v;
            uint sign = v < 0 ? uint.MaxValue : 1U;
            return (magnitude >> 32) == 0 ? new uint[] { sign, (uint)magnitude } : new uint[] { sign, (uint)magnitude, (uint)(magnitude >> 32) };
        }

        internal static uint[] FromDouble(double v)
        {
            if (v != v || v == double.PositiveInfinity || v == double.NegativeInfinity)
                throw new OverflowException("Value was either too large or too small for a BigInteger.");
            long bits = CoreIntrinsics.DoubleToBits(v);
            int biased = (int)((bits >> 52) & 0x7FF);
            if (biased < 1023) return null;  // |v| < 1 truncates to zero
            ulong mantissa = ((ulong)bits & 0xFFFFFFFFFFFFFUL) | (1UL << 52);
            int shift = biased - 1075;  // |v| = mantissa * 2^shift
            uint sign = bits < 0 ? uint.MaxValue : 1U;
            if (shift <= 0) return Make((int)sign, new uint[] { (uint)(mantissa >> -shift), (uint)((mantissa >> -shift) >> 32) }, 0, 2);
            int words = shift / 32, bitShift = shift % 32;
            uint[] limbs = new uint[words + 3];
            limbs[words] = (uint)(mantissa << bitShift);
            limbs[words + 1] = (uint)((mantissa << bitShift) >> 32);
            limbs[words + 2] = bitShift == 0 ? 0U : (uint)(mantissa >> (64 - bitShift));
            return Make((int)sign, limbs, 0, limbs.Length);
        }

        // ==========================================================================================
        // Magnitudes (arrays with an offset of 1, as stored)
        // ==========================================================================================

        private static int CompareMagnitudes(uint[] a, uint[] b)
        {
            int na = LengthOf(a), nb = LengthOf(b);
            if (na != nb) return na < nb ? -1 : 1;
            for (int i = na; i >= 1; i--) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return 0;
        }

        private static uint[] AddSigned(uint[] a, uint[] b, bool negateB)
        {
            int sa = SignOf(a), sb = SignOf(b) * (negateB ? -1 : 1);
            if (sb == 0) return a;
            if (sa == 0) return negateB ? Negate(b) : b;
            int na = LengthOf(a), nb = LengthOf(b);
            if (sa == sb)
            {
                if (na < nb) { uint[] t = a; a = b; b = t; int tn = na; na = nb; nb = tn; }
                uint[] r = new uint[na + 2];
                ulong carry = 0;
                for (int i = 1; i <= na; i++)
                {
                    ulong s = (ulong)a[i] + (i <= nb ? b[i] : 0U) + carry;
                    r[i] = (uint)s;
                    carry = s >> 32;
                }
                r[na + 1] = (uint)carry;
                return Make(sa, r, 1, na + 1);
            }
            int c = CompareMagnitudes(a, b);
            if (c == 0) return null;
            if (c < 0) { uint[] t = a; a = b; b = t; int tn = na; na = nb; nb = tn; sa = sb; }
            uint[] d = new uint[na + 1];
            long borrow = 0;
            for (int i = 1; i <= na; i++)
            {
                long t = (long)a[i] - (i <= nb ? b[i] : 0U) - borrow;
                borrow = t < 0 ? 1 : 0;
                d[i] = (uint)t;
            }
            return Make(sa, d, 1, na);
        }

        internal static uint[] Add(uint[] a, uint[] b) => AddSigned(a, b, false);
        internal static uint[] Subtract(uint[] a, uint[] b) => AddSigned(a, b, true);

        internal static uint[] Negate(uint[] a)
        {
            if (a == null) return null;
            uint[] r = Copy(a);
            r[0] = (uint)(-(int)a[0]);
            return r;
        }

        private static uint[] Copy(uint[] a)
        {
            uint[] r = new uint[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i];
            return r;
        }

        internal static uint[] Abs(uint[] a)
        {
            if (a == null || a[0] == 1) return a;
            uint[] r = Copy(a);
            r[0] = 1;
            return r;
        }

        internal static uint[] Multiply(uint[] a, uint[] b)
        {
            if (a == null || b == null) return null;
            int na = LengthOf(a), nb = LengthOf(b);
            uint[] r = new uint[na + nb + 1];
            for (int i = 1; i <= na; i++)
            {
                ulong carry = 0;
                for (int j = 1; j <= nb; j++)
                {
                    ulong t = (ulong)a[i] * b[j] + r[i + j - 1] + carry;
                    r[i + j - 1] = (uint)t;
                    carry = t >> 32;
                }
                r[i + nb] = (uint)carry;
            }
            return Make((int)a[0] * (int)b[0], r, 1, na + nb);
        }

        private static int LeadingZeros(uint x)
        {
            int n = 0;
            if (x == 0) return 32;
            while ((x & 0x80000000u) == 0) { x <<= 1; n++; }
            return n;
        }

        /// <summary>Knuth's algorithm D on magnitudes u (m limbs) / v (n limbs, nonzero): q (m limbs), rem (n limbs).</summary>
        private static void DivModMagnitudes(uint[] u, int m, uint[] v, int n, uint[] q, uint[] rem)
        {
            // u and v hold their limbs from index 1.
            if (n == 1)
            {
                ulong r = 0;
                for (int i = m; i >= 1; i--)
                {
                    ulong cur = (r << 32) | u[i];
                    q[i - 1] = (uint)(cur / v[1]);
                    r = cur % v[1];
                }
                rem[0] = (uint)r;
                return;
            }
            int s = LeadingZeros(v[n]);
            uint[] vn = new uint[n];
            uint[] un = new uint[m + 1];
            for (int i = n - 1; i > 0; i--) vn[i] = (v[i + 1] << s) | (s != 0 ? (uint)((ulong)v[i] >> (32 - s)) : 0U);
            vn[0] = v[1] << s;
            un[m] = s != 0 ? (uint)((ulong)u[m] >> (32 - s)) : 0U;
            for (int i = m - 1; i > 0; i--) un[i] = (u[i + 1] << s) | (s != 0 ? (uint)((ulong)u[i] >> (32 - s)) : 0U);
            un[0] = u[1] << s;
            for (int j = m - n; j >= 0; j--)
            {
                ulong num = ((ulong)un[j + n] << 32) | un[j + n - 1];
                ulong qhat = num / vn[n - 1], rhat = num % vn[n - 1];
                while (qhat >= (1UL << 32) || qhat * vn[n - 2] > ((rhat << 32) | un[j + n - 2]))
                {
                    qhat--;
                    rhat += vn[n - 1];
                    if (rhat >= (1UL << 32)) break;
                }
                long borrow = 0;
                ulong carry = 0;
                for (int i = 0; i < n; i++)
                {
                    ulong p = qhat * vn[i] + carry;
                    carry = p >> 32;
                    long t = (long)un[i + j] - (long)(uint)p - borrow;
                    un[i + j] = (uint)t;
                    borrow = t < 0 ? 1 : 0;
                }
                long top = (long)un[j + n] - (long)carry - borrow;
                un[j + n] = (uint)top;
                if (top < 0)
                {
                    qhat--;
                    ulong c = 0;
                    for (int i = 0; i < n; i++)
                    {
                        ulong sum = (ulong)un[i + j] + vn[i] + c;
                        un[i + j] = (uint)sum;
                        c = sum >> 32;
                    }
                    un[j + n] += (uint)c;
                }
                q[j] = (uint)qhat;
            }
            for (int i = 0; i < n - 1; i++) rem[i] = (un[i] >> s) | (s != 0 ? (uint)((ulong)un[i + 1] << (32 - s)) : 0U);
            rem[n - 1] = un[n - 1] >> s;
        }

        private static uint[] DivRemCore(uint[] a, uint[] b, out uint[] remainder)
        {
            if (b == null) throw new DivideByZeroException();
            if (a == null || CompareMagnitudes(a, b) < 0) { remainder = a; return null; }
            int na = LengthOf(a), nb = LengthOf(b);
            uint[] q = new uint[na];
            uint[] r = new uint[nb];
            DivModMagnitudes(a, na, b, nb, q, r);
            remainder = Make((int)a[0], r, 0, nb);  // the remainder takes the dividend's sign
            return Make((int)a[0] * (int)b[0], q, 0, na);
        }

        internal static uint[] Divide(uint[] a, uint[] b) => DivRemCore(a, b, out uint[] _);

        internal static uint[] Remainder(uint[] a, uint[] b)
        {
            DivRemCore(a, b, out uint[] r);
            return r;
        }

        internal static uint[] DivRem(uint[] a, uint[] b, ref uint[] remainder)
        {
            uint[] q = DivRemCore(a, b, out uint[] r);
            remainder = r;
            return q;
        }

        [RuntimeExport("csrt_bigint_compare", "csrt_Boxed_BigInteger_MT")]
        internal static int Compare(uint[] a, uint[] b)
        {
            int sa = SignOf(a), sb = SignOf(b);
            if (sa != sb) return sa < sb ? -1 : 1;
            if (sa == 0) return 0;
            int c = CompareMagnitudes(a, b);
            return sa > 0 ? c : -c;
        }

        internal static uint[] Pow(uint[] value, int exponent)
        {
            if (exponent < 0) throw new ArgumentOutOfRangeException("exponent", "The number must be greater than or equal to zero.");
            uint[] result = FromInt64(1);
            uint[] b = value;
            while (exponent != 0)
            {
                if ((exponent & 1) != 0) result = Multiply(result, b);
                exponent >>= 1;
                if (exponent != 0) b = Multiply(b, b);
            }
            return result;
        }

        internal static uint[] ModPow(uint[] value, uint[] exponent, uint[] modulus)
        {
            if (SignOf(exponent) < 0) throw new ArgumentOutOfRangeException("exponent", "The number must be greater than or equal to zero.");
            uint[] result = Remainder(FromInt64(1), modulus);
            uint[] b = Remainder(value, modulus);
            int bits = LengthOf(exponent) * 32;
            for (int i = 0; i < bits; i++)
            {
                if (((exponent[1 + i / 32] >> (i % 32)) & 1) != 0) result = Remainder(Multiply(result, b), modulus);
                b = Remainder(Multiply(b, b), modulus);
            }
            return result;
        }

        internal static uint[] GreatestCommonDivisor(uint[] a, uint[] b)
        {
            a = Abs(a);
            b = Abs(b);
            while (b != null) { uint[] t = Remainder(a, b); a = b; b = t; }
            return a;
        }

        /// <summary>Explicit conversion to an integer type: kind 7 long, 5 int, 8 ulong, 6 uint (overflow-checked).</summary>
        internal static long ToInteger(uint[] a, int kind)
        {
            if (a == null) return 0;
            int n = LengthOf(a);
            ulong magnitude = a[1] | (n > 1 ? (ulong)a[2] << 32 : 0UL);
            bool positive = a[0] == 1;
            bool ok = n <= 2;
            if (ok)
            {
                ok = kind switch
                {
                    5 => positive ? magnitude <= int.MaxValue : magnitude <= (ulong)int.MaxValue + 1,
                    6 => positive && magnitude <= uint.MaxValue,
                    8 => positive,
                    _ => positive ? magnitude <= long.MaxValue : magnitude <= (ulong)long.MaxValue + 1,
                };
            }
            if (!ok)
            {
                string name = kind == 5 ? "an Int32" : kind == 6 ? "a UInt32" : kind == 8 ? "a UInt64" : "an Int64";
                throw new OverflowException("Value was either too large or too small for " + name + ".");
            }
            return positive ? (long)magnitude : (long)(0UL - magnitude);
        }

        /// <summary>
        /// Explicit conversion to sbyte (1), byte (2), short (3), ushort (4) or char (9): any value outside the
        /// target's range is .NET 11's plain OverflowException ("Arithmetic operation resulted in an overflow."),
        /// however large it is.
        /// </summary>
        internal static long ToSmallInteger(uint[] a, int kind)
        {
            if (a != null && LengthOf(a) > 1) throw new OverflowException();
            long v = a == null ? 0 : a[0] == 1 ? a[1] : -(long)a[1];
            return kind switch
            {
                1 => checked((sbyte)v),
                2 => checked((byte)v),
                3 => checked((short)v),
                4 => checked((ushort)v),
                _ => checked((char)v),
            };
        }

        /// <summary>
        /// (decimal)BigInteger: at most 96 bits of magnitude. .NET 11 converts through Int128, so a value outside
        /// Int128's range overflows with Int128's message and one inside it but over 96 bits with Decimal's.
        /// </summary>
        internal static decimal ToDecimal(uint[] a)
        {
            int n = LengthOf(a);
            if (n == 0) return 0m;
            if (n > 3)
            {
                bool int128 = n == 4 && (a[4] < 0x80000000u
                    || (a[0] != 1 && a[4] == 0x80000000u && a[1] == 0 && a[2] == 0 && a[3] == 0)); // Int128.MinValue
                throw new OverflowException(int128 ? "Value was either too large or too small for a Decimal." : "Value was either too large or too small for an Int128.");
            }
            return new decimal((int)a[1], n > 1 ? (int)a[2] : 0, n > 2 ? (int)a[3] : 0, a[0] != 1, 0);
        }

        /// <summary>(BigInteger)decimal / new BigInteger(decimal): the integral part, truncated toward zero.</summary>
        internal static uint[] FromDecimal(decimal d)
        {
            int[] bits = decimal.GetBits(decimal.Truncate(d));
            return Make(d < 0 ? -1 : 1, new uint[] { (uint)bits[0], (uint)bits[1], (uint)bits[2] }, 0, 3);
        }

        internal static double ToDouble(uint[] a)
        {
            if (a == null) return 0;
            double r = 0;
            for (int i = LengthOf(a); i >= 1; i--) r = r * 4294967296.0 + a[i];
            return a[0] == 1 ? r : -r;
        }

        internal static bool IsEven(uint[] a) => a == null || (a[1] & 1) == 0;
        internal static int Sign(uint[] a) => SignOf(a);

        // ==========================================================================================
        // Text
        // ==========================================================================================

        /// <summary>Decimal digits of |a| (at least "0").</summary>
        private static string MagnitudeDigits(uint[] a)
        {
            int n = LengthOf(a);
            if (n == 0) return "0";
            if (n <= 2) return Number.UInt64ToDecStr(a[1] | (n > 1 ? (ulong)a[2] << 32 : 0UL));
            uint[] t = new uint[n];
            for (int i = 0; i < n; i++) t[i] = a[i + 1];
            char[] buf = new char[n * 10 + 1];
            int at = buf.Length;
            while (n > 0)
            {
                ulong r = 0;
                for (int i = n - 1; i >= 0; i--)
                {
                    ulong cur = (r << 32) | t[i];
                    t[i] = (uint)(cur / 1000000000u);
                    r = cur % 1000000000u;
                }
                while (n > 0 && t[n - 1] == 0) n--;
                for (int k = 0; k < 9; k++)
                {
                    buf[--at] = (char)('0' + (int)(r % 10));
                    r /= 10;
                    if (n == 0 && r == 0) break;
                }
            }
            string text = CoreIntrinsics.FastAllocateString(buf.Length - at);
            Span<char> dst = CoreIntrinsics.StringChars(text);
            for (int i = at; i < buf.Length; i++) dst[i - at] = buf[i];
            return text;
        }

        [RuntimeExport("csrt_bigint_to_string", "csrt_Boxed_BigInteger_MT")]
        internal static string ToDecimalString(uint[] a) =>
            SignOf(a) < 0 ? CsharpcCoreLib.Globalization.NumberFormatInfo.CurrentInfo.NegativeSign + MagnitudeDigits(a) : MagnitudeDigits(a);

        /// <summary>ToString(format): "D[n]", "N[n]" (n decimals of zeros), "G", "R", "X" (two's complement, minimal).</summary>
        internal static string Format(uint[] a, string format)
        {
            char f = format == null || format.Length == 0 ? 'G' : format[0];
            if (f == 'G' || f == 'g' || f == 'R' || f == 'r') return ToDecimalString(a);
            int precision = -1;
            if (format.Length > 1)
            {
                precision = 0;
                for (int i = 1; i < format.Length; i++)
                {
                    if (format[i] < '0' || format[i] > '9') throw new FormatException("Format specifier was invalid.");
                    precision = precision * 10 + (format[i] - '0');
                }
            }
            bool negative = SignOf(a) < 0;
            Span<char> initial = stackalloc char[64];
            var sb = new CharBuilder(initial);
            if (f == 'D' || f == 'd')
            {
                string digits = MagnitudeDigits(a);
                if (negative) sb.Append(CsharpcCoreLib.Globalization.NumberFormatInfo.CurrentInfo.NegativeSign);
                sb.Append('0', precision - digits.Length);
                sb.Append(digits);
            }
            else if (f == 'N' || f == 'n' || f == 'C' || f == 'c' || f == 'P' || f == 'p' || f == 'F' || f == 'f' || f == 'E' || f == 'e')
            {
                // The culture's number/currency/percent layouts over the exact digits (Number.Formatting.cs).
                return Number.FormatIntegerDigits(MagnitudeDigits(a), negative, f, precision, CsharpcCoreLib.Globalization.NumberFormatInfo.CurrentInfo);
            }
            else if (f == 'X' || f == 'x')
            {
                // Two's complement with one extra limb of sign extension, then the shortest prefix whose
                // top nibble still carries the sign: 128 is "080", -1 is "F", -980362 is "F10A76".
                char letter = f == 'X' ? 'A' : 'a';
                int len = LengthOf(a);
                uint[] words = new uint[len + 1];
                uint carry = 1;
                for (int i = 0; i < len; i++)
                {
                    uint w = a[i + 1];
                    if (negative) { w = ~w + carry; carry = w == 0 && carry == 1 ? 1U : 0U; }
                    words[i] = w;
                }
                words[len] = negative ? uint.MaxValue : 0U;
                int total = (len + 1) * 8;
                uint fill = negative ? 0xFU : 0U;
                int top = total - 1;
                while (top > 0 && Nibble(words, top) == fill && (Nibble(words, top - 1) >= 8) == negative) top--;
                char pad = negative ? (f == 'X' ? 'F' : 'f') : '0';
                sb.Append(pad, precision - (top + 1));
                for (int k = top; k >= 0; k--)
                {
                    int d = Nibble(words, k);
                    sb.Append(d < 10 ? (char)('0' + d) : (char)(letter + d - 10));
                }
            }
            else throw new FormatException("Format specifier was invalid.");
            return sb.Build();
        }

        /// <summary>Nibble k (counted from the least significant) of little-endian limbs.</summary>
        private static int Nibble(uint[] words, int k) => (int)((words[k / 8] >> ((k % 8) * 4)) & 0xF);

        private static bool TryParseCore(string s, out uint[] result)
        {
            result = null;
            if (s == null) return false;
            int i = 0, end = s.Length;
            while (i < end && (s[i] == ' ' || s[i] == '\t')) i++;
            int sign = 1;
            if (i < end && (s[i] == '-' || s[i] == '+')) { if (s[i] == '-') sign = -1; i++; }
            while (end > i && (s[end - 1] == ' ' || s[end - 1] == '\t')) end--;
            if (i >= end) return false;
            uint[] limbs = new uint[(end - i) / 9 + 2];
            int len = 0;
            for (; i < end; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
                ulong carry = (ulong)(c - '0');
                for (int k = 0; k < len; k++)
                {
                    ulong t = (ulong)limbs[k] * 10 + carry;
                    limbs[k] = (uint)t;
                    carry = t >> 32;
                }
                if (carry != 0) limbs[len++] = (uint)carry;
            }
            result = Make(sign, limbs, 0, len);
            return true;
        }

        internal static uint[] Parse(string s)
        {
            if (!TryParseCore(s, out uint[] r)) throw new FormatException("The value could not be parsed.");
            return r;
        }

        internal static bool TryParse(string s, ref uint[] result)
        {
            bool ok = TryParseCore(s, out uint[] r);
            result = r;
            return ok;
        }

        [RuntimeExport("csrt_bigint_get_hash_code", "csrt_Boxed_BigInteger_MT")]
        internal static int Hash(uint[] a)
        {
            uint h = (uint)SignOf(a);
            for (int i = 1; i <= LengthOf(a); i++) h = h * 31 + a[i];
            return (int)h;
        }
    }

    /// <summary>
    /// BigInteger.Parse(string, IFormatProvider) and ToString(string, IFormatProvider): csharpc's BigInteger
    /// parses and formats with the invariant culture's symbols; a provider whose number symbols are the same
    /// gives the same text, any other is an explicit NotSupportedException rather than invariant output.
    /// </summary>
    internal static class BigIntegerCulture
    {
        private static void CheckInvariantSymbols(System.IFormatProvider provider)
        {
            var nfi = System.Globalization.NumberFormatInfo.GetInstance(provider);
            int[] sizes = nfi.NumberGroupSizes;
            if (nfi.NegativeSign != "-" || nfi.PositiveSign != "+" || nfi.NumberDecimalSeparator != "." || nfi.NumberGroupSeparator != ","
                || sizes.Length != 1 || sizes[0] != 3)
                throw new System.NotSupportedException("BigInteger with a culture whose number symbols differ from the invariant culture's is not supported by csharpc yet.");
        }

        public static System.Numerics.BigInteger Parse(string value, System.IFormatProvider provider)
        {
            CheckInvariantSymbols(provider);
            return System.Numerics.BigInteger.Parse(value);
        }

        public static string Format(System.Numerics.BigInteger value, string format, System.IFormatProvider provider)
        {
            CheckInvariantSymbols(provider);
            return value.ToString(format);
        }
    }
}
