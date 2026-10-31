// SHA-1 and SHA-256 (FIPS 180-4) behind .NET's HashAlgorithm / SHA1 / SHA256 / IncrementalHash API.
// .NET delegates to the OS's crypto library; the digests are defined bit for bit by the standard, so a
// managed implementation gives the same bytes. Roslyn hashes source text, file identities and PE
// content with them; HMAC over them is Crypto/Pbkdf2.cs. SHA-384/512 share one 64-bit engine; MD5 is RFC 1321. SHA-3 is not implemented and stays a
// compile-time "no implementation" diagnostic or NotSupportedException where the name is a value.
#nullable disable
using System;
using System.IO;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    /// <summary>The block-hash core shared by HashAlgorithm and IncrementalHash.</summary>
    internal abstract class HashEngine
    {
        public abstract int HashSizeInBytes { get; }
        public abstract void Reset();
        public abstract void Append(ReadOnlySpan<byte> data);
        public abstract byte[] Finish(); // the digest; leaves the engine reset

        public static HashEngine For(HashAlgorithmName name)
        {
            if (name.Name == null || name.Name.Length == 0)
                throw new ArgumentException(name.Name == null ? "The hash algorithm name cannot be null or empty." : "The value cannot be an empty string.", "hashAlgorithm");
            if (name.Name == "SHA1") return new Sha1Engine();
            if (name.Name == "SHA256") return new Sha256Engine();
            if (name.Name == "SHA384") return new Sha512Engine(is384: true);
            if (name.Name == "SHA512") return new Sha512Engine(is384: false);
            if (name.Name == "MD5") return new Md5Engine();
            if (name.Name.StartsWith("SHA3-", StringComparison.Ordinal))
                throw new NotSupportedException("The hash algorithm " + name.Name + " is not supported by csharpc yet.");
#if CSHARPC_TARGET_WINDOWS
            // .NET on Windows asks CNG, whose NTE_BAD_ALGID surfaces as this message.
            throw new CryptographicException("Unknown error (0xc1000225)");
#else
            throw new CryptographicException("'" + name.Name + "' is not a known hash algorithm.");
#endif
        }
    }

    /// <summary>Merkle–Damgård framing (64-byte blocks, big-endian bit length) for SHA-1 and SHA-256.</summary>
    internal abstract class BlockHashEngine : HashEngine
    {
        private readonly byte[] _block = new byte[64];
        private int _filled;
        private long _length;

        protected abstract void Compress(byte[] block);
        protected abstract void InitState();
        protected abstract byte[] StateBytes();

        public override void Reset() { _filled = 0; _length = 0; Array.Clear(_block, 0, 64); InitState(); }

        public override void Append(ReadOnlySpan<byte> data)
        {
            _length += data.Length;
            int i = 0;
            while (i < data.Length)
            {
                int n = Math.Min(64 - _filled, data.Length - i);
                data.Slice(i, n).CopyTo(new Span<byte>(_block, _filled, n));
                _filled += n;
                i += n;
                if (_filled == 64) { Compress(_block); _filled = 0; }
            }
        }

        public override byte[] Finish()
        {
            long bits = _length * 8;
            _block[_filled++] = 0x80;
            if (_filled > 56)
            {
                while (_filled < 64) _block[_filled++] = 0;
                Compress(_block);
                _filled = 0;
            }
            while (_filled < 56) _block[_filled++] = 0;
            for (int i = 0; i < 8; i++) _block[56 + i] = (byte)(bits >> (56 - 8 * i));
            Compress(_block);
            byte[] digest = StateBytes();
            Reset();
            return digest;
        }

        protected static uint BigEndian(byte[] b, int i) => (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);
        protected static uint Rotl(uint x, int n) => (x << n) | (x >> (32 - n));
        protected static uint Rotr(uint x, int n) => (x >> n) | (x << (32 - n));

        protected static byte[] ToBytes(uint[] words, int count)
        {
            byte[] result = new byte[count * 4];
            for (int i = 0; i < count; i++)
            {
                result[4 * i] = (byte)(words[i] >> 24);
                result[4 * i + 1] = (byte)(words[i] >> 16);
                result[4 * i + 2] = (byte)(words[i] >> 8);
                result[4 * i + 3] = (byte)words[i];
            }
            return result;
        }
    }

    internal sealed class Sha1Engine : BlockHashEngine
    {
        private readonly uint[] _h = new uint[5];
        private readonly uint[] _w = new uint[80];

        public Sha1Engine() { InitState(); }
        public override int HashSizeInBytes => 20;

        protected override void InitState()
        {
            _h[0] = 0x67452301; _h[1] = 0xEFCDAB89; _h[2] = 0x98BADCFE; _h[3] = 0x10325476; _h[4] = 0xC3D2E1F0;
        }

        protected override void Compress(byte[] block)
        {
            uint[] w = _w;
            for (int i = 0; i < 16; i++) w[i] = BigEndian(block, 4 * i);
            for (int i = 16; i < 80; i++) w[i] = Rotl(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
            uint a = _h[0], b = _h[1], c = _h[2], d = _h[3], e = _h[4];
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint t = Rotl(a, 5) + f + e + k + w[i];
                e = d; d = c; c = Rotl(b, 30); b = a; a = t;
            }
            _h[0] += a; _h[1] += b; _h[2] += c; _h[3] += d; _h[4] += e;
        }

        protected override byte[] StateBytes() => ToBytes(_h, 5);
    }

    internal sealed class Sha256Engine : BlockHashEngine
    {
        private static uint[] s_k;
        private static uint[] K => s_k ??= new uint[]
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
        };

        private readonly uint[] _h = new uint[8];
        private readonly uint[] _w = new uint[64];

        public Sha256Engine() { InitState(); }
        public override int HashSizeInBytes => 32;

        protected override void InitState()
        {
            _h[0] = 0x6a09e667; _h[1] = 0xbb67ae85; _h[2] = 0x3c6ef372; _h[3] = 0xa54ff53a;
            _h[4] = 0x510e527f; _h[5] = 0x9b05688c; _h[6] = 0x1f83d9ab; _h[7] = 0x5be0cd19;
        }

        protected override void Compress(byte[] block)
        {
            uint[] w = _w;
            uint[] k = K;
            for (int i = 0; i < 16; i++) w[i] = BigEndian(block, 4 * i);
            for (int i = 16; i < 64; i++)
            {
                uint s0 = Rotr(w[i - 15], 7) ^ Rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = Rotr(w[i - 2], 17) ^ Rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            uint a = _h[0], b = _h[1], c = _h[2], d = _h[3], e = _h[4], f = _h[5], g = _h[6], h = _h[7];
            for (int i = 0; i < 64; i++)
            {
                uint t1 = h + (Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25)) + ((e & f) ^ (~e & g)) + k[i] + w[i];
                uint t2 = (Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
                h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            _h[0] += a; _h[1] += b; _h[2] += c; _h[3] += d; _h[4] += e; _h[5] += f; _h[6] += g; _h[7] += h;
        }

        protected override byte[] StateBytes() => ToBytes(_h, 8);
    }

    /// <summary>MD5 (RFC 1321): SHA-1's framing, but little-endian words and bit length.</summary>
    internal sealed class Md5Engine : HashEngine
    {
        private static readonly int[] s_shift =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };
        private static uint[] s_k;
        private static uint[] K
        {
            get
            {
                if (s_k != null) return s_k;
                // K[i] = floor(|sin(i + 1)| * 2^32), as RFC 1321 tabulates them.
                var k = new uint[]
                {
                    0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
                    0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
                    0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
                    0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
                    0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
                    0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
                    0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
                    0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
                };
                return s_k = k;
            }
        }

        private readonly uint[] _h = new uint[4];
        private readonly byte[] _block = new byte[64];
        private int _filled;
        private long _length;

        public Md5Engine() { Reset(); }
        public override int HashSizeInBytes => 16;

        public override void Reset()
        {
            _filled = 0; _length = 0; Array.Clear(_block, 0, 64);
            _h[0] = 0x67452301; _h[1] = 0xefcdab89; _h[2] = 0x98badcfe; _h[3] = 0x10325476;
        }

        public override void Append(ReadOnlySpan<byte> data)
        {
            _length += data.Length;
            int i = 0;
            while (i < data.Length)
            {
                int n = Math.Min(64 - _filled, data.Length - i);
                data.Slice(i, n).CopyTo(new Span<byte>(_block, _filled, n));
                _filled += n;
                i += n;
                if (_filled == 64) { Compress(); _filled = 0; }
            }
        }

        public override byte[] Finish()
        {
            long bits = _length * 8;
            _block[_filled++] = 0x80;
            if (_filled > 56)
            {
                while (_filled < 64) _block[_filled++] = 0;
                Compress();
                _filled = 0;
            }
            while (_filled < 56) _block[_filled++] = 0;
            for (int i = 0; i < 8; i++) _block[56 + i] = (byte)(bits >> (8 * i));
            Compress();
            byte[] digest = new byte[16];
            for (int w = 0; w < 4; w++)
                for (int b = 0; b < 4; b++) digest[4 * w + b] = (byte)(_h[w] >> (8 * b));
            Reset();
            return digest;
        }

        private void Compress()
        {
            uint[] k = K;
            uint a = _h[0], b = _h[1], c = _h[2], d = _h[3];
            for (int i = 0; i < 64; i++)
            {
                uint f;
                int g;
                if (i < 16) { f = (b & c) | (~b & d); g = i; }
                else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) & 15; }
                else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) & 15; }
                else { f = c ^ (b | ~d); g = (7 * i) & 15; }
                uint m = (uint)(_block[4 * g] | _block[4 * g + 1] << 8 | _block[4 * g + 2] << 16 | _block[4 * g + 3] << 24);
                uint sum = a + f + k[i] + m;
                a = d; d = c; c = b;
                b += (sum << s_shift[i]) | (sum >> (32 - s_shift[i]));
            }
            _h[0] += a; _h[1] += b; _h[2] += c; _h[3] += d;
        }
    }

    /// <summary>SHA-384 and SHA-512: 64-bit words, 128-byte blocks, a 128-bit big-endian bit length.</summary>
    internal sealed class Sha512Engine : HashEngine
    {
        private static ulong[] s_k;
        private static ulong[] K => s_k ??= new ulong[]
        {
            0x428a2f98d728ae22, 0x7137449123ef65cd, 0xb5c0fbcfec4d3b2f, 0xe9b5dba58189dbbc, 0x3956c25bf348b538, 0x59f111f1b605d019,
            0x923f82a4af194f9b, 0xab1c5ed5da6d8118, 0xd807aa98a3030242, 0x12835b0145706fbe, 0x243185be4ee4b28c, 0x550c7dc3d5ffb4e2,
            0x72be5d74f27b896f, 0x80deb1fe3b1696b1, 0x9bdc06a725c71235, 0xc19bf174cf692694, 0xe49b69c19ef14ad2, 0xefbe4786384f25e3,
            0x0fc19dc68b8cd5b5, 0x240ca1cc77ac9c65, 0x2de92c6f592b0275, 0x4a7484aa6ea6e483, 0x5cb0a9dcbd41fbd4, 0x76f988da831153b5,
            0x983e5152ee66dfab, 0xa831c66d2db43210, 0xb00327c898fb213f, 0xbf597fc7beef0ee4, 0xc6e00bf33da88fc2, 0xd5a79147930aa725,
            0x06ca6351e003826f, 0x142929670a0e6e70, 0x27b70a8546d22ffc, 0x2e1b21385c26c926, 0x4d2c6dfc5ac42aed, 0x53380d139d95b3df,
            0x650a73548baf63de, 0x766a0abb3c77b2a8, 0x81c2c92e47edaee6, 0x92722c851482353b, 0xa2bfe8a14cf10364, 0xa81a664bbc423001,
            0xc24b8b70d0f89791, 0xc76c51a30654be30, 0xd192e819d6ef5218, 0xd69906245565a910, 0xf40e35855771202a, 0x106aa07032bbd1b8,
            0x19a4c116b8d2d0c8, 0x1e376c085141ab53, 0x2748774cdf8eeb99, 0x34b0bcb5e19b48a8, 0x391c0cb3c5c95a63, 0x4ed8aa4ae3418acb,
            0x5b9cca4f7763e373, 0x682e6ff3d6b2b8a3, 0x748f82ee5defb2fc, 0x78a5636f43172f60, 0x84c87814a1f0ab72, 0x8cc702081a6439ec,
            0x90befffa23631e28, 0xa4506cebde82bde9, 0xbef9a3f7b2c67915, 0xc67178f2e372532b, 0xca273eceea26619c, 0xd186b8c721c0c207,
            0xeada7dd6cde0eb1e, 0xf57d4f7fee6ed178, 0x06f067aa72176fba, 0x0a637dc5a2c898a6, 0x113f9804bef90dae, 0x1b710b35131c471b,
            0x28db77f523047d84, 0x32caab7b40c72493, 0x3c9ebe0a15c9bebc, 0x431d67c49c100d4c, 0x4cc5d4becb3e42b6, 0x597f299cfc657e2a,
            0x5fcb6fab3ad6faec, 0x6c44198c4a475817,
        };

        private readonly bool _is384;
        private readonly ulong[] _h = new ulong[8];
        private readonly ulong[] _w = new ulong[80];
        private readonly byte[] _block = new byte[128];
        private int _filled;
        private long _length;

        public Sha512Engine(bool is384) { _is384 = is384; Reset(); }
        public override int HashSizeInBytes => _is384 ? 48 : 64;

        public override void Reset()
        {
            _filled = 0; _length = 0; Array.Clear(_block, 0, 128);
            if (_is384)
            {
                _h[0] = 0xcbbb9d5dc1059ed8; _h[1] = 0x629a292a367cd507; _h[2] = 0x9159015a3070dd17; _h[3] = 0x152fecd8f70e5939;
                _h[4] = 0x67332667ffc00b31; _h[5] = 0x8eb44a8768581511; _h[6] = 0xdb0c2e0d64f98fa7; _h[7] = 0x47b5481dbefa4fa4;
            }
            else
            {
                _h[0] = 0x6a09e667f3bcc908; _h[1] = 0xbb67ae8584caa73b; _h[2] = 0x3c6ef372fe94f82b; _h[3] = 0xa54ff53a5f1d36f1;
                _h[4] = 0x510e527fade682d1; _h[5] = 0x9b05688c2b3e6c1f; _h[6] = 0x1f83d9abfb41bd6b; _h[7] = 0x5be0cd19137e2179;
            }
        }

        public override void Append(ReadOnlySpan<byte> data)
        {
            _length += data.Length;
            int i = 0;
            while (i < data.Length)
            {
                int n = Math.Min(128 - _filled, data.Length - i);
                data.Slice(i, n).CopyTo(new Span<byte>(_block, _filled, n));
                _filled += n;
                i += n;
                if (_filled == 128) { Compress(); _filled = 0; }
            }
        }

        public override byte[] Finish()
        {
            long bits = _length * 8; // the high 64 bits of the 128-bit length stay 0 below 2^61 bytes
            _block[_filled++] = 0x80;
            if (_filled > 112)
            {
                while (_filled < 128) _block[_filled++] = 0;
                Compress();
                _filled = 0;
            }
            while (_filled < 120) _block[_filled++] = 0;
            for (int i = 0; i < 8; i++) _block[120 + i] = (byte)(bits >> (56 - 8 * i));
            Compress();
            int words = _is384 ? 6 : 8;
            byte[] digest = new byte[words * 8];
            for (int w = 0; w < words; w++)
                for (int b = 0; b < 8; b++) digest[8 * w + b] = (byte)(_h[w] >> (56 - 8 * b));
            Reset();
            return digest;
        }

        private static ulong Rotr(ulong x, int n) => (x >> n) | (x << (64 - n));

        private void Compress()
        {
            ulong[] w = _w;
            ulong[] k = K;
            byte[] block = _block;
            for (int i = 0; i < 16; i++)
            {
                ulong v = 0;
                for (int b = 0; b < 8; b++) v = (v << 8) | block[8 * i + b];
                w[i] = v;
            }
            for (int i = 16; i < 80; i++)
            {
                ulong s0 = Rotr(w[i - 15], 1) ^ Rotr(w[i - 15], 8) ^ (w[i - 15] >> 7);
                ulong s1 = Rotr(w[i - 2], 19) ^ Rotr(w[i - 2], 61) ^ (w[i - 2] >> 6);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            ulong a = _h[0], bb = _h[1], c = _h[2], d = _h[3], e = _h[4], f = _h[5], g = _h[6], h = _h[7];
            for (int i = 0; i < 80; i++)
            {
                ulong t1 = h + (Rotr(e, 14) ^ Rotr(e, 18) ^ Rotr(e, 41)) + ((e & f) ^ (~e & g)) + k[i] + w[i];
                ulong t2 = (Rotr(a, 28) ^ Rotr(a, 34) ^ Rotr(a, 39)) + ((a & bb) ^ (a & c) ^ (bb & c));
                h = g; g = f; f = e; e = d + t1; d = c; c = bb; bb = a; a = t1 + t2;
            }
            _h[0] += a; _h[1] += bb; _h[2] += c; _h[3] += d; _h[4] += e; _h[5] += f; _h[6] += g; _h[7] += h;
        }
    }

    [Shadows("System.Security.Cryptography.HashAlgorithm")]
    public abstract class HashAlgorithmImpl : IDisposable
    {
        protected int HashSizeValue;
        protected internal byte[] HashValue;
        protected int State;
        private bool _disposed;

        protected HashAlgorithmImpl() { }

        public virtual int HashSize => HashSizeValue;

        public virtual byte[] Hash
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(null);
                if (State != 0) throw new CryptographicUnexpectedOperationException("Hash must be finalized before the hash value is retrieved.");
                return (byte[])HashValue?.Clone();
            }
        }

        public byte[] ComputeHash(byte[] buffer)
        {
            if (_disposed) throw new ObjectDisposedException(null);
            if (buffer == null) throw new ArgumentNullException("buffer");
            HashCore(buffer, 0, buffer.Length);
            return CaptureHashCodeAndReinitialize();
        }

        public byte[] ComputeHash(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
            if (count < 0 || count > buffer.Length) throw new ArgumentException("Value was invalid.");
            if (buffer.Length - count < offset) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            if (_disposed) throw new ObjectDisposedException(null);
            HashCore(buffer, offset, count);
            return CaptureHashCodeAndReinitialize();
        }

        public byte[] ComputeHash(Stream inputStream)
        {
            if (_disposed) throw new ObjectDisposedException(null);
            byte[] buffer = new byte[4096];
            int read;
            while ((read = inputStream.Read(buffer, 0, buffer.Length)) > 0) HashCore(buffer, 0, read);
            return CaptureHashCodeAndReinitialize();
        }

        private byte[] CaptureHashCodeAndReinitialize()
        {
            HashValue = HashFinal();
            byte[] result = (byte[])HashValue.Clone();
            Initialize();
            return result;
        }

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            if (inputBuffer == null) throw new ArgumentNullException("inputBuffer");
            State = 1;
            HashCore(inputBuffer, inputOffset, inputCount);
            if (outputBuffer != null && (inputBuffer != outputBuffer || inputOffset != outputOffset))
                Array.Copy(inputBuffer, inputOffset, outputBuffer, outputOffset, inputCount);
            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            if (inputBuffer == null) throw new ArgumentNullException("inputBuffer");
            HashCore(inputBuffer, inputOffset, inputCount);
            HashValue = CaptureHashCodeAndReinitialize();
            byte[] outputBytes = new byte[inputCount];
            Array.Copy(inputBuffer, inputOffset, outputBytes, 0, inputCount);
            State = 0;
            return outputBytes;
        }

        public void Dispose() { Dispose(true); }
        public void Clear() => Dispose();
        protected virtual void Dispose(bool disposing) { if (disposing) _disposed = true; }

        public abstract void Initialize();
        protected abstract void HashCore(byte[] array, int ibStart, int cbSize);
        protected abstract byte[] HashFinal();
    }

    [Shadows("System.Security.Cryptography.SHA1")]
    public abstract class SHA1Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 160;
        public const int HashSizeInBytes = 20;

        protected SHA1Impl() { HashSizeValue = HashSizeInBits; }

        public static SHA1 Create() => (SHA1)(object)new Sha1Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Sha1Engine();
            engine.Append(source);
            return engine.Finish();
        }
    }

    [Shadows("System.Security.Cryptography.SHA256")]
    public abstract class SHA256Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 256;
        public const int HashSizeInBytes = 32;

        protected SHA256Impl() { HashSizeValue = HashSizeInBits; }

        public static SHA256 Create() => (SHA256)(object)new Sha256Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Sha256Engine();
            engine.Append(source);
            return engine.Finish();
        }
    }

    [Shadows("System.Security.Cryptography.SHA384")]
    public abstract class SHA384Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 384;
        public const int HashSizeInBytes = 48;

        protected SHA384Impl() { HashSizeValue = HashSizeInBits; }

        public static SHA384 Create() => (SHA384)(object)new Sha384Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Sha512Engine(is384: true);
            engine.Append(source);
            return engine.Finish();
        }
    }

    [Shadows("System.Security.Cryptography.SHA512")]
    public abstract class SHA512Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 512;
        public const int HashSizeInBytes = 64;

        protected SHA512Impl() { HashSizeValue = HashSizeInBits; }

        public static SHA512 Create() => (SHA512)(object)new Sha512Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Sha512Engine(is384: false);
            engine.Append(source);
            return engine.Finish();
        }
    }

    [Shadows("System.Security.Cryptography.MD5")]
    public abstract class MD5Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 128;
        public const int HashSizeInBytes = 16;

        protected MD5Impl() { HashSizeValue = HashSizeInBits; }

        public static MD5 Create() => (MD5)(object)new Md5Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Md5Engine();
            engine.Append(source);
            return engine.Finish();
        }
    }

    internal sealed class Md5Algorithm : MD5
    {
        private readonly Md5Engine _engine = new Md5Engine();
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }

    internal sealed class Sha384Algorithm : SHA384
    {
        private readonly Sha512Engine _engine = new Sha512Engine(is384: true);
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }

    internal sealed class Sha512Algorithm : SHA512
    {
        private readonly Sha512Engine _engine = new Sha512Engine(is384: false);
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }

    internal sealed class Sha1Algorithm : SHA1
    {
        private readonly Sha1Engine _engine = new Sha1Engine();
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }

    internal sealed class Sha256Algorithm : SHA256
    {
        private readonly Sha256Engine _engine = new Sha256Engine();
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }

    [Shadows("System.Security.Cryptography.IncrementalHash")]
    public sealed class IncrementalHashImpl : IDisposable
    {
        private readonly HashAlgorithmName _name;
        private readonly HashEngine _engine;
        private bool _disposed;

        private IncrementalHashImpl(HashAlgorithmName name, HashEngine engine) { _name = name; _engine = engine; }

        public HashAlgorithmName AlgorithmName => _name;
        public int HashLengthInBytes => _engine.HashSizeInBytes;

        public static IncrementalHashImpl CreateHash(HashAlgorithmName hashAlgorithm) =>
            new IncrementalHashImpl(hashAlgorithm, HashEngine.For(hashAlgorithm));

        public static IncrementalHashImpl CreateHMAC(HashAlgorithmName hashAlgorithm, byte[] key)
        {
            if (key == null) throw new ArgumentNullException("key");
            return CreateHMAC(hashAlgorithm, new ReadOnlySpan<byte>(key));
        }

        public static IncrementalHashImpl CreateHMAC(HashAlgorithmName hashAlgorithm, ReadOnlySpan<byte> key) =>
            new IncrementalHashImpl(hashAlgorithm, new HmacHashEngine(new HmacEngine(hashAlgorithm, key)));

        /// <summary>HMAC (Crypto/Pbkdf2.cs) behind the HashEngine interface.</summary>
        private sealed class HmacHashEngine : HashEngine
        {
            private readonly HmacEngine _hmac;
            public HmacHashEngine(HmacEngine hmac) { _hmac = hmac; }
            public override int HashSizeInBytes => _hmac.HashSizeInBytes;
            public override void Reset() => _hmac.Finish();
            public override void Append(ReadOnlySpan<byte> data) => _hmac.Append(data);
            public override byte[] Finish() => _hmac.Finish();
        }

        public void AppendData(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            AppendData(new ReadOnlySpan<byte>(data));
        }

        public void AppendData(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
            if (count < 0 || count > data.Length) throw new ArgumentOutOfRangeException("count", "Specified argument was out of the range of valid values.");
            if (data.Length - count < offset) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            AppendData(new ReadOnlySpan<byte>(data, offset, count));
        }

        public void AppendData(ReadOnlySpan<byte> data)
        {
            if (_disposed) throw new ObjectDisposedException("IncrementalHash");
            _engine.Append(data);
        }

        public byte[] GetHashAndReset()
        {
            if (_disposed) throw new ObjectDisposedException("IncrementalHash");
            return _engine.Finish();
        }

        public int GetHashAndReset(Span<byte> destination)
        {
            if (_disposed) throw new ObjectDisposedException("IncrementalHash");
            if (destination.Length < _engine.HashSizeInBytes) throw new ArgumentException("Destination is too short.", "destination");
            byte[] hash = _engine.Finish();
            hash.AsSpan().CopyTo(destination);
            return hash.Length;
        }

        public bool TryGetHashAndReset(Span<byte> destination, out int bytesWritten)
        {
            if (_disposed) throw new ObjectDisposedException("IncrementalHash");
            if (destination.Length < _engine.HashSizeInBytes) { bytesWritten = 0; return false; }
            bytesWritten = GetHashAndReset(destination);
            return true;
        }

        public void Dispose() { _disposed = true; }
    }
}
