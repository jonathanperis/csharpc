// HMAC (RFC 2104) and PBKDF2 (RFC 8018) over the corelib's SHA-1 / SHA-256 (Hashing.cs): the derived
// bytes are fixed by the standards, so they equal what .NET's OS-backed Rfc2898DeriveBytes.Pbkdf2
// returns. System.IO.Compression derives its WinZip AES keys with it.
#nullable disable
using System;
using System.Security.Cryptography;
using System.Text;

namespace CsharpcCoreLib
{
    internal sealed class HmacEngine
    {
        private readonly HashEngine _inner, _outer;
        private readonly byte[] _innerPad, _outerPad;

        public HmacEngine(HashAlgorithmName name, ReadOnlySpan<byte> key)
        {
            _inner = HashEngine.For(name);
            _outer = HashEngine.For(name);
            int BlockSize = _inner is Sha512Engine ? 128 : 64; // SHA-384/512 hash 128-byte blocks, SHA-1/256 64-byte ones
            byte[] k = new byte[BlockSize];
            if (key.Length > BlockSize)
            {
                _inner.Append(key);
                _inner.Finish().CopyTo(k, 0);
            }
            else key.CopyTo(k);
            _innerPad = new byte[BlockSize];
            _outerPad = new byte[BlockSize];
            for (int i = 0; i < BlockSize; i++)
            {
                _innerPad[i] = (byte)(k[i] ^ 0x36);
                _outerPad[i] = (byte)(k[i] ^ 0x5C);
            }
            Array.Clear(k, 0, k.Length);
            _inner.Append(_innerPad);
        }

        public int HashSizeInBytes => _inner.HashSizeInBytes;

        public void Append(ReadOnlySpan<byte> data) => _inner.Append(data);

        /// <summary>The MAC of everything appended; the engine is ready for the next message with the same key.</summary>
        public byte[] Finish()
        {
            byte[] innerHash = _inner.Finish();
            _outer.Append(_outerPad);
            _outer.Append(innerHash);
            byte[] mac = _outer.Finish();
            _inner.Append(_innerPad);
            return mac;
        }
    }

    [ShadowMembers("System.Security.Cryptography.Rfc2898DeriveBytes")]
    internal static class Rfc2898DeriveBytesMembers
    {
        public static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, HashAlgorithmName hashAlgorithm, int outputLength)
        {
            ArgumentNullException.ThrowIfNull(password);
            ArgumentNullException.ThrowIfNull(salt);
            return Pbkdf2(new ReadOnlySpan<byte>(password), new ReadOnlySpan<byte>(salt), iterations, hashAlgorithm, outputLength);
        }

