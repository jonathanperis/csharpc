// System.Decimal's bit-level members: the corelib keeps decimal's representation (the shadowed struct), so these read and build it.
// Parsing and formatting are the framework's (#426); these stayed with the representation when DecimalParsing went.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    [ShadowMembers("System.Decimal")]
    internal static class DecimalBits
    {
        // { lo, mid, hi, flags } of the 96-bit integer and the sign/scale word, as .NET's GetBits.
        public static int[] GetBits(decimal d)
        {
            ulong lo = CoreIntrinsics.DecimalLo(d);
            return new int[] { (int)lo, (int)(lo >> 32), (int)CoreIntrinsics.DecimalHi(d), CoreIntrinsics.DecimalFlags(d) };
        }

        public static int GetBits(decimal d, Span<int> destination)
        {
            if ((uint)destination.Length <= 3)
                throw new ArgumentException("Destination is too short.", "destination");
            ulong lo = CoreIntrinsics.DecimalLo(d);
            destination[0] = (int)lo;
            destination[1] = (int)(lo >> 32);
            destination[2] = (int)CoreIntrinsics.DecimalHi(d);
            destination[3] = CoreIntrinsics.DecimalFlags(d);
            return 4;
        }

        public static bool TryGetBits(decimal d, Span<int> destination, out int valuesWritten)
        {
            if ((uint)destination.Length <= 3)
            {
                valuesWritten = 0;
                return false;
            }
            valuesWritten = GetBits(d, destination);
            return true;
        }

        /// <summary>new decimal(int[]) / new decimal(ReadOnlySpan&lt;int&gt;) (BclValueTypes constructors call these).</summary>
        internal static decimal FromBits(int[] bits)
        {
            if (bits == null) throw new ArgumentNullException("bits");
            return FromBitsSpan(bits);
        }

        internal static decimal FromBitsSpan(ReadOnlySpan<int> bits)
        {
            if (bits.Length == 4)
            {
                int flags = bits[3];
                // Only the sign bit and a scale of 0..28 in bits 16-23 (.NET's IsValid).
                if ((flags & ~unchecked((int)0x80FF0000)) == 0 && (flags & 0x00FF0000) <= (28 << 16))
                    return CoreIntrinsics.MakeDecimal(flags, (uint)bits[2], (uint)bits[0] | ((ulong)(uint)bits[1] << 32));
            }
            throw new ArgumentException("Decimal constructor requires an array or span of four valid decimal bytes.");
        }
    }
}
