// Helpers for code ported from dotnet/runtime: upstream Debug.Assert/Fail are internal consistency
// checks, compiled out here (the corelib shouldn't pay for them, and users' System.Diagnostics.Debug
// is a different, observable API); plus small stand-ins for internals the ports call.
#nullable disable
using System;

namespace CsharpcCoreLib
{
    internal static unsafe class CoreDebug
    {
        [System.Diagnostics.Conditional("CSHARPC_COREDEBUG")] public static void Assert(bool condition) { }
        [System.Diagnostics.Conditional("CSHARPC_COREDEBUG")] public static void Assert(bool condition, string message) { }
        [System.Diagnostics.Conditional("CSHARPC_COREDEBUG")] public static void Fail(string message) { }

        /// <summary>Interop.GetRandomBytes: OS entropy (Posix.getentropy; Win32.cs provides it on Windows).</summary>
        public static void RandomBytes(byte* buffer, int count)
        {
            while (count > 0)
            {
                int n = count > 256 ? 256 : count;  // getentropy's limit
                Posix.getentropy(buffer, (nuint)n);
                buffer += n;
                count -= n;
            }
        }

        /// <summary>BitOperations.Log2Ceiling.</summary>
        public static int Log2Ceiling(ulong value)
        {
            int log = 0;
            ulong v = value;
            while (v > 1) { v >>= 1; log++; }
            return (value & (value - 1)) == 0 ? log : log + 1;
        }

        public static int Log2Ceiling(uint value) => Log2Ceiling((ulong)value);

        /// <summary>BitOperations.RoundUpToPowerOf2.</summary>
        public static uint RoundUpToPowerOf2(uint value)
        {
            --value;
            value |= value >> 1; value |= value >> 2; value |= value >> 4; value |= value >> 8; value |= value >> 16;
            return value + 1;
        }

        /// <summary>Math.BigMul(ulong, ulong, out ulong): the high 64 bits of the 128-bit product.</summary>
        public static ulong BigMul(ulong a, ulong b, out ulong low)
        {
            ulong aLo = (uint)a, aHi = a >> 32, bLo = (uint)b, bHi = b >> 32;
            ulong ll = aLo * bLo, lh = aLo * bHi, hl = aHi * bLo, hh = aHi * bHi;
            ulong mid = (ll >> 32) + (uint)lh + (uint)hl;
            low = (mid << 32) | (uint)ll;
            return hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }

        /// <summary>MemoryMarshal.Write(Span&lt;byte&gt;, ulong) (little-endian hosts).</summary>
        public static void WriteUInt64(Span<byte> destination, ulong value)
        {
            if (destination.Length < 8) throw new ArgumentOutOfRangeException("length");
            for (int i = 0; i < 8; i++) destination[i] = (byte)(value >> (8 * i));
        }
    }
}
