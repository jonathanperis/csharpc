// The shim's digests, PBKDF2, HMAC, EVP_MAC (KMAC), symmetric ciphers and EVP_KDF (HKDF, SP 800-108) over libcrypto: pal_evp.c, pal_hmac.c,
// pal_evp_mac.c, pal_evp_cipher.c and pal_evp_kdf.c (dotnet/runtime, .NET 11 GA), function by function. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        // ---- libcrypto -------------------------------------------------------------------------------------------------
        [DllImport(LibCrypto)] private static extern IntPtr EVP_MD_CTX_new();
        [DllImport(LibCrypto)] private static extern void EVP_MD_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_DigestInit_ex(IntPtr ctx, IntPtr type, IntPtr engine);
        [DllImport(LibCrypto)] private static extern int EVP_DigestUpdate(IntPtr ctx, void* d, nuint cnt);
        [DllImport(LibCrypto)] private static extern int EVP_DigestFinal_ex(IntPtr ctx, byte* md, uint* s);
        [DllImport(LibCrypto)] private static extern int EVP_DigestFinalXOF(IntPtr ctx, byte* md, nuint len);
        [DllImport(LibCrypto)] private static extern int EVP_MD_CTX_copy_ex(IntPtr outCtx, IntPtr inCtx);
        [DllImport(LibCrypto)] private static extern int EVP_MD_get_size(IntPtr md);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_MD_fetch(IntPtr libctx, byte* algorithm, byte* properties);
        [DllImport(LibCrypto)] private static extern byte* EVP_MD_get0_name(IntPtr md);
        [DllImport(LibCrypto)] private static extern int EVP_MD_is_a(IntPtr md, byte* name);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_md5();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_sha1();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_sha256();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_sha384();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_sha512();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_sha3_256();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_sha3_384();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_sha3_512();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_shake128();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_shake256();
        [DllImport(LibCrypto)] private static extern int PKCS5_PBKDF2_HMAC(byte* pass, int passlen, byte* salt, int saltlen, int iter, IntPtr digest, int keylen, byte* outBuf);

        [DllImport(LibCrypto)] private static extern IntPtr EVP_MAC_fetch(IntPtr libctx, byte* algorithm, byte* properties);
        [DllImport(LibCrypto)] private static extern void EVP_MAC_free(IntPtr mac);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_MAC_CTX_new(IntPtr mac);
        [DllImport(LibCrypto)] private static extern void EVP_MAC_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_MAC_CTX_dup(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_MAC_init(IntPtr ctx, byte* key, nuint keylen, OsslParam* parameters);
        [DllImport(LibCrypto)] private static extern int EVP_MAC_update(IntPtr ctx, byte* data, nuint datalen);
        [DllImport(LibCrypto)] private static extern int EVP_MAC_final(IntPtr ctx, byte* outBuf, nuint* outl, nuint outsize);
        [DllImport(LibCrypto)] private static extern int EVP_MAC_CTX_set_params(IntPtr ctx, OsslParam* parameters);
        [DllImport(LibCrypto)] private static extern IntPtr HMAC_CTX_new();
        [DllImport(LibCrypto)] private static extern void HMAC_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int HMAC_Init_ex(IntPtr ctx, void* key, int len, IntPtr md, IntPtr engine);
        [DllImport(LibCrypto)] private static extern int HMAC_Update(IntPtr ctx, byte* data, nuint len);
        [DllImport(LibCrypto)] private static extern int HMAC_Final(IntPtr ctx, byte* md, uint* len);
        [DllImport(LibCrypto)] private static extern int HMAC_CTX_copy(IntPtr dctx, IntPtr sctx);
        [DllImport(LibCrypto)] private static extern byte* HMAC(IntPtr evpMd, void* key, int keyLen, byte* data, nuint dataLen, byte* md, uint* mdLen);

        [DllImport(LibCrypto)] private static extern IntPtr EVP_CIPHER_CTX_new();
        [DllImport(LibCrypto)] private static extern void EVP_CIPHER_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_CIPHER_CTX_reset(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern void EVP_CIPHER_CTX_set_flags(IntPtr ctx, int flags);
        [DllImport(LibCrypto)] private static extern int EVP_CipherInit_ex(IntPtr ctx, IntPtr type, IntPtr engine, byte* key, byte* iv, int enc);
        [DllImport(LibCrypto)] private static extern int EVP_CIPHER_CTX_set_key_length(IntPtr ctx, int keylen);
        [DllImport(LibCrypto)] private static extern int EVP_CIPHER_get_nid(IntPtr cipher);
        [DllImport(LibCrypto)] private static extern int EVP_CIPHER_CTX_ctrl(IntPtr ctx, int type, int arg, void* ptr);
        [DllImport(LibCrypto)] private static extern int EVP_CIPHER_CTX_set_padding(IntPtr ctx, int pad);
        [DllImport(LibCrypto)] private static extern int EVP_CipherUpdate(IntPtr ctx, byte* outBuf, int* outl, byte* inBuf, int inl);
        [DllImport(LibCrypto)] private static extern int EVP_CipherFinal_ex(IntPtr ctx, byte* outm, int* outl);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_ecb();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_cbc();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_gcm();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_cfb128();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_cfb8();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_ccm();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_wrap_pad();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_128_wrap();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_ecb();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_cfb128();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_cfb8();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_cbc();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_gcm();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_ccm();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_wrap_pad();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_192_wrap();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_ecb();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_cfb128();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_cfb8();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_cbc();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_gcm();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_ccm();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_wrap_pad();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_aes_256_wrap();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_des_ecb();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_des_cfb8();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_des_cbc();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_des_ede3();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_des_ede3_cfb8();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_des_ede3_cfb64();
        [DllImport(LibCrypto)] private static extern IntPtr EVP_des_ede3_cbc();

        [DllImport(LibCrypto)] private static extern IntPtr EVP_KDF_fetch(IntPtr libctx, byte* algorithm, byte* properties);
        [DllImport(LibCrypto)] private static extern void EVP_KDF_free(IntPtr kdf);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_KDF_CTX_new(IntPtr kdf);
        [DllImport(LibCrypto)] private static extern void EVP_KDF_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_KDF_derive(IntPtr ctx, byte* key, nuint keylen, OsslParam* parameters);

        // ---- digests (pal_evp.c) ---------------------------------------------------------------------------------------
        [RuntimeExport("CryptoNative_EvpMdCtxCreate", Trigger + "CryptoNative_EvpMdCtxCreate")]
        public static IntPtr EvpMdCtxCreate(IntPtr type)
        {
            ERR_clear_error();
            IntPtr ctx = EVP_MD_CTX_new();
            if (ctx == IntPtr.Zero)
            {
                // One of the few allocations OpenSSL does not report to the queue.
                PutMallocFailure();
                return IntPtr.Zero;
            }
            // (OpenSSL 1.x's non-FIPS flag for MD5 does not apply: OpenSSL 3 fetches MD5 with "-fips" below.)
            if (EVP_DigestInit_ex(ctx, type, IntPtr.Zero) == 0)
            {
                EVP_MD_CTX_free(ctx);
                return IntPtr.Zero;
            }
            return ctx;
        }

        [RuntimeExport("CryptoNative_EvpMdCtxDestroy", Trigger + "CryptoNative_EvpMdCtxDestroy")]
        public static void EvpMdCtxDestroy(IntPtr ctx)
        {
            if (ctx != IntPtr.Zero) EVP_MD_CTX_free(ctx);
        }

        [RuntimeExport("CryptoNative_EvpDigestReset", Trigger + "CryptoNative_EvpDigestReset")]
        public static int EvpDigestReset(IntPtr ctx, IntPtr type)
        {
            ERR_clear_error();
            return EVP_DigestInit_ex(ctx, type, IntPtr.Zero);
        }

        [RuntimeExport("CryptoNative_EvpDigestUpdate", Trigger + "CryptoNative_EvpDigestUpdate")]
        public static int EvpDigestUpdate(IntPtr ctx, void* d, int cnt)
        {
            if (cnt < 0) return 0;
            return EVP_DigestUpdate(ctx, d, (nuint)cnt);
        }

        [RuntimeExport("CryptoNative_EvpDigestFinalEx", Trigger + "CryptoNative_EvpDigestFinalEx")]
        public static int EvpDigestFinalEx(IntPtr ctx, byte* md, uint* s)
        {
            ERR_clear_error();
            uint size;
            int ret = EVP_DigestFinal_ex(ctx, md, &size);
            if (ret == 1) *s = size;
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpDigestFinalXOF", Trigger + "CryptoNative_EvpDigestFinalXOF")]
        public static int EvpDigestFinalXOF(IntPtr ctx, byte* md, uint len)
        {
            ERR_clear_error();
            // openssl/openssl#9431: some optimized paths cannot finalize a zero-length XOF; ask for one byte and drop it, so the
            // context still ends finalized.
            if (len == 0)
            {
                byte single = 0;
                int result = EVP_DigestFinalXOF(ctx, &single, 1);
                OPENSSL_cleanse(&single, 1);
                return result;
            }
            if (md == null) return -1;
            return EVP_DigestFinalXOF(ctx, md, len);
        }

        [RuntimeExport("CryptoNative_EvpMdCtxCopyEx", Trigger + "CryptoNative_EvpMdCtxCopyEx")]
        public static IntPtr EvpMdCtxCopyEx(IntPtr ctx)
        {
            if (ctx == IntPtr.Zero) return IntPtr.Zero;
            IntPtr dup = EVP_MD_CTX_new();
            if (dup == IntPtr.Zero)
            {
                PutMallocFailure();
                return IntPtr.Zero;
            }
            if (EVP_MD_CTX_copy_ex(dup, ctx) == 0)
            {
                EVP_MD_CTX_free(dup);
                return IntPtr.Zero;
            }
            return dup;
        }

        [RuntimeExport("CryptoNative_EvpDigestCurrent", Trigger + "CryptoNative_EvpDigestCurrent")]
        public static int EvpDigestCurrent(IntPtr ctx, byte* md, uint* s)
        {
            ERR_clear_error();
            IntPtr dup = EvpMdCtxCopyEx(ctx);
            if (dup == IntPtr.Zero) return 0;
            int ret = EvpDigestFinalEx(dup, md, s);
            EVP_MD_CTX_free(dup);
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpDigestCurrentXOF", Trigger + "CryptoNative_EvpDigestCurrentXOF")]
        public static int EvpDigestCurrentXOF(IntPtr ctx, byte* md, uint len)
        {
            ERR_clear_error();
            IntPtr dup = EvpMdCtxCopyEx(ctx);
            if (dup == IntPtr.Zero) return 0;
            int ret = EvpDigestFinalXOF(dup, md, len);
            EVP_MD_CTX_free(dup);
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpDigestOneShot", Trigger + "CryptoNative_EvpDigestOneShot")]
        public static int EvpDigestOneShot(IntPtr type, void* source, int sourceSize, byte* md, uint* mdSize)
        {
            ERR_clear_error();
            if (type == IntPtr.Zero || sourceSize < 0 || md == null || mdSize == null) return 0;
            IntPtr ctx = EvpMdCtxCreate(type);
            if (ctx == IntPtr.Zero) return 0;
            int ret = EVP_DigestUpdate(ctx, source, (nuint)sourceSize);
            if (ret != 1)
            {
                EvpMdCtxDestroy(ctx);
                return 0;
            }
            ret = EvpDigestFinalEx(ctx, md, mdSize);
            EvpMdCtxDestroy(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpDigestXOFOneShot", Trigger + "CryptoNative_EvpDigestXOFOneShot")]
        public static int EvpDigestXOFOneShot(IntPtr type, void* source, int sourceSize, byte* md, uint len)
        {
            ERR_clear_error();
            if (type == IntPtr.Zero || sourceSize < 0 || (md == null && len > 0)) return 0;
            IntPtr ctx = EvpMdCtxCreate(type);
            if (ctx == IntPtr.Zero) return 0;
            int ret = EVP_DigestUpdate(ctx, source, (nuint)sourceSize);
            if (ret != 1)
            {
                EvpMdCtxDestroy(ctx);
                return 0;
            }
            ret = EvpDigestFinalXOF(ctx, md, len);
            EvpMdCtxDestroy(ctx);
            return ret;
        }

        private static IntPtr s_evpDigestSqueeze;
        private static int s_evpDigestSqueezeLooked;

        [RuntimeExport("CryptoNative_EvpDigestSqueeze", Trigger + "CryptoNative_EvpDigestSqueeze")]
        public static int EvpDigestSqueeze(IntPtr ctx, byte* md, uint len, int* haveFeature)
        {
            ERR_clear_error();
            if (ctx == IntPtr.Zero || haveFeature == null || (md == null && len > 0)) return 0;
            *haveFeature = 0;
            // EVP_DigestSqueeze is OpenSSL 3.3's.
            if (s_evpDigestSqueezeLooked == 0)
            {
                s_evpDigestSqueeze = OptionalExport("EVP_DigestSqueeze");
                s_evpDigestSqueezeLooked = 1;
            }
            if (s_evpDigestSqueeze == IntPtr.Zero) return 0;
            *haveFeature = 1;
            return ((delegate* unmanaged<IntPtr, byte*, nuint, int>)s_evpDigestSqueeze)(ctx, md, len);
        }

        [RuntimeExport("CryptoNative_EvpMdSize", Trigger + "CryptoNative_EvpMdSize")]
        public static int EvpMdSize(IntPtr md) => EVP_MD_get_size(md);

        [RuntimeExport("CryptoNative_GetMaxMdSize", Trigger + "CryptoNative_GetMaxMdSize")]
        public static int GetMaxMdSize() => 64; // EVP_MAX_MD_SIZE

        // BUILD_MD_FETCH: the provider's implementation (EVP_MD_fetch), else the built-in one, fetched once. MD5 asks for a non-FIPS
        // implementation, so a FIPS-enforcing system still hashes with it, as .NET does.
        private static readonly IntPtr[] s_fetchedMds = new IntPtr[10];

        private static IntPtr FetchMd(int slot, ReadOnlySpan<byte> name, bool nonFips)
        {
            IntPtr md = s_fetchedMds[slot];
            if (md != IntPtr.Zero) return md;
            ERR_clear_error();
            fixed (byte* n = name) fixed (byte* q = "-fips\0"u8)
                md = EVP_MD_fetch(IntPtr.Zero, n, nonFips ? q : null);
            if (md == IntPtr.Zero) md = BuiltinMd(slot);
            // A race fetches twice and keeps one: an EVP_MD from EVP_MD_fetch is reference counted, the other is leaked as the shim
            // would never free its own either.
            s_fetchedMds[slot] = md;
            return md;
        }

        private static IntPtr BuiltinMd(int slot) => slot switch
        {
            0 => EVP_md5(), 1 => EVP_sha1(), 2 => EVP_sha256(), 3 => EVP_sha384(), 4 => EVP_sha512(),
            5 => EVP_sha3_256(), 6 => EVP_sha3_384(), 7 => EVP_sha3_512(), 8 => EVP_shake128(), _ => EVP_shake256(),
        };

        [RuntimeExport("CryptoNative_EvpMd5", Trigger + "CryptoNative_EvpMd5")]
        public static IntPtr EvpMd5() => FetchMd(0, "MD5\0"u8, true);
        [RuntimeExport("CryptoNative_EvpSha1", Trigger + "CryptoNative_EvpSha1")]
        public static IntPtr EvpSha1() => FetchMd(1, "SHA1\0"u8, false);
        [RuntimeExport("CryptoNative_EvpSha256", Trigger + "CryptoNative_EvpSha256")]
        public static IntPtr EvpSha256() => FetchMd(2, "SHA256\0"u8, false);
        [RuntimeExport("CryptoNative_EvpSha384", Trigger + "CryptoNative_EvpSha384")]
        public static IntPtr EvpSha384() => FetchMd(3, "SHA384\0"u8, false);
        [RuntimeExport("CryptoNative_EvpSha512", Trigger + "CryptoNative_EvpSha512")]
        public static IntPtr EvpSha512() => FetchMd(4, "SHA512\0"u8, false);
        [RuntimeExport("CryptoNative_EvpSha3_256", Trigger + "CryptoNative_EvpSha3_256")]
        public static IntPtr EvpSha3_256() => FetchMd(5, "SHA3-256\0"u8, false);
        [RuntimeExport("CryptoNative_EvpSha3_384", Trigger + "CryptoNative_EvpSha3_384")]
        public static IntPtr EvpSha3_384() => FetchMd(6, "SHA3-384\0"u8, false);
        [RuntimeExport("CryptoNative_EvpSha3_512", Trigger + "CryptoNative_EvpSha3_512")]
        public static IntPtr EvpSha3_512() => FetchMd(7, "SHA3-512\0"u8, false);
        [RuntimeExport("CryptoNative_EvpShake128", Trigger + "CryptoNative_EvpShake128")]
        public static IntPtr EvpShake128() => FetchMd(8, "SHAKE-128\0"u8, false);
        [RuntimeExport("CryptoNative_EvpShake256", Trigger + "CryptoNative_EvpShake256")]
        public static IntPtr EvpShake256() => FetchMd(9, "SHAKE-256\0"u8, false);

        [RuntimeExport("CryptoNative_Pbkdf2", Trigger + "CryptoNative_Pbkdf2")]
        public static int Pbkdf2(byte* password, int passwordLength, byte* salt, int saltLength, int iterations, IntPtr digest,
            byte* destination, int destinationLength)
        {
            if (passwordLength < 0 || saltLength < 0 || iterations <= 0 || digest == IntPtr.Zero || destination == null || destinationLength < 0)
                return -1;
            ERR_clear_error();
            byte empty = 0;
            if (salt == null)
            {
                if (saltLength != 0) return -1;
                salt = &empty;
            }
            if (password == null)
            {
                if (passwordLength != 0) return -1;
                password = &empty;
            }
            return PKCS5_PBKDF2_HMAC(password, passwordLength, salt, saltLength, iterations, digest, destinationLength, destination);
        }

        // ---- HMAC (pal_hmac.c) -----------------------------------------------------------------------------------------
        // DN_MAC_CTX: an EVP_MAC_CTX (OpenSSL 3's HMAC), or a legacy HMAC_CTX for MD5 (HMAC-MD5 is missing from some distributions'
        // providers), plus the key for the OpenSSL versions whose EVP_MAC_init cannot re-use it.
        [StructLayout(LayoutKind.Sequential)]
        private struct DnMacCtx
        {
            public IntPtr Legacy;
            public IntPtr Mac;
            public byte* Key;
            public nuint KeyLen;
        }

        private static IntPtr s_evpMacHmac;
        private static int s_evpMacHmacFetched;

        [RuntimeExport("CryptoNative_HmacCreate", Trigger + "CryptoNative_HmacCreate")]
        public static IntPtr HmacCreate(byte* key, int keyLen, IntPtr md)
        {
            if (keyLen < 0 || (key == null && keyLen != 0) || md == IntPtr.Zero) return IntPtr.Zero;
            if (s_evpMacHmacFetched == 0)
            {
                IntPtr fetched;
                fixed (byte* hmac = "HMAC\0"u8) fetched = EVP_MAC_fetch(IntPtr.Zero, hmac, null);
                s_evpMacHmac = fetched;
                s_evpMacHmacFetched = 1;
            }
            ERR_clear_error();
            // A null key would make HMAC_Init_ex re-use the "existing" one.
            byte none = 0;
            if (keyLen == 0) key = &none;

            bool isMd5;
            fixed (byte* md5 = "MD5\0"u8) isMd5 = EVP_MD_is_a(md, md5) != 0;
            if (s_evpMacHmac != IntPtr.Zero && !isMd5)
            {
                IntPtr evpMac = EVP_MAC_CTX_new(s_evpMacHmac);
                if (evpMac == IntPtr.Zero) return IntPtr.Zero;
                byte* algorithm = EVP_MD_get0_name(md);
                OsslParam* parameters = stackalloc OsslParam[3];
                fixed (byte* keyName = "key\0"u8) fixed (byte* digestName = "digest\0"u8)
                {
                    parameters[0] = OctetParam(keyName, key, (nuint)keyLen);
                    parameters[1] = Utf8Param(digestName, algorithm);
                    parameters[2] = EndParam();
                    if (EVP_MAC_init(evpMac, null, 0, parameters) == 0)
                    {
                        EVP_MAC_CTX_free(evpMac);
                        return IntPtr.Zero;
                    }
                }
                DnMacCtx* dn = (DnMacCtx*)OpensslZalloc((nuint)sizeof(DnMacCtx));
                if (dn == null)
                {
                    EVP_MAC_CTX_free(evpMac);
                    return IntPtr.Zero;
                }
                long version = OpenSslVersionNumber();
                // OpenSSL 3.0.0 to 3.0.2: EVP_MAC_init cannot reset with the existing key, so the key is kept to be passed again.
                if (version >= OPENSSL_VERSION_3_0_RTM && version <= OPENSSL_VERSION_3_0_2_RTM)
                {
                    byte* keyCopy = (byte*)OpensslMalloc(keyLen == 0 ? 1 : (nuint)keyLen);
                    if (keyCopy == null)
                    {
                        EVP_MAC_CTX_free(evpMac);
                        OpensslFree(dn);
                        return IntPtr.Zero;
                    }
                    Buffer.MemoryCopy(key, keyCopy, keyLen, keyLen);
                    dn->Key = keyCopy;
                    dn->KeyLen = (nuint)keyLen;
                }
                dn->Mac = evpMac;
                return (IntPtr)dn;
            }

            IntPtr ctx = HMAC_CTX_new();
            if (ctx == IntPtr.Zero)
            {
                PutMallocFailure();
                return IntPtr.Zero;
            }
            if (HMAC_Init_ex(ctx, key, keyLen, md, IntPtr.Zero) == 0)
            {
                HMAC_CTX_free(ctx);
                return IntPtr.Zero;
            }
            DnMacCtx* legacy = (DnMacCtx*)OpensslZalloc((nuint)sizeof(DnMacCtx));
            if (legacy == null)
            {
                HMAC_CTX_free(ctx);
                return IntPtr.Zero;
            }
            legacy->Legacy = ctx;
            return (IntPtr)legacy;
        }

        [RuntimeExport("CryptoNative_HmacDestroy", Trigger + "CryptoNative_HmacDestroy")]
        public static void HmacDestroy(IntPtr handle)
        {
            DnMacCtx* ctx = (DnMacCtx*)handle;
            if (ctx == null) return;
            if (ctx->Mac != IntPtr.Zero)
            {
                EVP_MAC_CTX_free(ctx->Mac);
                ctx->Mac = IntPtr.Zero;
            }
            if (ctx->Key != null)
            {
                CRYPTO_clear_free(ctx->Key, ctx->KeyLen, null, 0);
                ctx->Key = null;
                ctx->KeyLen = 0;
            }
            if (ctx->Legacy != IntPtr.Zero)
            {
                HMAC_CTX_free(ctx->Legacy);
                ctx->Legacy = IntPtr.Zero;
            }
            OpensslFree(ctx);
        }

        [RuntimeExport("CryptoNative_HmacReset", Trigger + "CryptoNative_HmacReset")]
        public static int HmacReset(IntPtr handle)
        {
            DnMacCtx* ctx = (DnMacCtx*)handle;
            if (ctx == null) return -1;
            ERR_clear_error();
            // A null key (the fixed OpenSSL versions) means "re-use the existing key".
            if (ctx->Mac != IntPtr.Zero) return EVP_MAC_init(ctx->Mac, ctx->Key, ctx->KeyLen, null);
            if (ctx->Legacy != IntPtr.Zero) return HMAC_Init_ex(ctx->Legacy, null, 0, IntPtr.Zero, IntPtr.Zero);
            return -1;
        }

        [RuntimeExport("CryptoNative_HmacUpdate", Trigger + "CryptoNative_HmacUpdate")]
        public static int HmacUpdate(IntPtr handle, byte* data, int len)
        {
            DnMacCtx* ctx = (DnMacCtx*)handle;
            ERR_clear_error();
            if (ctx == null || len < 0 || (data == null && len != 0)) return 0;
            if (ctx->Mac != IntPtr.Zero) return EVP_MAC_update(ctx->Mac, data, (nuint)len);
            if (ctx->Legacy != IntPtr.Zero) return HMAC_Update(ctx->Legacy, data, (nuint)len);
            return -1;
        }

        [RuntimeExport("CryptoNative_HmacFinal", Trigger + "CryptoNative_HmacFinal")]
        public static int HmacFinal(IntPtr handle, byte* md, int* len)
        {
            DnMacCtx* ctx = (DnMacCtx*)handle;
            ERR_clear_error();
            if (ctx == null || len == null || *len < 0 || (md == null && *len != 0)) return 0;
            if (ctx->Mac != IntPtr.Zero)
            {
                // EVP_MAC_final refuses to write more than the caller's length.
                nuint outl = 0;
                int ret = EVP_MAC_final(ctx->Mac, md, &outl, (nuint)(*len));
                *len = (int)outl;
                return ret;
            }
            if (ctx->Legacy != IntPtr.Zero)
            {
                // HMAC_Final writes the digest's size: the framework sizes the buffer from EvpMdSize, as with the shim.
                uint unsignedLen = (uint)*len;
                int ret = HMAC_Final(ctx->Legacy, md, &unsignedLen);
                *len = (int)unsignedLen;
                return ret;
            }
            return -1;
        }

        [RuntimeExport("CryptoNative_HmacCopy", Trigger + "CryptoNative_HmacCopy")]
        public static IntPtr HmacCopy(IntPtr handle)
        {
            DnMacCtx* ctx = (DnMacCtx*)handle;
            if (ctx == null) return IntPtr.Zero;
            ERR_clear_error();
            if (ctx->Mac != IntPtr.Zero)
            {
                IntPtr macDup = EVP_MAC_CTX_dup(ctx->Mac);
                if (macDup == IntPtr.Zero) return IntPtr.Zero;
                DnMacCtx* copy = (DnMacCtx*)OpensslZalloc((nuint)sizeof(DnMacCtx));
                if (copy == null)
                {
                    EVP_MAC_CTX_free(macDup);
                    return IntPtr.Zero;
                }
                if (ctx->Key != null)
                {
                    nuint keyLen = ctx->KeyLen;
                    byte* keyCopy = (byte*)OpensslMalloc(keyLen == 0 ? 1 : keyLen);
                    if (keyCopy == null)
                    {
                        EVP_MAC_CTX_free(macDup);
                        OpensslFree(copy);
                        return IntPtr.Zero;
                    }
                    Buffer.MemoryCopy(ctx->Key, keyCopy, (long)keyLen, (long)keyLen);
                    copy->Key = keyCopy;
                    copy->KeyLen = keyLen;
                }
                copy->Mac = macDup;
                return (IntPtr)copy;
            }
            if (ctx->Legacy == IntPtr.Zero) return IntPtr.Zero;
            IntPtr dup = HMAC_CTX_new();
            if (dup == IntPtr.Zero)
            {
                PutMallocFailure();
                return IntPtr.Zero;
            }
            if (HMAC_CTX_copy(dup, ctx->Legacy) == 0)
            {
                HMAC_CTX_free(dup);
                return IntPtr.Zero;
            }
            DnMacCtx* legacyCopy = (DnMacCtx*)OpensslZalloc((nuint)sizeof(DnMacCtx));
            if (legacyCopy == null)
            {
                HMAC_CTX_free(dup);
                return IntPtr.Zero;
            }
            legacyCopy->Legacy = dup;
            return (IntPtr)legacyCopy;
        }

        [RuntimeExport("CryptoNative_HmacCurrent", Trigger + "CryptoNative_HmacCurrent")]
        public static int HmacCurrent(IntPtr handle, byte* md, int* len)
        {
            ERR_clear_error();
            if (handle == IntPtr.Zero || len == null || *len < 0) return 0;
            IntPtr dup = HmacCopy(handle);
            if (dup == IntPtr.Zero) return 0;
            int ret = HmacFinal(dup, md, len);
            HmacDestroy(dup);
            return ret;
        }

        [RuntimeExport("CryptoNative_HmacOneShot", Trigger + "CryptoNative_HmacOneShot")]
        public static int HmacOneShot(IntPtr type, byte* key, int keySize, byte* source, int sourceSize, byte* md, int* mdSize)
        {
            if (type == IntPtr.Zero || md == null || mdSize == null || keySize < 0 || *mdSize < 0 || sourceSize < 0
                || (source == null && sourceSize != 0))
                return 0;
            ERR_clear_error();
            byte empty = 0;
            if (key == null)
            {
                if (keySize != 0) return -1;
                key = &empty;
            }
            uint unsignedSize = (uint)*mdSize;
            byte* result = HMAC(type, key, keySize, source, (nuint)sourceSize, md, &unsignedSize);
            *mdSize = (int)unsignedSize;
            return result == null ? 0 : 1;
        }

        // ---- EVP_MAC: KMAC (pal_evp_mac.c) -----------------------------------------------------------------------------
        [RuntimeExport("CryptoNative_EvpMacFetch", Trigger + "CryptoNative_EvpMacFetch")]
        public static IntPtr EvpMacFetch(byte* algorithm, int* haveFeature)
        {
            ERR_clear_error();
            IntPtr mac = EVP_MAC_fetch(IntPtr.Zero, algorithm, null);
            if (mac != IntPtr.Zero)
            {
                *haveFeature = 1;
                return mac;
            }
            *haveFeature = ErrGetReason(ERR_peek_error()) == ERR_R_UNSUPPORTED ? 0 : 1;
            return IntPtr.Zero;
        }

        [RuntimeExport("CryptoNative_EvpMacFree", Trigger + "CryptoNative_EvpMacFree")]
        public static void EvpMacFree(IntPtr mac) => EVP_MAC_free(mac);

        [RuntimeExport("CryptoNative_EvpMacCtxNew", Trigger + "CryptoNative_EvpMacCtxNew")]
        public static IntPtr EvpMacCtxNew(IntPtr mac)
        {
            ERR_clear_error();
            return EVP_MAC_CTX_new(mac);
        }

        [RuntimeExport("CryptoNative_EvpMacCtxFree", Trigger + "CryptoNative_EvpMacCtxFree")]
        public static void EvpMacCtxFree(IntPtr ctx) => EVP_MAC_CTX_free(ctx);

        [RuntimeExport("CryptoNative_EvpMacInit", Trigger + "CryptoNative_EvpMacInit")]
        public static int EvpMacInit(IntPtr ctx, byte* key, int keyLength, byte* customizationString, int customizationStringLength, int xof)
        {
            if (ctx == IntPtr.Zero || (key == null && keyLength > 0) || keyLength < 0
                || (customizationString == null && customizationStringLength > 0) || customizationStringLength < 0)
                return -1;
            ERR_clear_error();
            OsslParam* parameters = stackalloc OsslParam[4];
            int i = 0;
            fixed (byte* keyName = "key\0"u8) fixed (byte* xofName = "xof\0"u8) fixed (byte* customName = "custom\0"u8)
            {
                parameters[i++] = OctetParam(keyName, key, (nuint)keyLength);
                parameters[i++] = Int32Param(xofName, &xof);
                if (customizationString != null && customizationStringLength > 0)
                    parameters[i++] = OctetParam(customName, customizationString, (nuint)customizationStringLength);
                parameters[i] = EndParam();
                return EVP_MAC_init(ctx, null, 0, parameters) == 0 ? 0 : 1;
            }
        }

        [RuntimeExport("CryptoNative_EvpMacReset", Trigger + "CryptoNative_EvpMacReset")]
        public static int EvpMacReset(IntPtr ctx)
        {
            if (ctx == IntPtr.Zero) return -1;
            ERR_clear_error();
            return EVP_MAC_init(ctx, null, 0, null) == 0 ? 0 : 1;
        }

        [RuntimeExport("CryptoNative_EvpMacUpdate", Trigger + "CryptoNative_EvpMacUpdate")]
        public static int EvpMacUpdate(IntPtr ctx, byte* data, int dataLength)
        {
            if (ctx == IntPtr.Zero || (data == null && dataLength > 0) || dataLength < 0) return -1;
            ERR_clear_error();
            if (dataLength > 0 && EVP_MAC_update(ctx, data, (nuint)dataLength) == 0) return 0;
            return 1;
        }

        [RuntimeExport("CryptoNative_EvpMacFinal", Trigger + "CryptoNative_EvpMacFinal")]
        public static int EvpMacFinal(IntPtr ctx, byte* mac, int macLength)
        {
            if (ctx == IntPtr.Zero || (mac == null && macLength > 0) || macLength < 0) return -1;
            ERR_clear_error();
            OsslParam* parameters = stackalloc OsslParam[2];
            fixed (byte* sizeName = "size\0"u8)
            {
                parameters[0] = Int32Param(sizeName, &macLength);
                parameters[1] = EndParam();
                if (EVP_MAC_CTX_set_params(ctx, parameters) == 0) return 0;
            }
            nuint written = 0;
            if (EVP_MAC_final(ctx, mac, &written, (nuint)macLength) == 0) return 0;
            return written != (nuint)macLength ? -3 : 1;
        }

        [RuntimeExport("CryptoNative_EvpMacCurrent", Trigger + "CryptoNative_EvpMacCurrent")]
        public static int EvpMacCurrent(IntPtr ctx, byte* mac, int macLength)
        {
            IntPtr dup = EVP_MAC_CTX_dup(ctx);
            if (dup == IntPtr.Zero) return 0;
            int ret = EvpMacFinal(dup, mac, macLength);
            EVP_MAC_CTX_free(dup);
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpMacCtxDup", Trigger + "CryptoNative_EvpMacCtxDup")]
        public static IntPtr EvpMacCtxDup(IntPtr ctx) => EVP_MAC_CTX_dup(ctx);

        [RuntimeExport("CryptoNative_EvpMacOneShot", Trigger + "CryptoNative_EvpMacOneShot")]
        public static int EvpMacOneShot(IntPtr mac, byte* key, int keyLength, byte* customizationString, int customizationStringLength,
            byte* data, int dataLength, byte* destination, int destinationLength, int xof)
        {
            if (mac == IntPtr.Zero || keyLength < 0 || customizationStringLength < 0 || destinationLength < 0 || dataLength < 0
                || (key == null && keyLength > 0) || (customizationString == null && customizationStringLength > 0)
                || (destination == null && destinationLength > 0) || (data == null && dataLength > 0))
                return -1;
            if (destinationLength == 0) return 1;
            ERR_clear_error();
            IntPtr ctx = EVP_MAC_CTX_new(mac);
            if (ctx == IntPtr.Zero) return 0;
            int ret = 0;
            OsslParam* parameters = stackalloc OsslParam[5];
            int i = 0;
            fixed (byte* keyName = "key\0"u8) fixed (byte* sizeName = "size\0"u8) fixed (byte* xofName = "xof\0"u8) fixed (byte* customName = "custom\0"u8)
            {
                parameters[i++] = OctetParam(keyName, key, (nuint)keyLength);
                parameters[i++] = Int32Param(sizeName, &destinationLength);
                parameters[i++] = Int32Param(xofName, &xof);
                if (customizationString != null && customizationStringLength > 0)
                    parameters[i++] = OctetParam(customName, customizationString, (nuint)customizationStringLength);
                parameters[i] = EndParam();
                nuint written = 0;
                if (EVP_MAC_init(ctx, null, 0, parameters) != 0
                    && EVP_MAC_update(ctx, data, (nuint)dataLength) != 0
                    && EVP_MAC_final(ctx, destination, &written, (nuint)destinationLength) != 0)
                    ret = written != (nuint)destinationLength ? -3 : 1;
            }
            EVP_MAC_CTX_free(ctx);
            return ret;
        }

        // ---- symmetric ciphers (pal_evp_cipher.c) ----------------------------------------------------------------------
        private const int NID_rc2_cbc = 37, NID_rc2_ecb = 38, EVP_CTRL_SET_RC2_KEY_BITS = 3, EVP_CTRL_GCM_SET_IVLEN = 9,
            EVP_CTRL_CCM_SET_IVLEN = 9, EVP_CTRL_GCM_GET_TAG = 16, EVP_CTRL_GCM_SET_TAG = 17, EVP_CTRL_AEAD_GET_TAG = 16, EVP_CTRL_AEAD_SET_TAG = 17;

        [RuntimeExport("CryptoNative_EvpCipherCreate2", Trigger + "CryptoNative_EvpCipherCreate2")]
        public static IntPtr EvpCipherCreate2(IntPtr type, byte* key, int keyLength, byte* iv, int enc)
        {
            ERR_clear_error();
            IntPtr ctx = EVP_CIPHER_CTX_new();
            if (ctx == IntPtr.Zero)
            {
                PutMallocFailure();
                return IntPtr.Zero;
            }
            if (EVP_CIPHER_CTX_reset(ctx) == 0) { EVP_CIPHER_CTX_free(ctx); return IntPtr.Zero; }
            // EVP_CIPHER_CTX_FLAG_WRAP_ALLOW: OpenSSL 1.1's AES-KWP needed it; a no-op in OpenSSL 3.
            EVP_CIPHER_CTX_set_flags(ctx, 1);
            // A partial initialization first, so that the key length can be set.
            if (EVP_CipherInit_ex(ctx, type, IntPtr.Zero, null, null, 0) == 0) { EVP_CIPHER_CTX_free(ctx); return IntPtr.Zero; }
            if (keyLength > 0 && EVP_CIPHER_CTX_set_key_length(ctx, keyLength / 8) == 0) { EVP_CIPHER_CTX_free(ctx); return IntPtr.Zero; }
            int nid = EVP_CIPHER_get_nid(type);
            if ((nid == NID_rc2_ecb || nid == NID_rc2_cbc) && EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_SET_RC2_KEY_BITS, keyLength, null) <= 0)
            {
                EVP_CIPHER_CTX_free(ctx);
                return IntPtr.Zero;
            }
            if (EVP_CipherInit_ex(ctx, IntPtr.Zero, IntPtr.Zero, key, iv, enc) == 0) { EVP_CIPHER_CTX_free(ctx); return IntPtr.Zero; }
            return ctx;
        }

        [RuntimeExport("CryptoNative_EvpCipherCreatePartial", Trigger + "CryptoNative_EvpCipherCreatePartial")]
        public static IntPtr EvpCipherCreatePartial(IntPtr type)
        {
            ERR_clear_error();
            IntPtr ctx = EVP_CIPHER_CTX_new();
            if (ctx == IntPtr.Zero)
            {
                PutMallocFailure();
                return IntPtr.Zero;
            }
            if (EVP_CIPHER_CTX_reset(ctx) == 0 || EVP_CipherInit_ex(ctx, type, IntPtr.Zero, null, null, 0) == 0)
            {
                EVP_CIPHER_CTX_free(ctx);
                return IntPtr.Zero;
            }
            return ctx;
        }

        [RuntimeExport("CryptoNative_EvpCipherSetKeyAndIV", Trigger + "CryptoNative_EvpCipherSetKeyAndIV")]
        public static int EvpCipherSetKeyAndIV(IntPtr ctx, byte* key, byte* iv, int enc)
        {
            ERR_clear_error();
            return EVP_CipherInit_ex(ctx, IntPtr.Zero, IntPtr.Zero, key, iv, enc);
        }

        [RuntimeExport("CryptoNative_EvpCipherSetGcmNonceLength", Trigger + "CryptoNative_EvpCipherSetGcmNonceLength")]
        public static int EvpCipherSetGcmNonceLength(IntPtr ctx, int ivLength)
        {
            ERR_clear_error();
            return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_SET_IVLEN, ivLength, null);
        }

        [RuntimeExport("CryptoNative_EvpCipherSetCcmNonceLength", Trigger + "CryptoNative_EvpCipherSetCcmNonceLength")]
        public static int EvpCipherSetCcmNonceLength(IntPtr ctx, int ivLength)
        {
            ERR_clear_error();
            return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_CCM_SET_IVLEN, ivLength, null);
        }

        [RuntimeExport("CryptoNative_EvpCipherDestroy", Trigger + "CryptoNative_EvpCipherDestroy")]
        public static void EvpCipherDestroy(IntPtr ctx)
        {
            if (ctx != IntPtr.Zero) EVP_CIPHER_CTX_free(ctx);
        }

        [RuntimeExport("CryptoNative_EvpCipherReset", Trigger + "CryptoNative_EvpCipherReset")]
        public static int EvpCipherReset(IntPtr ctx, byte* pIv, int cIv)
        {
            ERR_clear_error();
            // -1: keep the current direction.
            return EVP_CipherInit_ex(ctx, IntPtr.Zero, IntPtr.Zero, null, pIv, -1);
        }

        [RuntimeExport("CryptoNative_EvpCipherCtxSetPadding", Trigger + "CryptoNative_EvpCipherCtxSetPadding")]
        public static int EvpCipherCtxSetPadding(IntPtr x, int padding) => EVP_CIPHER_CTX_set_padding(x, padding);

        [RuntimeExport("CryptoNative_EvpCipherUpdate", Trigger + "CryptoNative_EvpCipherUpdate")]
        public static int EvpCipherUpdate(IntPtr ctx, byte* output, int* outl, byte* input, int inl)
        {
            ERR_clear_error();
            int outLength = 0;
            int ret = EVP_CipherUpdate(ctx, output, &outLength, input, inl);
            *outl = ret == 1 ? outLength : 0;
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpCipherFinalEx", Trigger + "CryptoNative_EvpCipherFinalEx")]
        public static int EvpCipherFinalEx(IntPtr ctx, byte* outm, int* outl)
        {
            ERR_clear_error();
            int outLength;
            int ret = EVP_CipherFinal_ex(ctx, outm, &outLength);
            if (ret == 1) *outl = outLength;
            return ret;
        }

        [RuntimeExport("CryptoNative_EvpCipherGetGcmTag", Trigger + "CryptoNative_EvpCipherGetGcmTag")]
        public static int EvpCipherGetGcmTag(IntPtr ctx, byte* tag, int tagLength) { ERR_clear_error(); return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_GET_TAG, tagLength, tag); }
        [RuntimeExport("CryptoNative_EvpCipherSetGcmTag", Trigger + "CryptoNative_EvpCipherSetGcmTag")]
        public static int EvpCipherSetGcmTag(IntPtr ctx, byte* tag, int tagLength) { ERR_clear_error(); return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_SET_TAG, tagLength, tag); }
        [RuntimeExport("CryptoNative_EvpCipherGetCcmTag", Trigger + "CryptoNative_EvpCipherGetCcmTag")]
        public static int EvpCipherGetCcmTag(IntPtr ctx, byte* tag, int tagLength) { ERR_clear_error(); return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_GET_TAG, tagLength, tag); }
        [RuntimeExport("CryptoNative_EvpCipherSetCcmTag", Trigger + "CryptoNative_EvpCipherSetCcmTag")]
        public static int EvpCipherSetCcmTag(IntPtr ctx, byte* tag, int tagLength) { ERR_clear_error(); return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_SET_TAG, tagLength, tag); }
        [RuntimeExport("CryptoNative_EvpCipherGetAeadTag", Trigger + "CryptoNative_EvpCipherGetAeadTag")]
        public static int EvpCipherGetAeadTag(IntPtr ctx, byte* tag, int tagLength) { ERR_clear_error(); return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_AEAD_GET_TAG, tagLength, tag); }
        [RuntimeExport("CryptoNative_EvpCipherSetAeadTag", Trigger + "CryptoNative_EvpCipherSetAeadTag")]
        public static int EvpCipherSetAeadTag(IntPtr ctx, byte* tag, int tagLength) { ERR_clear_error(); return EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_AEAD_SET_TAG, tagLength, tag); }

        [RuntimeExport("CryptoNative_EvpAes128Ecb", Trigger + "CryptoNative_EvpAes128Ecb")] public static IntPtr EvpAes128Ecb() => EVP_aes_128_ecb();
        [RuntimeExport("CryptoNative_EvpAes128Cbc", Trigger + "CryptoNative_EvpAes128Cbc")] public static IntPtr EvpAes128Cbc() => EVP_aes_128_cbc();
        [RuntimeExport("CryptoNative_EvpAes128Gcm", Trigger + "CryptoNative_EvpAes128Gcm")] public static IntPtr EvpAes128Gcm() => EVP_aes_128_gcm();
        [RuntimeExport("CryptoNative_EvpAes128Cfb128", Trigger + "CryptoNative_EvpAes128Cfb128")] public static IntPtr EvpAes128Cfb128() => EVP_aes_128_cfb128();
        [RuntimeExport("CryptoNative_EvpAes128Cfb8", Trigger + "CryptoNative_EvpAes128Cfb8")] public static IntPtr EvpAes128Cfb8() => EVP_aes_128_cfb8();
        [RuntimeExport("CryptoNative_EvpAes128Ccm", Trigger + "CryptoNative_EvpAes128Ccm")] public static IntPtr EvpAes128Ccm() => EVP_aes_128_ccm();
        [RuntimeExport("CryptoNative_EvpAes128WrapPad", Trigger + "CryptoNative_EvpAes128WrapPad")] public static IntPtr EvpAes128WrapPad() => EVP_aes_128_wrap_pad();
        [RuntimeExport("CryptoNative_EvpAes128Wrap", Trigger + "CryptoNative_EvpAes128Wrap")] public static IntPtr EvpAes128Wrap() => EVP_aes_128_wrap();
        [RuntimeExport("CryptoNative_EvpAes192Ecb", Trigger + "CryptoNative_EvpAes192Ecb")] public static IntPtr EvpAes192Ecb() => EVP_aes_192_ecb();
        [RuntimeExport("CryptoNative_EvpAes192Cfb128", Trigger + "CryptoNative_EvpAes192Cfb128")] public static IntPtr EvpAes192Cfb128() => EVP_aes_192_cfb128();
        [RuntimeExport("CryptoNative_EvpAes192Cfb8", Trigger + "CryptoNative_EvpAes192Cfb8")] public static IntPtr EvpAes192Cfb8() => EVP_aes_192_cfb8();
        [RuntimeExport("CryptoNative_EvpAes192Cbc", Trigger + "CryptoNative_EvpAes192Cbc")] public static IntPtr EvpAes192Cbc() => EVP_aes_192_cbc();
        [RuntimeExport("CryptoNative_EvpAes192Gcm", Trigger + "CryptoNative_EvpAes192Gcm")] public static IntPtr EvpAes192Gcm() => EVP_aes_192_gcm();
        [RuntimeExport("CryptoNative_EvpAes192Ccm", Trigger + "CryptoNative_EvpAes192Ccm")] public static IntPtr EvpAes192Ccm() => EVP_aes_192_ccm();
        [RuntimeExport("CryptoNative_EvpAes192WrapPad", Trigger + "CryptoNative_EvpAes192WrapPad")] public static IntPtr EvpAes192WrapPad() => EVP_aes_192_wrap_pad();
        [RuntimeExport("CryptoNative_EvpAes192Wrap", Trigger + "CryptoNative_EvpAes192Wrap")] public static IntPtr EvpAes192Wrap() => EVP_aes_192_wrap();
        [RuntimeExport("CryptoNative_EvpAes256Ecb", Trigger + "CryptoNative_EvpAes256Ecb")] public static IntPtr EvpAes256Ecb() => EVP_aes_256_ecb();
        [RuntimeExport("CryptoNative_EvpAes256Cfb128", Trigger + "CryptoNative_EvpAes256Cfb128")] public static IntPtr EvpAes256Cfb128() => EVP_aes_256_cfb128();
        [RuntimeExport("CryptoNative_EvpAes256Cfb8", Trigger + "CryptoNative_EvpAes256Cfb8")] public static IntPtr EvpAes256Cfb8() => EVP_aes_256_cfb8();
        [RuntimeExport("CryptoNative_EvpAes256Cbc", Trigger + "CryptoNative_EvpAes256Cbc")] public static IntPtr EvpAes256Cbc() => EVP_aes_256_cbc();
        [RuntimeExport("CryptoNative_EvpAes256Gcm", Trigger + "CryptoNative_EvpAes256Gcm")] public static IntPtr EvpAes256Gcm() => EVP_aes_256_gcm();
        [RuntimeExport("CryptoNative_EvpAes256Ccm", Trigger + "CryptoNative_EvpAes256Ccm")] public static IntPtr EvpAes256Ccm() => EVP_aes_256_ccm();
        [RuntimeExport("CryptoNative_EvpAes256WrapPad", Trigger + "CryptoNative_EvpAes256WrapPad")] public static IntPtr EvpAes256WrapPad() => EVP_aes_256_wrap_pad();
        [RuntimeExport("CryptoNative_EvpAes256Wrap", Trigger + "CryptoNative_EvpAes256Wrap")] public static IntPtr EvpAes256Wrap() => EVP_aes_256_wrap();
        [RuntimeExport("CryptoNative_EvpDesEcb", Trigger + "CryptoNative_EvpDesEcb")] public static IntPtr EvpDesEcb() => EVP_des_ecb();
        [RuntimeExport("CryptoNative_EvpDesCfb8", Trigger + "CryptoNative_EvpDesCfb8")] public static IntPtr EvpDesCfb8() => EVP_des_cfb8();
        [RuntimeExport("CryptoNative_EvpDesCbc", Trigger + "CryptoNative_EvpDesCbc")] public static IntPtr EvpDesCbc() => EVP_des_cbc();
        [RuntimeExport("CryptoNative_EvpDes3Ecb", Trigger + "CryptoNative_EvpDes3Ecb")] public static IntPtr EvpDes3Ecb() => EVP_des_ede3();
        [RuntimeExport("CryptoNative_EvpDes3Cfb8", Trigger + "CryptoNative_EvpDes3Cfb8")] public static IntPtr EvpDes3Cfb8() => EVP_des_ede3_cfb8();
        [RuntimeExport("CryptoNative_EvpDes3Cfb64", Trigger + "CryptoNative_EvpDes3Cfb64")] public static IntPtr EvpDes3Cfb64() => EVP_des_ede3_cfb64();
        [RuntimeExport("CryptoNative_EvpDes3Cbc", Trigger + "CryptoNative_EvpDes3Cbc")] public static IntPtr EvpDes3Cbc() => EVP_des_ede3_cbc();

        // RC2 and ChaCha20-Poly1305 may be compiled out of a distribution's libcrypto: null then, as the shim's API_EXISTS gives.
        [RuntimeExport("CryptoNative_EvpRC2Ecb", Trigger + "CryptoNative_EvpRC2Ecb")]
        public static IntPtr EvpRC2Ecb() => OptionalExport("EVP_rc2_ecb") is var f && f != IntPtr.Zero ? ((delegate* unmanaged<IntPtr>)f)() : IntPtr.Zero;
        [RuntimeExport("CryptoNative_EvpRC2Cbc", Trigger + "CryptoNative_EvpRC2Cbc")]
        public static IntPtr EvpRC2Cbc() => OptionalExport("EVP_rc2_cbc") is var f && f != IntPtr.Zero ? ((delegate* unmanaged<IntPtr>)f)() : IntPtr.Zero;
        [RuntimeExport("CryptoNative_EvpChaCha20Poly1305", Trigger + "CryptoNative_EvpChaCha20Poly1305")]
        public static IntPtr EvpChaCha20Poly1305() => OptionalExport("EVP_chacha20_poly1305") is var f && f != IntPtr.Zero ? ((delegate* unmanaged<IntPtr>)f)() : IntPtr.Zero;

        // ---- EVP_KDF: HKDF and SP 800-108 (pal_evp_kdf.c) --------------------------------------------------------------
        [RuntimeExport("CryptoNative_EvpKdfFree", Trigger + "CryptoNative_EvpKdfFree")]
        public static void EvpKdfFree(IntPtr kdf) => EVP_KDF_free(kdf);

        [RuntimeExport("CryptoNative_EvpKdfFetch", Trigger + "CryptoNative_EvpKdfFetch")]
        public static IntPtr EvpKdfFetch(byte* algorithm, int* haveFeature)
        {
            ERR_clear_error();
            IntPtr kdf = EVP_KDF_fetch(IntPtr.Zero, algorithm, null);
            if (kdf != IntPtr.Zero)
            {
                *haveFeature = 1;
                return kdf;
            }
            *haveFeature = ErrGetReason(ERR_peek_error()) == ERR_R_UNSUPPORTED ? 0 : 1;
            return IntPtr.Zero;
        }

        [RuntimeExport("CryptoNative_KbkdfHmacOneShot", Trigger + "CryptoNative_KbkdfHmacOneShot")]
        public static int KbkdfHmacOneShot(IntPtr kdf, byte* key, int keyLength, byte* algorithm, byte* label, int labelLength,
            byte* context, int contextLength, byte* destination, int destinationLength)
        {
            if (kdf == IntPtr.Zero || keyLength < 0 || algorithm == null || destination == null || destinationLength <= 0
                || labelLength < 0 || contextLength < 0 || (label == null && labelLength != 0) || (context == null && contextLength != 0)
                || (key == null && keyLength != 0))
                return 0;
            ERR_clear_error();
            // An empty key is one zero byte, as the shim passes it (OpenSSL refuses an empty one).
            byte zero = 0;
            if (key == null || keyLength == 0)
            {
                key = &zero;
                keyLength = 1;
            }
            IntPtr ctx = EVP_KDF_CTX_new(kdf);
            if (ctx == IntPtr.Zero) return 0;
            int ret = 0;
            OsslParam* parameters = stackalloc OsslParam[6];
            fixed (byte* digestName = "digest\0"u8) fixed (byte* macName = "mac\0"u8) fixed (byte* hmac = "HMAC\0"u8)
            fixed (byte* keyName = "key\0"u8) fixed (byte* saltName = "salt\0"u8) fixed (byte* infoName = "info\0"u8)
            {
                parameters[0] = Utf8Param(digestName, algorithm);
                parameters[1] = Utf8Param(macName, hmac);
                parameters[2] = OctetParam(keyName, key, (nuint)keyLength);
                parameters[3] = OctetParam(saltName, label, (nuint)labelLength);
                parameters[4] = OctetParam(infoName, context, (nuint)contextLength);
                parameters[5] = EndParam();
                if (EVP_KDF_derive(ctx, destination, (nuint)destinationLength, parameters) > 0) ret = 1;
            }
            EVP_KDF_CTX_free(ctx);
            return ret;
        }

        private static int HkdfCore(IntPtr kdf, int operation, byte* key, int keyLength, byte* algorithm, byte* salt, int saltLength,
            byte* info, int infoLength, byte* destination, int destinationLength)
        {
            if (kdf == IntPtr.Zero || keyLength < 0 || (key == null && keyLength != 0) || algorithm == null || destination == null
                || destinationLength <= 0 || saltLength < 0 || (salt == null && saltLength != 0) || infoLength < 0 || (info == null && infoLength != 0))
                return 0;
            ERR_clear_error();
            IntPtr ctx = EVP_KDF_CTX_new(kdf);
            if (ctx == IntPtr.Zero) return 0;
            int ret = 0;
            OsslParam* parameters = stackalloc OsslParam[6];
            int i = 0;
            fixed (byte* keyName = "key\0"u8) fixed (byte* digestName = "digest\0"u8) fixed (byte* saltName = "salt\0"u8)
            fixed (byte* infoName = "info\0"u8) fixed (byte* modeName = "mode\0"u8)
            {
                parameters[i++] = OctetParam(keyName, key, (nuint)keyLength);
                parameters[i++] = Utf8Param(digestName, algorithm);
                if (salt != null && saltLength > 0) parameters[i++] = OctetParam(saltName, salt, (nuint)saltLength);
                if (info != null && infoLength > 0) parameters[i++] = OctetParam(infoName, info, (nuint)infoLength);
                parameters[i++] = Int32Param(modeName, &operation);
                parameters[i] = EndParam();
                if (EVP_KDF_derive(ctx, destination, (nuint)destinationLength, parameters) > 0) ret = 1;
            }
            EVP_KDF_CTX_free(ctx);
            return ret;
        }

        [RuntimeExport("CryptoNative_HkdfDeriveKey", Trigger + "CryptoNative_HkdfDeriveKey")]
        public static int HkdfDeriveKey(IntPtr kdf, byte* ikm, int ikmLength, byte* algorithm, byte* salt, int saltLength, byte* info, int infoLength,
            byte* destination, int destinationLength) =>
            HkdfCore(kdf, 0 /* EXTRACT_AND_EXPAND */, ikm, ikmLength, algorithm, salt, saltLength, info, infoLength, destination, destinationLength);

        [RuntimeExport("CryptoNative_HkdfExpand", Trigger + "CryptoNative_HkdfExpand")]
        public static int HkdfExpand(IntPtr kdf, byte* prk, int prkLength, byte* algorithm, byte* info, int infoLength, byte* destination, int destinationLength) =>
            HkdfCore(kdf, 2 /* EXPAND_ONLY */, prk, prkLength, algorithm, null, 0, info, infoLength, destination, destinationLength);

        [RuntimeExport("CryptoNative_HkdfExtract", Trigger + "CryptoNative_HkdfExtract")]
        public static int HkdfExtract(IntPtr kdf, byte* ikm, int ikmLength, byte* algorithm, byte* salt, int saltLength, byte* destination, int destinationLength) =>
            HkdfCore(kdf, 1 /* EXTRACT_ONLY */, ikm, ikmLength, algorithm, salt, saltLength, null, 0, destination, destinationLength);
    }
}
#endif
