// The shim's EVP_PKEY handles, their import and export (SubjectPublicKeyInfo, PKCS#8), the quick RSA consistency check, engine and
// provider keys over libcrypto: pal_evp_pkey.c (dotnet/runtime, .NET 11 GA), function by function. See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_new();
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_get_bits(IntPtr pkey);
        [DllImport(LibCrypto)] internal static extern int EVP_PKEY_up_ref(IntPtr pkey);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_get_base_id(IntPtr pkey);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_is_a(IntPtr pkey, byte* name);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_get_bn_param(IntPtr pkey, byte* keyName, IntPtr* bn);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_get_octet_string_param(IntPtr pkey, byte* keyName, byte* buf, nuint maxBufSize, nuint* outLen);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_CTX_new(IntPtr pkey, IntPtr engine);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_CTX_new_from_pkey(IntPtr libctx, IntPtr pkey, byte* propquery);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_CTX_new_from_name(IntPtr libctx, byte* name, byte* propquery);
        [DllImport(LibCrypto)] internal static extern void EVP_PKEY_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_check(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_public_check(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_fromdata_init(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_fromdata(IntPtr ctx, IntPtr* ppkey, int selection, OsslParam* parameters);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_PUBKEY(IntPtr a, byte** pp, long length);
        [DllImport(LibCrypto)] private static extern int i2d_PUBKEY(IntPtr a, byte** pp);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_PKCS8_PRIV_KEY_INFO(IntPtr a, byte** pp, long length);
        [DllImport(LibCrypto)] private static extern int i2d_PKCS8_PRIV_KEY_INFO(IntPtr a, byte** pp);
        [DllImport(LibCrypto)] private static extern void PKCS8_PRIV_KEY_INFO_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKCS82PKEY(IntPtr p8);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY2PKCS8(IntPtr pkey);
        [DllImport(LibCrypto)] private static extern ulong ERR_peek_error_line(byte** file, int* line);
        [DllImport(LibCrypto)] private static extern IntPtr BN_new();
        [DllImport(LibCrypto)] private static extern IntPtr BN_CTX_new();
        [DllImport(LibCrypto)] private static extern void BN_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int BN_mul(IntPtr r, IntPtr a, IntPtr b, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int BN_gcd(IntPtr r, IntPtr a, IntPtr b, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int BN_div(IntPtr dv, IntPtr rem, IntPtr m, IntPtr d, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int BN_sub(IntPtr r, IntPtr a, IntPtr b);
        [DllImport(LibCrypto)] private static extern int BN_mod_mul(IntPtr r, IntPtr a, IntPtr b, IntPtr m, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr BN_mod_inverse(IntPtr r, IntPtr a, IntPtr n, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int BN_cmp(IntPtr a, IntPtr b);
        [DllImport(LibCrypto)] private static extern int BN_is_zero(IntPtr a);
        [DllImport(LibCrypto)] private static extern int BN_is_one(IntPtr a);
        [DllImport(LibCrypto)] private static extern int BN_is_odd(IntPtr a);
        [DllImport(LibCrypto)] private static extern IntPtr BN_value_one();
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_LIB_CTX_new();
        [DllImport(LibCrypto)] private static extern void OSSL_LIB_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_PROVIDER_load(IntPtr libctx, byte* name);
        [DllImport(LibCrypto)] private static extern int OSSL_PROVIDER_unload(IntPtr prov);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_STORE_open_ex(byte* uri, IntPtr libctx, byte* propq, IntPtr uiMethod, IntPtr uiData, IntPtr parameters, IntPtr postProcess, IntPtr postProcessData);
        [DllImport(LibCrypto)] private static extern int OSSL_STORE_eof(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_STORE_load(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int OSSL_STORE_INFO_get_type(IntPtr info);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_STORE_INFO_get1_PKEY(IntPtr info);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_STORE_INFO_get1_PUBKEY(IntPtr info);
        [DllImport(LibCrypto)] private static extern void OSSL_STORE_INFO_free(IntPtr info);
        [DllImport(LibCrypto)] private static extern int OSSL_STORE_close(IntPtr ctx);

        private const int EVP_PKEY_RSA = 6, EVP_PKEY_DSA = 116, EVP_PKEY_EC = 408, EVP_PKEY_RSA_PSS = 912;
        private const int ERR_LIB_NONE = 1, ERR_LIB_RSA = 4;
        private const int RSA_R_BAD_E_VALUE = 101, RSA_R_MODULUS_TOO_LARGE = 105, RSA_R_D_E_NOT_CONGRUENT_TO_1 = 123,
            RSA_R_DMP1_NOT_CONGRUENT_TO_D = 124, RSA_R_DMQ1_NOT_CONGRUENT_TO_D = 125, RSA_R_IQMP_NOT_INVERSE_OF_Q = 126,
            RSA_R_N_DOES_NOT_EQUAL_P_Q = 127, RSA_R_VALUE_MISSING = 147;
        private const int EVP_R_DECODE_ERROR = 114, EVP_R_NO_KEY_SET = 154, EVP_R_UNSUPPORTED_ALGORITHM = 156;

        [RuntimeExport("CryptoNative_EvpPkeyCreate", Trigger + "CryptoNative_EvpPkeyCreate")]
        public static IntPtr EvpPkeyCreate()
        {
            ERR_clear_error();
            return EVP_PKEY_new();
        }

        [RuntimeExport("CryptoNative_EvpPkeyDestroy", Trigger + "CryptoNative_EvpPkeyDestroy")]
        public static void EvpPkeyDestroy(IntPtr pkey)
        {
            if (pkey != IntPtr.Zero) EVP_PKEY_free(pkey);
        }

        [RuntimeExport("CryptoNative_EvpPKeyBits", Trigger + "CryptoNative_EvpPKeyBits")]
        public static int EvpPKeyBits(IntPtr pkey) => EVP_PKEY_get_bits(pkey);

        [RuntimeExport("CryptoNative_UpRefEvpPkey", Trigger + "CryptoNative_UpRefEvpPkey")]
        public static int UpRefEvpPkey(IntPtr pkey) => pkey == IntPtr.Zero ? 0 : EVP_PKEY_up_ref(pkey);

        [RuntimeExport("CryptoNative_EvpPKeyType", Trigger + "CryptoNative_EvpPKeyType")]
        public static int EvpPKeyType(IntPtr key)
        {
            int baseId = EVP_PKEY_get_base_id(key);
            switch (baseId)
            {
                case EVP_PKEY_RSA: case EVP_PKEY_EC: case EVP_PKEY_DSA: return baseId;
                case EVP_PKEY_RSA_PSS: return EVP_PKEY_RSA;
                default: return 0;
            }
        }

        private static bool PkeyIsA(IntPtr key, ReadOnlySpan<byte> name)
        {
            fixed (byte* n = name) return EVP_PKEY_is_a(key, n) != 0;
        }

        [RuntimeExport("CryptoNative_EvpPKeyFamily", Trigger + "CryptoNative_EvpPKeyFamily")]
        public static int EvpPKeyFamily(IntPtr key)
        {
            switch (EVP_PKEY_get_base_id(key))
            {
                case EVP_PKEY_RSA_PSS: case EVP_PKEY_RSA: return 1; // RSA
                case EVP_PKEY_EC: return 3; // ECC
                case EVP_PKEY_DSA: return 2; // DSA
            }
            ERR_clear_error();
            if (PkeyIsA(key, "ML-KEM-512\0"u8) || PkeyIsA(key, "ML-KEM-768\0"u8) || PkeyIsA(key, "ML-KEM-1024\0"u8)) return 4;
            if (PkeyIsA(key, "ML-DSA-44\0"u8) || PkeyIsA(key, "ML-DSA-65\0"u8) || PkeyIsA(key, "ML-DSA-87\0"u8)) return 6;
            if (IsSlhDsaFamily(key)) return 5;
            return 0;
        }

        private static IntPtr GetBn(IntPtr pkey, ReadOnlySpan<byte> name, out bool ok)
        {
            IntPtr bn = IntPtr.Zero;
            fixed (byte* n = name) ok = EVP_PKEY_get_bn_param(pkey, n, &bn) != 0;
            return bn;
        }

        private static void ClearFree(IntPtr bn)
        {
            if (bn != IntPtr.Zero) BN_clear_free(bn);
        }

        private static void PutError(int lib, int reason) => ERR_put_error(lib, 0, reason, null, 0);

        // QuickRsaCheckCore over OpenSSL 3's key parameters: 1 consistent, 0 inconsistent (an error queued), -1 "let OpenSSL decide".
        private static int QuickRsaCheckEvp(IntPtr pkey, bool isPublic)
        {
            IntPtr n = IntPtr.Zero, e = IntPtr.Zero, d = IntPtr.Zero, p = IntPtr.Zero, q = IntPtr.Zero, dp = IntPtr.Zero, dq = IntPtr.Zero,
                inverseQ = IntPtr.Zero, ctx = IntPtr.Zero, x = IntPtr.Zero, y = IntPtr.Zero, pM1 = IntPtr.Zero, qM1 = IntPtr.Zero;
            int ret = 0;
            try
            {
                n = GetBn(pkey, "n\0"u8, out bool okN);
                e = GetBn(pkey, "e\0"u8, out bool okE);
                if (!okN || !okE)
                {
                    if (ERR_peek_error() == 0) PutError(ERR_LIB_RSA, RSA_R_VALUE_MISSING);
                    return 0;
                }
                d = GetBn(pkey, "d\0"u8, out bool okD);
                if (!okD)
                {
                    ERR_clear_error();
                    d = IntPtr.Zero;
                }
                if (n == IntPtr.Zero || e == IntPtr.Zero) { PutError(ERR_LIB_RSA, RSA_R_VALUE_MISSING); return 0; }
                if (BN_is_zero(n) != 0) { PutError(ERR_LIB_EVP, EVP_R_DECODE_ERROR); return 0; }
                if (BN_num_bits(n) > 16384 /* OPENSSL_RSA_MAX_MODULUS_BITS */) { PutError(ERR_LIB_RSA, RSA_R_MODULUS_TOO_LARGE); return 0; }
                if (BN_is_one(e) != 0 || BN_is_odd(e) == 0) { PutError(ERR_LIB_RSA, RSA_R_BAD_E_VALUE); return 0; }
                if (isPublic) return 1;

                // Multi-prime keys: OpenSSL's own check.
                IntPtr factor3 = GetBn(pkey, "rsa-factor3\0"u8, out bool hasFactor3);
                ClearFree(factor3);
                if (hasFactor3) return -1;
                ERR_clear_error();
                if (d == IntPtr.Zero) return -1;
                p = GetBn(pkey, "rsa-factor1\0"u8, out bool okP);
                if (okP) q = GetBn(pkey, "rsa-factor2\0"u8, out okP);
                if (!okP || p == IntPtr.Zero || q == IntPtr.Zero)
                {
                    ERR_clear_error();
                    return -1;
                }
                if (BN_cmp(d, n) >= 0) { PutError(ERR_LIB_EVP, EVP_R_DECODE_ERROR); return 0; }
                if ((ctx = BN_CTX_new()) == IntPtr.Zero || (x = BN_new()) == IntPtr.Zero || (y = BN_new()) == IntPtr.Zero
                    || (pM1 = BN_new()) == IntPtr.Zero || (qM1 = BN_new()) == IntPtr.Zero)
                    return 0;
                if (BN_mul(x, p, q, ctx) == 0) return 0;
                if (BN_cmp(x, n) != 0) { PutError(ERR_LIB_RSA, RSA_R_N_DOES_NOT_EQUAL_P_Q); return 0; }
                if (BN_sub(pM1, p, BN_value_one()) == 0 || BN_sub(qM1, q, BN_value_one()) == 0 || !Lcm(pM1, qM1, ctx, x)) return 0;
                if (BN_mod_mul(y, d, e, x, ctx) == 0) return 0;
                if (BN_is_one(y) == 0) { PutError(ERR_LIB_RSA, RSA_R_D_E_NOT_CONGRUENT_TO_1); return 0; }
                dp = GetBn(pkey, "rsa-exponent1\0"u8, out bool okCrt);
                if (okCrt) dq = GetBn(pkey, "rsa-exponent2\0"u8, out okCrt);
                if (okCrt) inverseQ = GetBn(pkey, "rsa-coefficient1\0"u8, out okCrt);
                if (okCrt && dp != IntPtr.Zero && dq != IntPtr.Zero && inverseQ != IntPtr.Zero)
                {
                    if (BN_div(IntPtr.Zero, x, d, pM1, ctx) == 0) return 0;
                    if (BN_cmp(x, dp) != 0) { PutError(ERR_LIB_RSA, RSA_R_DMP1_NOT_CONGRUENT_TO_D); return 0; }
                    if (BN_div(IntPtr.Zero, x, d, qM1, ctx) == 0) return 0;
                    if (BN_cmp(x, dq) != 0) { PutError(ERR_LIB_RSA, RSA_R_DMQ1_NOT_CONGRUENT_TO_D); return 0; }
                    if (BN_mod_inverse(x, q, p, ctx) == IntPtr.Zero) return 0;
                    if (BN_cmp(x, inverseQ) != 0) { PutError(ERR_LIB_RSA, RSA_R_IQMP_NOT_INVERSE_OF_Q); return 0; }
                }
                else
                {
                    ERR_clear_error();
                }
                ret = 1;
                return ret;
            }
            finally
            {
                ClearFree(n); ClearFree(e); ClearFree(d); ClearFree(p); ClearFree(q); ClearFree(dp); ClearFree(dq); ClearFree(inverseQ);
                ClearFree(x); ClearFree(y); ClearFree(pM1); ClearFree(qM1);
                if (ctx != IntPtr.Zero) BN_CTX_free(ctx);
            }
        }

        private static bool Lcm(IntPtr num1, IntPtr num2, IntPtr ctx, IntPtr result)
        {
            IntPtr mul = BN_new(), gcd = BN_new();
            bool ret = mul != IntPtr.Zero && gcd != IntPtr.Zero && BN_mul(mul, num1, num2, ctx) != 0 && BN_gcd(gcd, num1, num2, ctx) != 0
                && BN_div(result, IntPtr.Zero, mul, gcd, ctx) != 0;
            ClearFree(mul);
            ClearFree(gcd);
            return ret;
        }

        private static bool CheckKey(IntPtr key, int algId, bool isPublic)
        {
            if (algId != 0 && EVP_PKEY_get_base_id(key) != algId)
            {
                PutError(ERR_LIB_EVP, EVP_R_UNSUPPORTED_ALGORITHM);
                return false;
            }
            if (algId == 6 /* NID_rsaEncryption */)
            {
                int result = QuickRsaCheckEvp(key, isPublic);
                if (result == 0) return false;
                if (result == 1) return true;
                ERR_clear_error();
            }
            IntPtr ctx = EVP_PKEY_CTX_new(key, IntPtr.Zero);
            if (ctx == IntPtr.Zero) return false;
            int check = isPublic ? EVP_PKEY_public_check(ctx) : EVP_PKEY_check(ctx);
            EVP_PKEY_CTX_free(ctx);
            // -2: the key type has no check; accepted, as there.
            if (check == 1 || check == -2)
            {
                ERR_clear_error();
                return true;
            }
            return false;
        }

        [RuntimeExport("CryptoNative_DecodeSubjectPublicKeyInfo", Trigger + "CryptoNative_DecodeSubjectPublicKeyInfo")]
        public static IntPtr DecodeSubjectPublicKeyInfo(byte* buf, int len, int algId)
        {
            ERR_clear_error();
            if (buf == null || len <= 0) return IntPtr.Zero;
            IntPtr key = d2i_PUBKEY(IntPtr.Zero, &buf, len);
            if (key != IntPtr.Zero && !CheckKey(key, algId, true))
            {
                EVP_PKEY_free(key);
                key = IntPtr.Zero;
            }
            return key;
        }

        [RuntimeExport("CryptoNative_DecodePkcs8PrivateKey", Trigger + "CryptoNative_DecodePkcs8PrivateKey")]
        public static IntPtr DecodePkcs8PrivateKey(byte* buf, int len, int algId)
        {
            ERR_clear_error();
            if (buf == null || len <= 0) return IntPtr.Zero;
            IntPtr p8info = d2i_PKCS8_PRIV_KEY_INFO(IntPtr.Zero, &buf, len);
            if (p8info == IntPtr.Zero) return IntPtr.Zero;
            IntPtr key = EVP_PKCS82PKEY(p8info);
            PKCS8_PRIV_KEY_INFO_free(p8info);
            if (key != IntPtr.Zero && !CheckKey(key, algId, false))
            {
                EVP_PKEY_free(key);
                key = IntPtr.Zero;
            }
            return key;
        }

        [RuntimeExport("CryptoNative_GetPkcs8PrivateKeySize", Trigger + "CryptoNative_GetPkcs8PrivateKeySize")]
        public static int GetPkcs8PrivateKeySize(IntPtr pkey, int* p8size)
        {
            *p8size = 0;
            ERR_clear_error();
            IntPtr p8 = EVP_PKEY2PKCS8(pkey);
            if (p8 == IntPtr.Zero)
            {
                byte* file = null;
                int line = 0;
                ulong error = ERR_peek_error_line(&file, &line);
                // Anything but an allocation failure means "this key has no PKCS#8 form" (a public key): -2, queue cleared.
                if (ErrGetReason(error) != ERR_R_MALLOC_FAILURE)
                {
                    ERR_clear_error();
                    return -2;
                }
                ERR_clear_error();
                ERR_put_error((int)((error >> 23) & 0xFF), 0, ERR_R_MALLOC_FAILURE, file, line);
                return -1;
            }
            *p8size = i2d_PKCS8_PRIV_KEY_INFO(p8, null);
            PKCS8_PRIV_KEY_INFO_free(p8);
            return *p8size < 0 ? -1 : 1;
        }

        [RuntimeExport("CryptoNative_EncodePkcs8PrivateKey", Trigger + "CryptoNative_EncodePkcs8PrivateKey")]
        public static int EncodePkcs8PrivateKey(IntPtr pkey, byte* buf)
        {
            ERR_clear_error();
            IntPtr p8 = EVP_PKEY2PKCS8(pkey);
            if (p8 == IntPtr.Zero) return -1;
            int ret = i2d_PKCS8_PRIV_KEY_INFO(p8, &buf);
            PKCS8_PRIV_KEY_INFO_free(p8);
            return ret;
        }

        [RuntimeExport("CryptoNative_GetSubjectPublicKeyInfoSize", Trigger + "CryptoNative_GetSubjectPublicKeyInfoSize")]
        public static int GetSubjectPublicKeyInfoSize(IntPtr pkey)
        {
            ERR_clear_error();
            return i2d_PUBKEY(pkey, null);
        }

        [RuntimeExport("CryptoNative_EncodeSubjectPublicKeyInfo", Trigger + "CryptoNative_EncodeSubjectPublicKeyInfo")]
        public static int EncodeSubjectPublicKeyInfo(IntPtr pkey, byte* buf)
        {
            ERR_clear_error();
            return i2d_PUBKEY(pkey, &buf);
        }

        // OpenSSL ENGINEs are deprecated in OpenSSL 3 and gone from 4: csharpc does not load them. The framework reads haveEngine == 0 as
        // "engines are unavailable" and throws its own PlatformNotSupportedException, so this is an explicit refusal, not a silent one.
        [RuntimeExport("CryptoNative_LoadPrivateKeyFromEngine", Trigger + "CryptoNative_LoadPrivateKeyFromEngine")]
        public static IntPtr LoadPrivateKeyFromEngine(byte* engineName, byte* keyName, int* haveEngine)
            => throw new PlatformNotSupportedException("OpenSSL ENGINE keys (SafeEvpPKeyHandle.OpenPrivateKeyFromEngine) are not supported by csharpc yet.");

        [RuntimeExport("CryptoNative_LoadPublicKeyFromEngine", Trigger + "CryptoNative_LoadPublicKeyFromEngine")]
        public static IntPtr LoadPublicKeyFromEngine(byte* engineName, byte* keyName, int* haveEngine)
            => throw new PlatformNotSupportedException("OpenSSL ENGINE keys (SafeEvpPKeyHandle.OpenPublicKeyFromEngine) are not supported by csharpc yet.");

        [RuntimeExport("CryptoNative_LoadKeyFromProvider", Trigger + "CryptoNative_LoadKeyFromProvider")]
        public static IntPtr LoadKeyFromProvider(byte** providerNames, int providerNameCount, byte* keyUri, byte* propertyQuery, IntPtr* extraHandle, int* haveProvider)
        {
            ERR_clear_error();
            *haveProvider = 1;
            IntPtr libCtx = *extraHandle;
            IntPtr ret = IntPtr.Zero, store = IntPtr.Zero, firstPubKey = IntPtr.Zero;
            IntPtr* loadedProviders = null;
            int loadedProviderCount = 0;
            if (providerNameCount < 0) return IntPtr.Zero;
            if (libCtx == IntPtr.Zero)
            {
                libCtx = OSSL_LIB_CTX_new();
                if (libCtx == IntPtr.Zero) goto end;
                loadedProviders = (IntPtr*)NativeMemory.AllocZeroed((nuint)Math.Max(providerNameCount, 1), (nuint)sizeof(IntPtr));
                if (loadedProviders == null)
                {
                    OSSL_LIB_CTX_free(libCtx);
                    ERR_put_error(ERR_LIB_EVP, 0, ERR_R_MALLOC_FAILURE, null, 0);
                    libCtx = IntPtr.Zero;
                    goto end;
                }
                for (int i = 0; i < providerNameCount; i++)
                {
                    IntPtr loaded = OSSL_PROVIDER_load(libCtx, providerNames[i]);
                    if (loaded == IntPtr.Zero) goto end;
                    loadedProviders[loadedProviderCount++] = loaded;
                }
                *extraHandle = libCtx;
            }
            store = OSSL_STORE_open_ex(keyUri, libCtx, propertyQuery, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (store == IntPtr.Zero) goto end;
            while (ret == IntPtr.Zero && OSSL_STORE_eof(store) == 0)
            {
                IntPtr info = OSSL_STORE_load(store);
                if (info == IntPtr.Zero) continue;
                int type = OSSL_STORE_INFO_get_type(info);
                if (type == 4 /* OSSL_STORE_INFO_PKEY */)
                {
                    ret = OSSL_STORE_INFO_get1_PKEY(info);
                    OSSL_STORE_INFO_free(info);
                    break;
                }
                if (type == 3 /* OSSL_STORE_INFO_PUBKEY */ && firstPubKey == IntPtr.Zero)
                {
                    firstPubKey = info;
                    continue;
                }
                OSSL_STORE_INFO_free(info);
            }
            if (ret == IntPtr.Zero && firstPubKey != IntPtr.Zero) ret = OSSL_STORE_INFO_get1_PUBKEY(firstPubKey);
            if (ret == IntPtr.Zero)
            {
                ERR_clear_error();
                ERR_put_error(ERR_LIB_NONE, 0, EVP_R_NO_KEY_SET, null, 0);
            }
        end:
            if (firstPubKey != IntPtr.Zero) OSSL_STORE_INFO_free(firstPubKey);
            if (store != IntPtr.Zero) OSSL_STORE_close(store);
            if (loadedProviders != null)
            {
                // Failed before the context was handed out: unload what was loaded.
                if (*extraHandle == IntPtr.Zero)
                {
                    for (int i = loadedProviderCount - 1; i >= 0; i--) OSSL_PROVIDER_unload(loadedProviders[i]);
                    if (libCtx != IntPtr.Zero) OSSL_LIB_CTX_free(libCtx);
                }
                NativeMemory.Free(loadedProviders);
            }
            return ret;
        }

        internal static IntPtr EvpPKeyCtxCreateFromPKey(IntPtr pkey, IntPtr extraHandle) => EVP_PKEY_CTX_new_from_pkey(extraHandle, pkey, null);

        [RuntimeExport("CryptoNative_EvpPKeyFromData", Trigger + "CryptoNative_EvpPKeyFromData")]
        public static IntPtr EvpPKeyFromData(byte* algorithmName, byte* key, int keyLength, int privateKey)
        {
            ERR_clear_error();
            if (keyLength < 0) return IntPtr.Zero;
            IntPtr pkey = IntPtr.Zero;
            IntPtr ctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, algorithmName, null);
            if (ctx == IntPtr.Zero) return IntPtr.Zero;
            if (EVP_PKEY_fromdata_init(ctx) == 1)
            {
                OsslParam* parameters = stackalloc OsslParam[2];
                fixed (byte* pub = "pub\0"u8) fixed (byte* priv = "priv\0"u8)
                {
                    parameters[0] = OctetParam(privateKey == 0 ? pub : priv, key, (nuint)keyLength);
                    parameters[1] = EndParam();
                    if (EVP_PKEY_fromdata(ctx, &pkey, privateKey == 0 ? 134 /* EVP_PKEY_PUBLIC_KEY */ : 135 /* EVP_PKEY_KEYPAIR */, parameters) != 1)
                    {
                        if (pkey != IntPtr.Zero) EVP_PKEY_free(pkey);
                        pkey = IntPtr.Zero;
                    }
                }
            }
            EVP_PKEY_CTX_free(ctx);
            return pkey;
        }

        internal static int EvpPKeyHasKeyOctetStringParam(IntPtr pKey, byte* name)
        {
            ERR_clear_error();
            nuint outLength = 0;
            return EVP_PKEY_get_octet_string_param(pKey, name, null, 0, &outLength) == 1 && outLength > 0 ? 1 : 0;
        }

        internal static int EvpPKeyGetKeyOctetStringParam(IntPtr pKey, byte* name, byte* destination, int destinationLength)
        {
            ERR_clear_error();
            if (destinationLength < 0) return 0;
            nuint outLength = 0;
            if (EVP_PKEY_get_octet_string_param(pKey, name, null, 0, &outLength) != 1) return -1;
            // OpenSSL refuses to write more than destinationLength.
            if (EVP_PKEY_get_octet_string_param(pKey, name, destination, (nuint)destinationLength, &outLength) != 1) return 0;
            return outLength != (nuint)destinationLength ? -2 : 1;
        }
    }
}
#endif
