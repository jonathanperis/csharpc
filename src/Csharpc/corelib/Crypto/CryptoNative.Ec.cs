// The shim's ECDSA and ECDH over EVP_PKEY: pal_evp_pkey_ecdsa.c, pal_evp_pkey_ecdh.c and pal_evp_pkey_eckey.c (dotnet/runtime, .NET 11 GA),
// function by function. Key import and export are CryptoNative.EcImportExport.cs. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_derive_init(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_derive_set_peer(IntPtr ctx, IntPtr peer);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_derive(IntPtr ctx, byte* key, nuint* keylen);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_set1_EC_KEY(IntPtr pkey, IntPtr key);

        [RuntimeExport("CryptoNative_EcDsaSignHash", Trigger + "CryptoNative_EcDsaSignHash")]
        public static int EcDsaSignHash(IntPtr pkey, IntPtr extraHandle, byte* hash, int hashLen, byte* destination, int destinationLen)
        {
            ERR_clear_error();
            if (hashLen < 0 || destinationLen < 0) return -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            int ret = -1;
            nuint written = (nuint)destinationLen;
            if (ctx != IntPtr.Zero && EVP_PKEY_sign_init(ctx) > 0 && EVP_PKEY_sign(ctx, destination, &written, hash, (nuint)hashLen) > 0)
                ret = (int)written;
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_EcDsaVerifyHash", Trigger + "CryptoNative_EcDsaVerifyHash")]
        public static int EcDsaVerifyHash(IntPtr pkey, IntPtr extraHandle, byte* hash, int hashLen, byte* signature, int signatureLen)
        {
            ERR_clear_error();
            if (hashLen < 0 || signatureLen < 0) return -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            int ret = -1;
            if (ctx != IntPtr.Zero && EVP_PKEY_verify_init(ctx) > 0)
                ret = EVP_PKEY_verify(ctx, signature, (nuint)signatureLen, hash, (nuint)hashLen) == 1 ? 1 : 0;
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpPKeyDeriveSecretAgreement", Trigger + "CryptoNative_EvpPKeyDeriveSecretAgreement")]
        public static int EvpPKeyDeriveSecretAgreement(IntPtr pkey, IntPtr extraHandle, IntPtr peerKey, byte* secret, uint secretLength)
        {
            if (pkey == IntPtr.Zero || peerKey == IntPtr.Zero || secretLength == 0 || secret == null) return 0;
            ERR_clear_error();
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            if (ctx == IntPtr.Zero) return 0;
            if (EVP_PKEY_derive_init(ctx) != 1 || EVP_PKEY_derive_set_peer(ctx, peerKey) != 1)
            {
                EVP_PKEY_CTX_free(ctx);
                return 0;
            }
            // EVP_PKEY_derive writes at most tmpSize bytes.
            nuint tmpSize = secretLength;
            int ret = EVP_PKEY_derive(ctx, secret, &tmpSize);
            EVP_PKEY_CTX_free(ctx);
            return ret != 1 ? 0 : (int)tmpSize;
        }

        [RuntimeExport("CryptoNative_CreateEvpPkeyFromEcKey", Trigger + "CryptoNative_CreateEvpPkeyFromEcKey")]
        public static IntPtr CreateEvpPkeyFromEcKey(IntPtr ecKey, int* outKeySize)
        {
            ERR_clear_error();
            *outKeySize = 0;
            IntPtr pkey = EVP_PKEY_new();
            if (pkey == IntPtr.Zero) return IntPtr.Zero;
            if (EVP_PKEY_set1_EC_KEY(pkey, ecKey) == 0)
            {
                EVP_PKEY_free(pkey);
                return IntPtr.Zero;
            }
            int keySize = EvpPKeyGetEcKeySize(pkey);
            if (keySize == 0)
            {
                EVP_PKEY_free(pkey);
                return IntPtr.Zero;
            }
            *outKeySize = keySize;
            return pkey;
        }
    }
}
#endif
