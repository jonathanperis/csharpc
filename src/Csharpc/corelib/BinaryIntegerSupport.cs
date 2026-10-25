// IBinaryInteger<T>.ReadBigEndian / ReadLittleEndian for the primitive integers (generic code calls them as
// T.ReadBigEndian(...): FunctionCodeGen.Calls.cs): the bytes as a two's-complement number (unsigned when
// isUnsigned), an OverflowException when it doesn't fit the target, as in .NET.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static class BinaryIntegerSupport
    {
        public static ulong ReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, int size, bool signedTarget)
        {
            byte[] bigEndian = source.ToArray();
            return Read(bigEndian, isUnsigned, size, signedTarget);
        }

        public static ulong ReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, int size, bool signedTarget)
        {
            byte[] bigEndian = source.ToArray();
            Array.Reverse(bigEndian);
            return Read(bigEndian, isUnsigned, size, signedTarget);
        }

        private static ulong Read(byte[] bigEndian, bool isUnsigned, int size, bool signedTarget)
        {
            if (bigEndian.Length == 0) return 0;
            bool negative = !isUnsigned && (sbyte)bigEndian[0] < 0;
            byte fill = negative ? (byte)0xFF : (byte)0;
            // Leading bytes beyond 8 must only extend the sign.
            int start = 0;
            while (bigEndian.Length - start > 8)
            {
                if (bigEndian[start] != fill) throw new OverflowException();
                start++;
            }
            ulong bits = negative ? ulong.MaxValue : 0;
            for (int i = start; i < bigEndian.Length; i++) bits = (bits << 8) | bigEndian[i];
            int width = size * 8;
            // The value must be representable: within [min, max] of the target.
            if (negative)
            {
                if (!signedTarget) throw new OverflowException();
                long v = (long)bits;
                if (width < 64 && v < -(1L << (width - 1))) throw new OverflowException();
                // A shortened stream of 0xFF bytes past 8 positions also had to keep the sign.
                if (bigEndian.Length - start == 8 && (long)bits >= 0) throw new OverflowException();
            }
            else
            {
                ulong max = signedTarget ? (width == 64 ? long.MaxValue : (1UL << (width - 1)) - 1) : (width == 64 ? ulong.MaxValue : (1UL << width) - 1);
                if (bits > max) throw new OverflowException();
            }
            return bits;
        }
    }
}
