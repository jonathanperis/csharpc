// The shim's RSA over EVP_PKEY: pal_evp_pkey_rsa.c (dotnet/runtime, .NET 11 GA), function by function. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_set1_RSA(IntPtr pkey, IntPtr key);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_CTX_new_id(int id, IntPtr engine);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_keygen_init(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_keygen(IntPtr ctx, IntPtr* ppkey);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_set_rsa_keygen_bits(IntPtr ctx, int bits);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_set_rsa_padding(IntPtr ctx, int pad);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_set_rsa_oaep_md(IntPtr ctx, IntPtr md);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_set_rsa_pss_saltlen(IntPtr ctx, int len);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_set_signature_md(IntPtr ctx, IntPtr md);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_ctrl_str(IntPtr ctx, byte* type, byte* value);
        [DllImport(LibCrypto)] private static extern int ERR_set_mark();
        [DllImport(LibCrypto)] private static extern int ERR_pop_to_mark();
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_decrypt_init(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_decrypt(IntPtr ctx, byte* outBuf, nuint* outlen, byte* inBuf, nuint inlen);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_encrypt_init(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_encrypt(IntPtr ctx, byte* outBuf, nuint* outlen, byte* inBuf, nuint inlen);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_sign_init(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_sign(IntPtr ctx, byte* sig, nuint* siglen, byte* tbs, nuint tbslen);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_verify_init(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_verify(IntPtr ctx, byte* sig, nuint siglen, byte* tbs, nuint tbslen);

        private const int RsaPaddingPkcs1 = 0;

        [RuntimeExport("CryptoNative_EvpPKeyCreateRsa", Trigger + "CryptoNative_EvpPKeyCreateRsa")]
        public static IntPtr EvpPKeyCreateRsa(IntPtr currentKey)
        {
            ERR_clear_error();
            IntPtr pkey = EVP_PKEY_new();
            if (pkey == IntPtr.Zero) return IntPtr.Zero;
            if (EVP_PKEY_set1_RSA(pkey, currentKey) == 0)
            {
                EVP_PKEY_free(pkey);
                return IntPtr.Zero;
            }
            return pkey;
        }

        [RuntimeExport("CryptoNative_RsaGenerateKey", Trigger + "CryptoNative_RsaGenerateKey")]
        public static IntPtr RsaGenerateKey(int keySize)
        {
            ERR_clear_error();
            IntPtr ctx = EVP_PKEY_CTX_new_id(EVP_PKEY_RSA, IntPtr.Zero);
            if (ctx == IntPtr.Zero) return IntPtr.Zero;
            IntPtr pkey = IntPtr.Zero, ret = IntPtr.Zero;
            if (EVP_PKEY_keygen_init(ctx) == 1 && EVP_PKEY_CTX_set_rsa_keygen_bits(ctx, keySize) == 1 && EVP_PKEY_keygen(ctx, &pkey) == 1)
            {
                ret = pkey;
                pkey = IntPtr.Zero;
            }
            if (pkey != IntPtr.Zero) EVP_PKEY_free(pkey);
            EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        private static bool ConfigureEncryption(IntPtr ctx, int padding, IntPtr digest)
        {
            if (padding == RsaPaddingPkcs1)
            {
                if (EVP_PKEY_CTX_set_rsa_padding(ctx, 1 /* RSA_PKCS1_PADDING */) <= 0) return false;
                // OpenSSL 3.2's implicit rejection returns random bytes instead of failing; .NET turns it off where it exists and
                // ignores the error where it does not.
                ERR_set_mark();
                fixed (byte* name = "rsa_pkcs1_implicit_rejection\0"u8) fixed (byte* zero = "0\0"u8) EVP_PKEY_CTX_ctrl_str(ctx, name, zero);
                ERR_pop_to_mark();
                return true;
            }
            return EVP_PKEY_CTX_set_rsa_padding(ctx, 4 /* RSA_PKCS1_OAEP_PADDING */) > 0 && EVP_PKEY_CTX_set_rsa_oaep_md(ctx, digest) > 0;
        }

        [RuntimeExport("CryptoNative_RsaDecrypt", Trigger + "CryptoNative_RsaDecrypt")]
        public static int RsaDecrypt(IntPtr pkey, IntPtr extraHandle, byte* source, int sourceLen, int padding, IntPtr digest, byte* destination, int destinationLen)
        {
            ERR_clear_error();
            if (sourceLen < 0 || destinationLen < 0) return -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            int ret = -1;
            nuint written = (nuint)destinationLen;
            // EVP_PKEY_decrypt refuses to write more than `written`.
            if (ctx != IntPtr.Zero && EVP_PKEY_decrypt_init(ctx) > 0 && ConfigureEncryption(ctx, padding, digest)
                && EVP_PKEY_decrypt(ctx, destination, &written, source, (nuint)sourceLen) > 0)
                ret = (int)written;
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_RsaEncrypt", Trigger + "CryptoNative_RsaEncrypt")]
        public static int RsaEncrypt(IntPtr pkey, IntPtr extraHandle, byte* source, int sourceLen, int padding, IntPtr digest, byte* destination, int destinationLen)
        {
            ERR_clear_error();
            if (sourceLen < 0 || destinationLen < 0) return -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            int ret = -1;
            nuint written = (nuint)destinationLen;
            if (ctx != IntPtr.Zero && EVP_PKEY_encrypt_init(ctx) > 0 && ConfigureEncryption(ctx, padding, digest)
                && EVP_PKEY_encrypt(ctx, destination, &written, source, (nuint)sourceLen) > 0)
                ret = (int)written;
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        private static bool ConfigureSignature(IntPtr ctx, int padding, IntPtr digest)
        {
            if (EVP_PKEY_CTX_set_signature_md(ctx, digest) <= 0) return false;
            if (padding == RsaPaddingPkcs1) return EVP_PKEY_CTX_set_rsa_padding(ctx, 1 /* RSA_PKCS1_PADDING */) > 0;
            return EVP_PKEY_CTX_set_rsa_padding(ctx, 6 /* RSA_PKCS1_PSS_PADDING */) > 0
                && EVP_PKEY_CTX_set_rsa_pss_saltlen(ctx, -1 /* RSA_PSS_SALTLEN_DIGEST */) > 0;
        }

        [RuntimeExport("CryptoNative_RsaSignHash", Trigger + "CryptoNative_RsaSignHash")]
        public static int RsaSignHash(IntPtr pkey, IntPtr extraHandle, int padding, IntPtr digest, byte* hash, int hashLen, byte* destination, int destinationLen)
        {
            ERR_clear_error();
            if (hashLen < 0 || destinationLen < 0) return -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            int ret = -1;
            nuint written = (nuint)destinationLen;
            if (ctx != IntPtr.Zero && EVP_PKEY_sign_init(ctx) > 0 && ConfigureSignature(ctx, padding, digest)
                && EVP_PKEY_sign(ctx, destination, &written, hash, (nuint)hashLen) > 0)
                ret = (int)written;
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_RsaVerifyHash", Trigger + "CryptoNative_RsaVerifyHash")]
        public static int RsaVerifyHash(IntPtr pkey, IntPtr extraHandle, int padding, IntPtr digest, byte* hash, int hashLen, byte* signature, int signatureLen)
        {
            ERR_clear_error();
            if (hashLen < 0 || signatureLen < 0) return -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            int ret = -1;
            if (ctx != IntPtr.Zero && EVP_PKEY_verify_init(ctx) > 0 && ConfigureSignature(ctx, padding, digest))
                ret = hashLen != EVP_MD_get_size(digest) ? 0 : EVP_PKEY_verify(ctx, signature, (nuint)signatureLen, hash, (nuint)hashLen);
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }
    }
}
#endif
