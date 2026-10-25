// Aes.Create()'s implementation: the managed SymmetricAlgorithm/UniversalCryptoTransform machinery ported
// from .NET (SymmetricCrypto.cs: padding, the transforms, CryptoStream, the one-shot APIs) over the block
// cipher of the OS's crypto library, as .NET uses it — OpenSSL's libcrypto on Linux (EVP, loaded on first
// use like .NET's shim loads it), CommonCrypto on macOS (part of libSystem). The cipher always runs without
// padding (UniversalCryptoTransform pads), as in .NET's OpenSslCipher/AppleCCCryptor. Windows (CNG) and
// WASI have no implementation yet: Aes.Create() isn't there, a compile-time error.
#nullable disable
#if !CSHARPC_TARGET_WINDOWS && !CSHARPC_TARGET_WASI
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    internal sealed class AesImplementation : Aes
    {
        private const int BitsPerByte = 8;

        public override ICryptoTransform CreateDecryptor() => CreateTransform(Key, IV, encrypting: false);
        public override ICryptoTransform CreateDecryptor(byte[] rgbKey, byte[] rgbIV) => CreateTransform(rgbKey, rgbIV.CloneByteArray(), encrypting: false);
        public override ICryptoTransform CreateEncryptor() => CreateTransform(Key, IV, encrypting: true);
        public override ICryptoTransform CreateEncryptor(byte[] rgbKey, byte[] rgbIV) => CreateTransform(rgbKey, rgbIV.CloneByteArray(), encrypting: true);

        public override void GenerateIV() => IV = RandomNumberGenerator.GetBytes(BlockSize / BitsPerByte);

        public override void GenerateKey()
        {
            byte[] key = RandomNumberGenerator.GetBytes(KeySize / BitsPerByte);
            SetKeyCore(key);
            CryptographicOperations.ZeroMemory(key);
        }

        protected override bool TryDecryptEcbCore(ReadOnlySpan<byte> ciphertext, Span<byte> destination, PaddingMode paddingMode, out int bytesWritten)
        {
            using var cipher = CreateCipher(CipherMode.ECB, Key, default, BlockSize / BitsPerByte, BlockSize / BitsPerByte, 0, encrypting: false);
            return UniversalCryptoOneShot.OneShotDecrypt(cipher, paddingMode, ciphertext, destination, out bytesWritten);
        }

        protected override bool TryEncryptEcbCore(ReadOnlySpan<byte> plaintext, Span<byte> destination, PaddingMode paddingMode, out int bytesWritten)
        {
            using var cipher = CreateCipher(CipherMode.ECB, Key, default, BlockSize / BitsPerByte, BlockSize / BitsPerByte, 0, encrypting: true);
            return UniversalCryptoOneShot.OneShotEncrypt(cipher, paddingMode, plaintext, destination, out bytesWritten);
        }

        protected override bool TryEncryptCbcCore(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> iv, Span<byte> destination, PaddingMode paddingMode, out int bytesWritten)
        {
            using var cipher = CreateCipher(CipherMode.CBC, Key, iv, BlockSize / BitsPerByte, BlockSize / BitsPerByte, 0, encrypting: true);
            return UniversalCryptoOneShot.OneShotEncrypt(cipher, paddingMode, plaintext, destination, out bytesWritten);
        }

        protected override bool TryDecryptCbcCore(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> iv, Span<byte> destination, PaddingMode paddingMode, out int bytesWritten)
        {
            using var cipher = CreateCipher(CipherMode.CBC, Key, iv, BlockSize / BitsPerByte, BlockSize / BitsPerByte, 0, encrypting: false);
            return UniversalCryptoOneShot.OneShotDecrypt(cipher, paddingMode, ciphertext, destination, out bytesWritten);
        }

        protected override bool TryDecryptCfbCore(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> iv, Span<byte> destination, PaddingMode paddingMode, int feedbackSizeInBits, out int bytesWritten)
        {
            ValidateCFBFeedbackSize(feedbackSizeInBits);
            using var cipher = CreateCipher(CipherMode.CFB, Key, iv, BlockSize / BitsPerByte, feedbackSizeInBits / BitsPerByte, feedbackSizeInBits / BitsPerByte, encrypting: false);
            return UniversalCryptoOneShot.OneShotDecrypt(cipher, paddingMode, ciphertext, destination, out bytesWritten);
        }

        protected override bool TryEncryptCfbCore(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> iv, Span<byte> destination, PaddingMode paddingMode, int feedbackSizeInBits, out int bytesWritten)
        {
            ValidateCFBFeedbackSize(feedbackSizeInBits);
            using var cipher = CreateCipher(CipherMode.CFB, Key, iv, BlockSize / BitsPerByte, feedbackSizeInBits / BitsPerByte, feedbackSizeInBits / BitsPerByte, encrypting: true);
            return UniversalCryptoOneShot.OneShotEncrypt(cipher, paddingMode, plaintext, destination, out bytesWritten);
        }

        private UniversalCryptoTransform CreateTransform(byte[] rgbKey, byte[] rgbIV, bool encrypting)
        {
            ArgumentNullException.ThrowIfNull(rgbKey);
            long keySize = rgbKey.Length * (long)BitsPerByte;
            if (keySize > int.MaxValue || !((int)keySize).IsLegalSize(LegalKeySizes))
                throw new ArgumentException("Specified key is not a valid size for this algorithm.", nameof(rgbKey));
            if (rgbIV != null && rgbIV.Length * (long)BitsPerByte != BlockSize)
                throw new ArgumentException("Specified initialization vector (IV) does not match the block size for this algorithm.", nameof(rgbIV));
            if (Mode == CipherMode.CFB) ValidateCFBFeedbackSize(FeedbackSize);
            var mode = Mode;
            int paddingSize = (mode == CipherMode.CFB ? FeedbackSize : BlockSize) / BitsPerByte;
            var cipher = CreateCipher(mode, rgbKey, mode.UsesIv() ? GetCipherIv(mode, rgbIV) : null, BlockSize / BitsPerByte, paddingSize, FeedbackSize / BitsPerByte, encrypting);
            return UniversalCryptoTransform.Create(Padding, cipher, encrypting);
        }

        private static byte[] GetCipherIv(CipherMode mode, byte[] iv)
        {
            if (iv == null) throw new CryptographicException("The cipher mode specified requires that an initialization vector (IV) be used.");
            return iv;
        }

        private static void ValidateCFBFeedbackSize(int feedback)
        {
            if (feedback != 8 && feedback != 128)
                throw new CryptographicException(string.Format("The specified feedback size '{0}' for CipherMode '{1}' is not supported.", feedback, CipherMode.CFB));
        }

        private static NativeAesCipher CreateCipher(CipherMode mode, ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, int blockSize, int paddingSize, int feedback, bool encrypting)
        {
            if (mode is not (CipherMode.ECB or CipherMode.CBC or CipherMode.CFB))
                throw new CryptographicException("Specified cipher mode is not valid for this algorithm.");
            return new NativeAesCipher(mode, key, iv, blockSize, paddingSize, feedback, encrypting);
        }
    }

    /// <summary>One AES context of the OS library, without padding: the BasicSymmetricCipher of the transforms, the lite cipher of the one-shots.</summary>
    internal sealed unsafe class NativeAesCipher : BasicSymmetricCipher, ILiteSymmetricCipher
    {
        private IntPtr _ctx;
        private readonly bool _encrypting;

        public NativeAesCipher(CipherMode mode, ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, int blockSize, int paddingSize, int feedback, bool encrypting)
            : base(iv.IsEmpty ? null : iv.ToArray(), blockSize, paddingSize)
        {
            _encrypting = encrypting;
            fixed (byte* k = key) fixed (byte* v = iv)
                _ctx = Native.Create(mode, feedback * 8, k, key.Length, iv.IsEmpty ? null : v, encrypting);
        }

        public override int Transform(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (input.Length == 0) return 0;
            fixed (byte* i = input) fixed (byte* o = output) return Native.Update(_ctx, i, input.Length, o, output.Length);
        }

        public override int TransformFinal(ReadOnlySpan<byte> input, Span<byte> output)
        {
            int written = Transform(input, output);
            Reset(IV);
            return written;
        }

        public void Reset(ReadOnlySpan<byte> iv)
        {
            fixed (byte* v = iv) Native.Reset(_ctx, iv.IsEmpty ? null : v);
        }

        protected override void Dispose(bool disposing)
        {
            if (_ctx != IntPtr.Zero)
            {
                Native.Free(_ctx);
                _ctx = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }

        private static CryptographicException Error() => new CryptographicException("Error occurred during a cryptographic operation.");

#if CSHARPC_TARGET_MACOS
        // CommonCrypto (CommonCryptor.h): kCCEncrypt/kCCDecrypt, kCCModeECB 1, kCCModeCBC 2, kCCModeCFB 3, kCCModeCFB8 10,
        // kCCAlgorithmAES 0, ccNoPadding 0.
        private static class Native
        {
            [DllImport("libc")] private static extern int CCCryptorCreateWithMode(uint op, uint mode, uint alg, uint padding, byte* iv, byte* key, nuint keyLength,
                byte* tweak, nuint tweakLength, int numRounds, uint options, IntPtr* cryptorRef);
            [DllImport("libc")] private static extern int CCCryptorUpdate(IntPtr cryptorRef, byte* dataIn, nuint dataInLength, byte* dataOut, nuint dataOutAvailable, nuint* dataOutMoved);
            [DllImport("libc")] private static extern int CCCryptorReset(IntPtr cryptorRef, byte* iv);
            [DllImport("libc")] private static extern int CCCryptorRelease(IntPtr cryptorRef);

            public static IntPtr Create(CipherMode mode, int feedbackBits, byte* key, int keyLength, byte* iv, bool encrypting)
            {
                uint ccMode = mode switch { CipherMode.ECB => 1u, CipherMode.CBC => 2u, _ => feedbackBits == 8 ? 10u : 3u };
                IntPtr cryptor;
                if (CCCryptorCreateWithMode(encrypting ? 0u : 1u, ccMode, 0, 0, iv, key, (nuint)keyLength, null, 0, 0, 0, &cryptor) != 0) throw Error();
                return cryptor;
            }

            public static int Update(IntPtr ctx, byte* input, int inputLength, byte* output, int outputLength)
            {
                nuint moved;
                if (CCCryptorUpdate(ctx, input, (nuint)inputLength, output, (nuint)outputLength, &moved) != 0) throw Error();
                return (int)moved;
            }

            public static void Reset(IntPtr ctx, byte* iv)
            {
                if (CCCryptorReset(ctx, iv) != 0) throw Error();
            }

            public static void Free(IntPtr ctx) => CCCryptorRelease(ctx);
        }
#else
        // OpenSSL's EVP interface (libcrypto 3, as on the Linux distributions .NET 11 supports).
        private static class Native
        {
            private const string Lib = "libcrypto.so.3";
            [DllImport(Lib)] private static extern IntPtr EVP_CIPHER_CTX_new();
            [DllImport(Lib)] private static extern void EVP_CIPHER_CTX_free(IntPtr ctx);
            [DllImport(Lib)] private static extern int EVP_CipherInit_ex(IntPtr ctx, IntPtr type, IntPtr engine, byte* key, byte* iv, int enc);
            [DllImport(Lib)] private static extern int EVP_CIPHER_CTX_set_padding(IntPtr ctx, int padding);
            [DllImport(Lib)] private static extern int EVP_CipherUpdate(IntPtr ctx, byte* output, int* outputLength, byte* input, int inputLength);
            [DllImport(Lib)] private static extern IntPtr EVP_aes_128_ecb();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_192_ecb();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_256_ecb();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_128_cbc();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_192_cbc();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_256_cbc();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_128_cfb8();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_192_cfb8();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_256_cfb8();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_128_cfb128();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_192_cfb128();
            [DllImport(Lib)] private static extern IntPtr EVP_aes_256_cfb128();

            private static IntPtr Algorithm(CipherMode mode, int keyBits, int feedbackBits) => (mode, keyBits, feedbackBits) switch
            {
                (CipherMode.ECB, 128, _) => EVP_aes_128_ecb(),
                (CipherMode.ECB, 192, _) => EVP_aes_192_ecb(),
                (CipherMode.ECB, 256, _) => EVP_aes_256_ecb(),
                (CipherMode.CBC, 128, _) => EVP_aes_128_cbc(),
                (CipherMode.CBC, 192, _) => EVP_aes_192_cbc(),
                (CipherMode.CBC, 256, _) => EVP_aes_256_cbc(),
                (CipherMode.CFB, 128, 8) => EVP_aes_128_cfb8(),
                (CipherMode.CFB, 192, 8) => EVP_aes_192_cfb8(),
                (CipherMode.CFB, 256, 8) => EVP_aes_256_cfb8(),
                (CipherMode.CFB, 128, 128) => EVP_aes_128_cfb128(),
                (CipherMode.CFB, 192, 128) => EVP_aes_192_cfb128(),
                (CipherMode.CFB, 256, 128) => EVP_aes_256_cfb128(),
                _ => throw new NotSupportedException(),
            };

            public static IntPtr Create(CipherMode mode, int feedbackBits, byte* key, int keyLength, byte* iv, bool encrypting)
            {
                IntPtr ctx = EVP_CIPHER_CTX_new();
                if (ctx == IntPtr.Zero) throw Error();
                if (EVP_CipherInit_ex(ctx, Algorithm(mode, keyLength * 8, feedbackBits), IntPtr.Zero, key, iv, encrypting ? 1 : 0) != 1
                    || EVP_CIPHER_CTX_set_padding(ctx, 0) != 1)
                {
                    EVP_CIPHER_CTX_free(ctx);
                    throw Error();
                }
                return ctx;
            }

            public static int Update(IntPtr ctx, byte* input, int inputLength, byte* output, int outputLength)
            {
                int written;
                if (EVP_CipherUpdate(ctx, output, &written, input, inputLength) != 1) throw Error();
                return written;
            }

            // Same key and direction (enc = -1), the IV back to its start value.
            public static void Reset(IntPtr ctx, byte* iv)
            {
                if (EVP_CipherInit_ex(ctx, IntPtr.Zero, IntPtr.Zero, null, iv, -1) != 1) throw Error();
            }

            public static void Free(IntPtr ctx) => EVP_CIPHER_CTX_free(ctx);
        }
#endif
    }
}
#endif

