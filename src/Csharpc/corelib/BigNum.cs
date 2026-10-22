// A small fixed-capacity arbitrary-precision unsigned integer over caller-provided (stackalloc'd)
// storage, for exact floating-point formatting and correctly rounded parsing (Number.Formatting.cs,
// Number.Parsing.cs). Little-endian 32-bit limbs; Len is the number of significant limbs.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal ref struct BigNum
    {
        public Span<uint> W;
        public int Len;

        public BigNum(Span<uint> storage) { W = storage; Len = 0; }

        public bool IsZero => Len == 0;

        public void SetUInt64(ulong value)
        {
            W[0] = (uint)value;
            W[1] = (uint)(value >> 32);
            Len = W[1] != 0 ? 2 : W[0] != 0 ? 1 : 0;
        }

        public void CopyFrom(ref BigNum other)
        {
            for (int i = 0; i < other.Len; i++) W[i] = other.W[i];
            Len = other.Len;
        }

        public int BitLength()
        {
            if (Len == 0) return 0;
            uint top = W[Len - 1];
            int bits = 0;
            while (top != 0) { bits++; top >>= 1; }
            return (Len - 1) * 32 + bits;
        }

        public void MulSmall(uint m)
        {
            ulong carry = 0;
            for (int i = 0; i < Len; i++)
            {
                ulong p = (ulong)W[i] * m + carry;
                W[i] = (uint)p;
                carry = p >> 32;
            }
            if (carry != 0) W[Len++] = (uint)carry;
        }

        public void AddSmall(uint a)
        {
            ulong carry = a;
            for (int i = 0; i < Len && carry != 0; i++)
            {
                ulong s = W[i] + carry;
                W[i] = (uint)s;
                carry = s >> 32;
            }
            if (carry != 0) W[Len++] = (uint)carry;
        }

        public void MulPow5(int n)
        {
            while (n >= 13) { MulSmall(1220703125); n -= 13; }  // 5^13
            uint m = 1;
            while (n-- > 0) m *= 5;
            if (m != 1) MulSmall(m);
        }

        public void MulPow10(int n)
        {
            MulPow5(n);
            ShiftLeft(n);
        }

        public void ShiftLeft(int bits)
        {
            if (Len == 0 || bits == 0) return;
            int words = bits >> 5, shift = bits & 31;
            if (shift == 0)
            {
                for (int i = Len - 1; i >= 0; i--) W[i + words] = W[i];
                Len += words;
            }
            else
            {
                W[Len + words] = 0;
                for (int i = Len - 1; i >= 0; i--)
                {
                    W[i + words + 1] |= W[i] >> (32 - shift);
                    W[i + words] = W[i] << shift;
                }
                Len += words + 1;
            }
            for (int i = 0; i < words; i++) W[i] = 0;
            Normalize();
        }

        public void ShiftRightOne()
        {
            for (int i = 0; i < Len; i++)
                W[i] = (W[i] >> 1) | (i + 1 < Len ? W[i + 1] << 31 : 0);
            Normalize();
        }

        /// <summary>this = this * 10 + digit.</summary>
        public void MulAdd10(uint digit)
        {
            ulong carry = digit;
            for (int i = 0; i < Len; i++)
            {
                ulong p = (ulong)W[i] * 10 + carry;
                W[i] = (uint)p;
                carry = p >> 32;
            }
            if (carry != 0) W[Len++] = (uint)carry;
        }

        /// <summary>Divides in place by a small divisor, returning the remainder.</summary>
        public uint DivRemSmall(uint divisor)
        {
            ulong rem = 0;
            for (int i = Len - 1; i >= 0; i--)
            {
                ulong cur = (rem << 32) | W[i];
                W[i] = (uint)(cur / divisor);
                rem = cur % divisor;
            }
            Normalize();
            return (uint)rem;
        }

        public void Normalize()
        {
            while (Len > 0 && W[Len - 1] == 0) Len--;
        }

        public static int Compare(ref BigNum a, ref BigNum b)
        {
            if (a.Len != b.Len) return a.Len < b.Len ? -1 : 1;
            for (int i = a.Len - 1; i >= 0; i--)
                if (a.W[i] != b.W[i]) return a.W[i] < b.W[i] ? -1 : 1;
            return 0;
        }

        /// <summary>Compares a + b with c.</summary>
        public static int CompareSum(ref BigNum a, ref BigNum b, ref BigNum c)
        {
            int n = a.Len > b.Len ? a.Len : b.Len;
            if (n + 1 < c.Len) return -1;
            if (n > c.Len) return 1;
            // Compute the sum limb by limb from the bottom into a running comparison.
            int result = 0;
            ulong carry = 0;
            int top = c.Len > n ? c.Len : n + 1;
            for (int i = 0; i < top; i++)
            {
                ulong s = (i < a.Len ? a.W[i] : 0UL) + (i < b.Len ? b.W[i] : 0UL) + carry;
                uint limb = (uint)s;
                carry = s >> 32;
                uint cl = i < c.Len ? c.W[i] : 0;
                if (limb != cl) result = limb < cl ? -1 : 1;
            }
            return result;
        }

        /// <summary>this -= other (requires this >= other).</summary>
        public void Subtract(ref BigNum other)
        {
            long borrow = 0;
            for (int i = 0; i < Len; i++)
            {
                long d = (long)W[i] - (i < other.Len ? other.W[i] : 0) - borrow;
                borrow = d < 0 ? 1 : 0;
                W[i] = (uint)d;
            }
            Normalize();
        }

        /// <summary>Small quotient (a decimal digit) of this / divisor; this becomes the remainder.</summary>
        public uint DivRemDigit(ref BigNum divisor)
        {
            uint q = 0;
            while (Compare(ref this, ref divisor) >= 0) { Subtract(ref divisor); q++; }
            return q;
        }
    }
}
