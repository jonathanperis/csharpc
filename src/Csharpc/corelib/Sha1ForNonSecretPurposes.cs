// SHA-1 for non-secret purposes (dotnet/runtime src/libraries/Common/src/System/Sha1ForNonSecretPurposes.cs, HashData): the public-key token of an
// AssemblyName (corelib/Reflection/AssemblyName.cs) is the last 8 bytes of the key's SHA-1, as .NET computes it without the crypto stack.
// It lived in the old EventSource shadow (deleted when tracing came from the framework's IL, #423), which used it for event source GUIDs.
#nullable disable
namespace CsharpcCoreLib
{
    internal static class Sha1ForNonSecretPurposes
    {
        public static byte[] HashData(byte[] source)
        {
            uint[] w = new uint[85];
            w[80] = 0x67452301;
            w[81] = 0xEFCDAB89;
            w[82] = 0x98BADCFE;
            w[83] = 0x10325476;
            w[84] = 0xC3D2E1F0;
            int full = source.Length / 64 * 64;
            for (int offset = 0; offset < full; offset += 64) Block(w, source, offset);
            byte[] tail = new byte[128];
            int pos = 0;
            for (int i = full; i < source.Length; i++) tail[pos++] = source[i];
            tail[pos++] = 0x80;
            while ((pos & 63) != 56) tail[pos++] = 0;
            ulong bits = (ulong)source.Length * 8;
            for (int i = 7; i >= 0; i--) tail[pos++] = (byte)(bits >> (8 * i));
            for (int offset = 0; offset < pos; offset += 64) Block(w, tail, offset);
            byte[] destination = new byte[20];
            for (int i = 0; i < 5; i++)
            {
                uint v = w[80 + i];
                destination[4 * i] = (byte)(v >> 24);
                destination[4 * i + 1] = (byte)(v >> 16);
                destination[4 * i + 2] = (byte)(v >> 8);
                destination[4 * i + 3] = (byte)v;
            }
            return destination;
        }

        private static uint Rol(uint value, int count) => (value << count) | (value >> (32 - count));

        private static void Block(uint[] w, byte[] data, int offset)
        {
            for (int i = 0; i < 16; i++)
            {
                int p = offset + 4 * i;
                w[i] = (uint)data[p] << 24 | (uint)data[p + 1] << 16 | (uint)data[p + 2] << 8 | data[p + 3];
            }
            for (int i = 16; i < 80; i++) w[i] = Rol(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
            uint a = w[80], b = w[81], c = w[82], d = w[83], e = w[84];
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint temp = Rol(a, 5) + f + e + k + w[i];
                e = d; d = c; c = Rol(b, 30); b = a; a = temp;
            }
            w[80] += a;
            w[81] += b;
            w[82] += c;
            w[83] += d;
            w[84] += e;
        }
    }
}
