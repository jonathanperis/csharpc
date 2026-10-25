// SHA-1 and SHA-256 (FIPS 180-4) behind .NET's HashAlgorithm / SHA1 / SHA256 / IncrementalHash API.
// .NET delegates to the OS's crypto library; the digests are defined bit for bit by the standard, so a
// managed implementation gives the same bytes. Roslyn hashes source text, file identities and PE
// content with them; HMAC over them is Crypto/Pbkdf2.cs. Other algorithms (MD5, SHA-384/512, SHA-3) are not implemented and stay
// compile-time "no implementation" diagnostics or NotSupportedException where the name is a value.
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
            if (name.Name == "MD5" || name.Name == "SHA384" || name.Name == "SHA512" || name.Name.StartsWith("SHA3-", StringComparison.Ordinal))
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
            if (destination.Length < HashLengthInBytes) throw new ArgumentException("Destination is too short.", "destination");
            byte[] hash = _engine.Finish();
            hash.CopyTo(destination);
            return hash.Length;
        }

        public bool TryGetHashAndReset(Span<byte> destination, out int bytesWritten)
        {
            if (_disposed) throw new ObjectDisposedException("IncrementalHash");
            if (destination.Length < HashLengthInBytes)
            {
                bytesWritten = 0;
                return false;
            }
            bytesWritten = GetHashAndReset(destination);
            return true;
        }

        public void Dispose() { _disposed = true; }
    }
}