namespace CsharpcCoreLib
{
    using System;
    using System.Security.Cryptography;

    internal static class CryptoHelpers
    {
        public static byte[] CloneByteArray(this byte[] src) => src == null ? null : (byte[])src.Clone();
        public static bool UsesIv(this CipherMode cipherMode) => cipherMode != CipherMode.ECB;

        /// <summary>Stands in for MemoryMarshal.TryGetArray: csharpc's Memory&lt;T&gt; has no array to hand out.</summary>
        public static bool NoArray<T>(ReadOnlyMemory<T> memory, out ArraySegment<T> segment)
        {
            segment = default;
            return false;
        }

        public static bool NoArray<T>(Memory<T> memory, out ArraySegment<T> segment)
        {
            segment = default;
            return false;
        }
    }

    /// <summary>CryptoPoolLease: a rented buffer of exactly the asked length, cleared when the lease ends.</summary>
    internal ref struct CryptoPoolLease : IDisposable
    {
        private byte[] _rented;
        private bool _skipClear;

        internal Span<byte> Span { get; private set; }
        internal readonly bool IsRented => _rented is not null;

        public void Dispose() => Return();

        internal void Return()
        {
            if (_rented is not null)
            {
                CryptoPool.Return(_rented, _skipClear ? 0 : Span.Length);
                _rented = null;
            }
            else if (!_skipClear) CryptographicOperations.ZeroMemory(Span);
            Span = default;
        }

        /// <summary>The caller's buffer when it is long enough, else a rented one.</summary>
        internal static CryptoPoolLease RentConditionally(int length, Span<byte> currentBuffer, bool skipClear = false, bool skipClearIfNotRented = false) =>
            RentConditionally(length, currentBuffer, out _, skipClear, skipClearIfNotRented);

        internal static CryptoPoolLease RentConditionally(int length, Span<byte> currentBuffer, out bool rented, bool skipClear = false, bool skipClearIfNotRented = false)
        {
            if (currentBuffer.Length >= length)
            {
                rented = false;
                return new CryptoPoolLease { _rented = null, _skipClear = skipClearIfNotRented || skipClear, Span = currentBuffer.Slice(0, length) };
            }
            rented = true;
            return Rent(length, skipClear);
        }

        internal static CryptoPoolLease Rent(int length, bool skipClear = false)
        {
            byte[] rented = CryptoPool.Rent(length);
            return new CryptoPoolLease { _rented = rented, _skipClear = skipClear, Span = new Span<byte>(rented, 0, length) };
        }
    }

    /// <summary>CryptoPool: buffers for transient sensitive data, cleared on return.</summary>
    internal static class CryptoPool
    {
        internal const int ClearAll = -1;

        internal static byte[] Rent(int minimumLength) => new byte[minimumLength];

        internal static void Return(byte[] array, int clearSize = ClearAll)
        {
            Array.Clear(array, 0, clearSize < 0 ? array.Length : clearSize);
        }

        internal static void Return(ArraySegment<byte> arraySegment)
        {
            if (arraySegment.Array != null) Array.Clear(arraySegment.Array, arraySegment.Offset, arraySegment.Count);
        }
    }
}
