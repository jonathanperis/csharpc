// The shim's key encapsulation (ML-KEM) over EVP_PKEY: pal_evp_kem.c (dotnet/runtime, .NET 11 GA), function by function. ML-KEM itself
// is OpenSSL 3.5's: on an older libcrypto EvpKemAvailable answers 0 and MLKem.IsSupported is false, as under .NET. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern IntPtr EVP_KEM_fetch(IntPtr libctx, byte* algorithm, byte* properties);
        [DllImport(LibCrypto)] private static extern void EVP_KEM_free(IntPtr kem);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_encapsulate_init(IntPtr ctx, OsslParam* parameters);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_encapsulate(IntPtr ctx, byte* wrappedkey, nuint* wrappedkeylen, byte* genkey, nuint* genkeylen);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_decapsulate_init(IntPtr ctx, OsslParam* parameters);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_decapsulate(IntPtr ctx, byte* unwrapped, nuint* unwrappedlen, byte* wrapped, nuint wrappedlen);

        [RuntimeExport("CryptoNative_EvpKemAvailable", Trigger + "CryptoNative_EvpKemAvailable")]
        public static int EvpKemAvailable(byte* algorithm)
        {
            ERR_clear_error();
            IntPtr kem = EVP_KEM_fetch(IntPtr.Zero, algorithm, null);
            if (kem == IntPtr.Zero) return 0;
            EVP_KEM_free(kem);
            return 1;
        }

        [RuntimeExport("CryptoNative_EvpKemGetPalId", Trigger + "CryptoNative_EvpKemGetPalId")]
        public static int EvpKemGetPalId(IntPtr pKey, int* kemId, int* hasSeed, int* hasDecapsulationKey)
        {
            ERR_clear_error();
            if (PkeyIsA(pKey, "ML-KEM-512\0"u8)) *kemId = 1;
            else if (PkeyIsA(pKey, "ML-KEM-768\0"u8)) *kemId = 2;
            else if (PkeyIsA(pKey, "ML-KEM-1024\0"u8)) *kemId = 3;
            else
            {
                *kemId = 0;
                *hasSeed = 0;
                *hasDecapsulationKey = 0;
                return 1;
            }
            fixed (byte* seed = "seed\0"u8) *hasSeed = EvpPKeyHasKeyOctetStringParam(pKey, seed);
            fixed (byte* priv = "priv\0"u8) *hasDecapsulationKey = EvpPKeyHasKeyOctetStringParam(pKey, priv);
            return 1;
        }

        [RuntimeExport("CryptoNative_EvpKemGeneratePkey", Trigger + "CryptoNative_EvpKemGeneratePkey")]
        public static IntPtr EvpKemGeneratePkey(byte* kemName, byte* seed, int seedLength)
        {
            ERR_clear_error();
            IntPtr ctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, kemName, null);
            if (ctx == IntPtr.Zero) return IntPtr.Zero;
            IntPtr key = IntPtr.Zero;
            if (EVP_PKEY_keygen_init(ctx) == 1)
            {
                bool ok = true;
                if (seed != null && seedLength > 0)
                {
                    OsslParam* p = stackalloc OsslParam[2];
                    fixed (byte* n = "seed\0"u8)
                    {
                        p[0] = OctetParam(n, seed, (nuint)seedLength);
                        p[1] = EndParam();
                        ok = EVP_PKEY_CTX_set_params(ctx, p) == 1;
                    }
                }
                if (ok && EVP_PKEY_keygen(ctx, &key) != 1 && key != IntPtr.Zero)
                {
                    EVP_PKEY_free(key);
                    key = IntPtr.Zero;
                }
            }
            EVP_PKEY_CTX_free(ctx);
            return key;
        }

        [RuntimeExport("CryptoNative_EvpKemEncapsulate", Trigger + "CryptoNative_EvpKemEncapsulate")]
        public static int EvpKemEncapsulate(IntPtr pKey, IntPtr extraHandle, byte* ciphertext, int ciphertextLength, byte* sharedSecret, int sharedSecretLength)
        {
            ERR_clear_error();
            if (ciphertextLength < 0 || sharedSecretLength < 0) return 0;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pKey, extraHandle);
            int ret = 0;
            if (ctx != IntPtr.Zero && EVP_PKEY_encapsulate_init(ctx, null) == 1)
            {
                // OpenSSL checks both buffers against these lengths before writing.
                nuint ct = (nuint)ciphertextLength, ss = (nuint)sharedSecretLength;
                if (EVP_PKEY_encapsulate(ctx, ciphertext, &ct, sharedSecret, &ss) == 1)
                    ret = ct != (nuint)ciphertextLength || ss != (nuint)sharedSecretLength ? -1 : 1;
            }
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpKemDecapsulate", Trigger + "CryptoNative_EvpKemDecapsulate")]
        public static int EvpKemDecapsulate(IntPtr pKey, IntPtr extraHandle, byte* ciphertext, int ciphertextLength, byte* sharedSecret, int sharedSecretLength)
        {
            ERR_clear_error();
            if (ciphertextLength < 0 || sharedSecretLength < 0) return 0;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pKey, extraHandle);
            int ret = 0;
            if (ctx != IntPtr.Zero && EVP_PKEY_decapsulate_init(ctx, null) == 1)
            {
                nuint ss = (nuint)sharedSecretLength;
                if (EVP_PKEY_decapsulate(ctx, sharedSecret, &ss, ciphertext, (nuint)ciphertextLength) == 1)
                    ret = ss != (nuint)sharedSecretLength ? -1 : 1;
            }
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpKemExportPrivateSeed", Trigger + "CryptoNative_EvpKemExportPrivateSeed")]
        public static int EvpKemExportPrivateSeed(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "seed\0"u8, destination, destinationLength);

        [RuntimeExport("CryptoNative_EvpKemExportDecapsulationKey", Trigger + "CryptoNative_EvpKemExportDecapsulationKey")]
        public static int EvpKemExportDecapsulationKey(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "priv\0"u8, destination, destinationLength);

        [RuntimeExport("CryptoNative_EvpKemExportEncapsulationKey", Trigger + "CryptoNative_EvpKemExportEncapsulationKey")]
        public static int EvpKemExportEncapsulationKey(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "pub\0"u8, destination, destinationLength);
    }
}
#endif
