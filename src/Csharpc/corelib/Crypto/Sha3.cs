// SHA-3 (FIPS 202): SHA3-256, SHA3-384 and SHA3-512 over the Keccak-f[1600] permutation, behind the same
// HashEngine as the SHA-1/2 family (so IncrementalHash, HMAC and the RSA padding schemes get them too) and
// .NET's SHA3_256/SHA3_384/SHA3_512 classes. .NET asks the OS (OpenSSL 1.1.1+, CNG on Windows 11) and
// reports IsSupported = false where that has no SHA-3; the digests are fixed by the standard, so a
// managed implementation gives the same bytes everywhere and IsSupported is always true.
#nullable disable
using System;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    internal sealed class Sha3Engine : HashEngine
    {
        private static ulong[] s_roundConstants;
        private static ulong[] RoundConstants => s_roundConstants ??= new ulong[]
        {
            0x0000000000000001, 0x0000000000008082, 0x800000000000808A, 0x8000000080008000, 0x000000000000808B, 0x0000000080000001,
            0x8000000080008081, 0x8000000000008009, 0x000000000000008A, 0x0000000000000088, 0x0000000080008009, 0x000000008000000A,
            0x000000008000808B, 0x800000000000008B, 0x8000000000008089, 0x8000000000008003, 0x8000000000008002, 0x8000000000000080,
            0x000000000000800A, 0x800000008000000A, 0x8000000080008081, 0x8000000000008080, 0x0000000080000001, 0x8000000080008008,
        };

        // Rotation offsets and lane order of the combined rho and pi steps.
        private static readonly int[] RhoOffsets = { 1, 3, 6, 10, 15, 21, 28, 36, 45, 55, 2, 14, 27, 41, 56, 8, 25, 43, 62, 18, 39, 61, 20, 44 };
        private static readonly int[] PiLanes = { 10, 7, 11, 17, 18, 3, 5, 16, 8, 21, 24, 4, 15, 23, 19, 13, 12, 2, 20, 14, 22, 9, 6, 1 };

        private readonly int _digestBytes;
        private readonly int _rate;
        private readonly ulong[] _state = new ulong[25];
        private readonly ulong[] _scratch = new ulong[5];
        private readonly byte[] _block;
        private int _filled;

        public Sha3Engine(int digestBytes)
        {
            _digestBytes = digestBytes;
            _rate = 200 - 2 * digestBytes; // the capacity is twice the digest: 136, 104 and 72 bytes absorbed per permutation
            _block = new byte[_rate];
        }

        public override int HashSizeInBytes => _digestBytes;
        public override int BlockSizeInBytes => _rate;

        public override void Reset()
        {
            Array.Clear(_state, 0, 25);
            Array.Clear(_block, 0, _block.Length);
            _filled = 0;
        }

        public override void Append(ReadOnlySpan<byte> data)
        {
            int i = 0;
            while (i < data.Length)
            {
                int n = Math.Min(_rate - _filled, data.Length - i);
                data.Slice(i, n).CopyTo(new Span<byte>(_block, _filled, n));
                _filled += n;
                i += n;
                if (_filled == _rate) { Absorb(); _filled = 0; }
            }
        }

        public override byte[] Finish()
        {
            // pad10*1 with the SHA-3 domain separation bits 01: 0x06 after the message, 0x80 in the block's last byte.
            Array.Clear(_block, _filled, _rate - _filled);
            _block[_filled] ^= 0x06;
            _block[_rate - 1] ^= 0x80;
            Absorb();
            var digest = new byte[_digestBytes];
            for (int i = 0; i < _digestBytes; i++) digest[i] = (byte)(_state[i / 8] >> (8 * (i % 8)));
            Reset();
            return digest;
        }

        private void Absorb()
        {
            for (int lane = 0; lane < _rate / 8; lane++)
            {
                ulong value = 0;
                for (int b = 7; b >= 0; b--) value = (value << 8) | _block[lane * 8 + b];
                _state[lane] ^= value;
            }
            Permute();
        }

        private static ulong Rotl(ulong x, int n) => (x << n) | (x >> (64 - n));

        private void Permute()
        {
            ulong[] a = _state, c = _scratch, rc = RoundConstants;
            for (int round = 0; round < 24; round++)
            {
                // theta
                for (int x = 0; x < 5; x++) c[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];
                for (int x = 0; x < 5; x++)
                {
                    ulong d = c[(x + 4) % 5] ^ Rotl(c[(x + 1) % 5], 1);
                    for (int y = 0; y < 25; y += 5) a[y + x] ^= d;
                }
                // rho and pi
                ulong carry = a[1];
                for (int i = 0; i < 24; i++)
                {
                    int lane = PiLanes[i];
                    ulong next = a[lane];
                    a[lane] = Rotl(carry, RhoOffsets[i]);
                    carry = next;
                }
                // chi
                for (int y = 0; y < 25; y += 5)
                {
                    for (int x = 0; x < 5; x++) c[x] = a[y + x];
                    for (int x = 0; x < 5; x++) a[y + x] = c[x] ^ (~c[(x + 1) % 5] & c[(x + 2) % 5]);
                }
                // iota
                a[0] ^= rc[round];
            }
        }
    }

    [Shadows("System.Security.Cryptography.SHA3_256")]
    public abstract class SHA3_256Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 256;
        public const int HashSizeInBytes = 32;

        protected SHA3_256Impl() { HashSizeValue = HashSizeInBits; }

        public static bool IsSupported => true;
        public static SHA3_256 Create() => (SHA3_256)(object)new Sha3_256Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Sha3Engine(HashSizeInBytes);
            engine.Append(source);
            return engine.Finish();
        }

        public static int HashData(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (destination.Length < HashSizeInBytes) throw new ArgumentException("Destination is too short.", "destination");
            HashData(source).AsSpan().CopyTo(destination);
            return HashSizeInBytes;
        }

        public static bool TryHashData(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < HashSizeInBytes) { bytesWritten = 0; return false; }
            HashData(source).AsSpan().CopyTo(destination);
            bytesWritten = HashSizeInBytes;
            return true;
        }
    }

    [Shadows("System.Security.Cryptography.SHA3_384")]
    public abstract class SHA3_384Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 384;
        public const int HashSizeInBytes = 48;

        protected SHA3_384Impl() { HashSizeValue = HashSizeInBits; }

        public static bool IsSupported => true;
        public static SHA3_384 Create() => (SHA3_384)(object)new Sha3_384Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Sha3Engine(HashSizeInBytes);
            engine.Append(source);
            return engine.Finish();
        }

        public static int HashData(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (destination.Length < HashSizeInBytes) throw new ArgumentException("Destination is too short.", "destination");
            HashData(source).AsSpan().CopyTo(destination);
            return HashSizeInBytes;
        }

        public static bool TryHashData(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < HashSizeInBytes) { bytesWritten = 0; return false; }
            HashData(source).AsSpan().CopyTo(destination);
            bytesWritten = HashSizeInBytes;
            return true;
        }
    }

    [Shadows("System.Security.Cryptography.SHA3_512")]
    public abstract class SHA3_512Impl : HashAlgorithm
    {
        public const int HashSizeInBits = 512;
        public const int HashSizeInBytes = 64;

        protected SHA3_512Impl() { HashSizeValue = HashSizeInBits; }

        public static bool IsSupported => true;
        public static SHA3_512 Create() => (SHA3_512)(object)new Sha3_512Algorithm();

        public static byte[] HashData(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return HashData(new ReadOnlySpan<byte>(source));
        }

        public static byte[] HashData(ReadOnlySpan<byte> source)
        {
            var engine = new Sha3Engine(HashSizeInBytes);
            engine.Append(source);
            return engine.Finish();
        }

        public static int HashData(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (destination.Length < HashSizeInBytes) throw new ArgumentException("Destination is too short.", "destination");
            HashData(source).AsSpan().CopyTo(destination);
            return HashSizeInBytes;
        }

        public static bool TryHashData(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < HashSizeInBytes) { bytesWritten = 0; return false; }
            HashData(source).AsSpan().CopyTo(destination);
            bytesWritten = HashSizeInBytes;
            return true;
        }
    }

    internal sealed class Sha3_256Algorithm : SHA3_256
    {
        private readonly Sha3Engine _engine = new Sha3Engine(32);
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }

    internal sealed class Sha3_384Algorithm : SHA3_384
    {
        private readonly Sha3Engine _engine = new Sha3Engine(48);
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }

    internal sealed class Sha3_512Algorithm : SHA3_512
    {
        private readonly Sha3Engine _engine = new Sha3Engine(64);
        public override void Initialize() => _engine.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _engine.Append(new ReadOnlySpan<byte>(array, ibStart, cbSize));
        protected override byte[] HashFinal() => _engine.Finish();
    }
}