        public static byte[] Pbkdf2(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations, HashAlgorithmName hashAlgorithm, int outputLength)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
            ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
            byte[] result = new byte[outputLength];
            Pbkdf2Core(password, salt, result, iterations, hashAlgorithm);
            return result;
        }

        public static void Pbkdf2(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Span<byte> destination, int iterations, HashAlgorithmName hashAlgorithm)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
            Pbkdf2Core(password, salt, destination, iterations, hashAlgorithm);
        }

        public static byte[] Pbkdf2(string password, byte[] salt, int iterations, HashAlgorithmName hashAlgorithm, int outputLength)
        {
            ArgumentNullException.ThrowIfNull(password);
            ArgumentNullException.ThrowIfNull(salt);
            return Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, hashAlgorithm, outputLength);
        }

        public static byte[] Pbkdf2(ReadOnlySpan<char> password, ReadOnlySpan<byte> salt, int iterations, HashAlgorithmName hashAlgorithm, int outputLength) =>
            Pbkdf2(new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(password.ToArray())), salt, iterations, hashAlgorithm, outputLength);

        public static void Pbkdf2(ReadOnlySpan<char> password, ReadOnlySpan<byte> salt, Span<byte> destination, int iterations, HashAlgorithmName hashAlgorithm) =>
            Pbkdf2(new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(password.ToArray())), salt, destination, iterations, hashAlgorithm);

        private static void Pbkdf2Core(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Span<byte> destination, int iterations, HashAlgorithmName hashAlgorithm)
        {
            if (destination.IsEmpty) return;
            var hmac = new HmacEngine(hashAlgorithm, password);
            int hashLength = hmac.HashSizeInBytes;
            byte[] counter = new byte[4];
            uint block = 1;
            for (int offset = 0; offset < destination.Length; offset += hashLength, block++)
            {
                counter[0] = (byte)(block >> 24); counter[1] = (byte)(block >> 16); counter[2] = (byte)(block >> 8); counter[3] = (byte)block;
                // T_i = U_1 xor ... xor U_c, U_1 = PRF(P, S || INT(i)), U_j = PRF(P, U_{j-1})
                hmac.Append(salt);
                hmac.Append(counter);
                byte[] u = hmac.Finish();
                byte[] t = (byte[])u.Clone();
                for (int j = 1; j < iterations; j++)
                {
                    hmac.Append(u);
                    u = hmac.Finish();
                    for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
                }
                int n = Math.Min(hashLength, destination.Length - offset);
                new ReadOnlySpan<byte>(t, 0, n).CopyTo(destination.Slice(offset));
            }
        }
    }

    /// <summary>The one-shot HMACSHA*.HashData of .NET 6+ (Npgsql's SCRAM-SHA-256 authentication), over HmacEngine.</summary>
    internal static class HmacOneShot
    {
        internal static byte[] Hash(HashAlgorithmName name, byte[] key, byte[] source)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(source);
            return Hash(name, new ReadOnlySpan<byte>(key), new ReadOnlySpan<byte>(source));
        }

        internal static byte[] Hash(HashAlgorithmName name, ReadOnlySpan<byte> key, ReadOnlySpan<byte> source)
        {
            var hmac = new HmacEngine(name, key);
            hmac.Append(source);
            return hmac.Finish();
        }

        internal static int Hash(HashAlgorithmName name, ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination)
        {
            byte[] mac = Hash(name, key, source);
            if (destination.Length < mac.Length) throw new ArgumentException("Destination is too short.", "destination");
            mac.CopyTo(destination);
            return mac.Length;
        }

        internal static bool TryHash(HashAlgorithmName name, ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
            byte[] mac = Hash(name, key, source);
            if (destination.Length < mac.Length) { bytesWritten = 0; return false; }
            mac.CopyTo(destination);
            bytesWritten = mac.Length;
            return true;
        }
    }

    [ShadowMembers("System.Security.Cryptography.HMACSHA256")]
    internal static class HMACSHA256Members
    {
        public static byte[] HashData(byte[] key, byte[] source) => HmacOneShot.Hash(HashAlgorithmName.SHA256, key, source);
        public static byte[] HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source) => HmacOneShot.Hash(HashAlgorithmName.SHA256, key, source);
        public static int HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination) => HmacOneShot.Hash(HashAlgorithmName.SHA256, key, source, destination);
        public static bool TryHashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) =>
            HmacOneShot.TryHash(HashAlgorithmName.SHA256, key, source, destination, out bytesWritten);
    }

    [ShadowMembers("System.Security.Cryptography.HMACSHA1")]
    internal static class HMACSHA1Members
    {
        public static byte[] HashData(byte[] key, byte[] source) => HmacOneShot.Hash(HashAlgorithmName.SHA1, key, source);
        public static byte[] HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source) => HmacOneShot.Hash(HashAlgorithmName.SHA1, key, source);
        public static int HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination) => HmacOneShot.Hash(HashAlgorithmName.SHA1, key, source, destination);
        public static bool TryHashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) =>
            HmacOneShot.TryHash(HashAlgorithmName.SHA1, key, source, destination, out bytesWritten);
    }

    [ShadowMembers("System.Security.Cryptography.HMACSHA384")]
    internal static class HMACSHA384Members
    {
        public static byte[] HashData(byte[] key, byte[] source) => HmacOneShot.Hash(HashAlgorithmName.SHA384, key, source);
        public static byte[] HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source) => HmacOneShot.Hash(HashAlgorithmName.SHA384, key, source);
        public static int HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination) => HmacOneShot.Hash(HashAlgorithmName.SHA384, key, source, destination);
        public static bool TryHashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) =>
            HmacOneShot.TryHash(HashAlgorithmName.SHA384, key, source, destination, out bytesWritten);
    }

    [ShadowMembers("System.Security.Cryptography.HMACSHA512")]
    internal static class HMACSHA512Members
    {
        public static byte[] HashData(byte[] key, byte[] source) => HmacOneShot.Hash(HashAlgorithmName.SHA512, key, source);
        public static byte[] HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source) => HmacOneShot.Hash(HashAlgorithmName.SHA512, key, source);
        public static int HashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination) => HmacOneShot.Hash(HashAlgorithmName.SHA512, key, source, destination);
        public static bool TryHashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) =>
            HmacOneShot.TryHash(HashAlgorithmName.SHA512, key, source, destination, out bytesWritten);
    }
}
