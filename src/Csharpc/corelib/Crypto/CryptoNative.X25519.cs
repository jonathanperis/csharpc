// The shim's X25519 over EVP_PKEY: pal_evp_pkey_x25519.c (dotnet/runtime, .NET 11 GA), function by function. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_get_raw_private_key(IntPtr pkey, byte* priv, nuint* len);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_get_raw_public_key(IntPtr pkey, byte* pub, nuint* len);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_new_raw_private_key(int type, IntPtr e, byte* priv, nuint len);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_new_raw_public_key(int type, IntPtr e, byte* pub, nuint len);

        private const int EVP_PKEY_X25519 = 1034, X25519KeySize = 32;

        // The raw key into the caller's buffer, bounded by its length (OpenSSL fails rather than overflow it).
        private static int ExportRawKeyMaterial(IntPtr key, byte* destination, int destinationLength, bool privateKey)
        {
            ERR_clear_error();
            if (destinationLength < 0) return 0;
            nuint len = (nuint)destinationLength;
            int result = privateKey ? EVP_PKEY_get_raw_private_key(key, destination, &len) : EVP_PKEY_get_raw_public_key(key, destination, &len);
            if (result != 1) return 0;
            return len != (nuint)destinationLength ? -1 : 1;
        }

        [RuntimeExport("CryptoNative_X25519Available", Trigger + "CryptoNative_X25519Available")]
        public static int X25519Available()
        {
            ERR_clear_error();
            IntPtr ctx = EVP_PKEY_CTX_new_id(EVP_PKEY_X25519, IntPtr.Zero);
            if (ctx != IntPtr.Zero)
            {
                EVP_PKEY_CTX_free(ctx);
                return 1;
            }
            int result = ErrGetReason(ERR_peek_error()) == ERR_R_UNSUPPORTED ? 0 : 1;
            ERR_clear_error();
            return result;
        }

        [RuntimeExport("CryptoNative_X25519ExportPrivateKey", Trigger + "CryptoNative_X25519ExportPrivateKey")]
        public static int X25519ExportPrivateKey(IntPtr key, byte* destination, int destinationLength) => ExportRawKeyMaterial(key, destination, destinationLength, true);

        [RuntimeExport("CryptoNative_X25519ExportPublicKey", Trigger + "CryptoNative_X25519ExportPublicKey")]
        public static int X25519ExportPublicKey(IntPtr key, byte* destination, int destinationLength) => ExportRawKeyMaterial(key, destination, destinationLength, false);

        [RuntimeExport("CryptoNative_X25519GenerateKey", Trigger + "CryptoNative_X25519GenerateKey")]
        public static IntPtr X25519GenerateKey()
        {
            ERR_clear_error();
            IntPtr ctx = EVP_PKEY_CTX_new_id(EVP_PKEY_X25519, IntPtr.Zero);
            if (ctx == IntPtr.Zero) return IntPtr.Zero;
            IntPtr pkey = IntPtr.Zero, ret = IntPtr.Zero;
            if (EVP_PKEY_keygen_init(ctx) == 1 && EVP_PKEY_keygen(ctx, &pkey) == 1)
            {
                ret = pkey;
                pkey = IntPtr.Zero;
            }
            if (pkey != IntPtr.Zero) EVP_PKEY_free(pkey);
            EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_X25519ImportPrivateKey", Trigger + "CryptoNative_X25519ImportPrivateKey")]
        public static IntPtr X25519ImportPrivateKey(byte* source, int sourceLength)
        {
            ERR_clear_error();
            if (sourceLength < 0) return IntPtr.Zero;
            return EVP_PKEY_new_raw_private_key(EVP_PKEY_X25519, IntPtr.Zero, source, (nuint)sourceLength);
        }

        [RuntimeExport("CryptoNative_X25519ImportPublicKey", Trigger + "CryptoNative_X25519ImportPublicKey")]
        public static IntPtr X25519ImportPublicKey(byte* source, int sourceLength)
        {
            ERR_clear_error();
            if (sourceLength < 0) return IntPtr.Zero;
            return EVP_PKEY_new_raw_public_key(EVP_PKEY_X25519, IntPtr.Zero, source, (nuint)sourceLength);
        }

        [RuntimeExport("CryptoNative_X25519DeriveSecretAgreementWithBytes", Trigger + "CryptoNative_X25519DeriveSecretAgreementWithBytes")]
        public static int X25519DeriveSecretAgreementWithBytes(IntPtr pkey, IntPtr extraHandle, byte* peerKey, int peerKeyLength, byte* secret, uint secretLength)
        {
            if (pkey == IntPtr.Zero || peerKey == null || peerKeyLength <= 0 || secret == null || secretLength == 0) return 0;
            IntPtr peerPKey = X25519ImportPublicKey(peerKey, peerKeyLength);
            if (peerPKey == IntPtr.Zero) return 0;
            int ret = EvpPKeyDeriveSecretAgreement(pkey, extraHandle, peerPKey, secret, secretLength);
            EVP_PKEY_free(peerPKey);
            return ret;
        }

        [RuntimeExport("CryptoNative_X25519IsValidHandle", Trigger + "CryptoNative_X25519IsValidHandle")]
        public static int X25519IsValidHandle(IntPtr key, int* hasPrivateKey)
        {
            ERR_clear_error();
            *hasPrivateKey = 0;
            if (EVP_PKEY_get_base_id(key) != EVP_PKEY_X25519) return 0;
            byte* privateKey = stackalloc byte[X25519KeySize];
            nuint privateKeyLength = X25519KeySize;
            int ret;
            if (EVP_PKEY_get_raw_private_key(key, privateKey, &privateKeyLength) == 1)
            {
                ret = privateKeyLength == X25519KeySize ? 1 : 0;
                if (ret == 1) *hasPrivateKey = 1;
            }
            else
            {
                // A public key only.
                ret = 1;
                ERR_clear_error();
            }
            OPENSSL_cleanse(privateKey, X25519KeySize);
            return ret;
        }
    }
}
#endif
