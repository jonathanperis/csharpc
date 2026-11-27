// The shim's ML-DSA and SLH-DSA over EVP_PKEY, with the message-signing helpers they share: pal_evp_pkey_raw_signverify.c,
// pal_evp_pkey_ml_dsa.c and pal_evp_pkey_slh_dsa.c (dotnet/runtime, .NET 11 GA), function by function. EVP_PKEY_sign_message_init and
// EVP_PKEY_verify_message_init are OpenSSL 3.5's: looked up by name, and without them these algorithms are unavailable, as the shim's
// API_EXISTS makes them (MLDsa.IsSupported and SlhDsa.IsSupported are then false, as under .NET on the same OpenSSL). See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_set_params(IntPtr ctx, OsslParam* parameters);

        private static IntPtr s_signMessageInit, s_verifyMessageInit;
        private static int s_messageApiLooked;

        // API_EXISTS(EVP_PKEY_sign_message_init) && API_EXISTS(EVP_PKEY_verify_message_init).
        private static bool HaveMessageApi()
        {
            if (s_messageApiLooked == 0)
            {
                s_signMessageInit = OptionalExport("EVP_PKEY_sign_message_init");
                s_verifyMessageInit = OptionalExport("EVP_PKEY_verify_message_init");
                s_messageApiLooked = 1;
            }
            return s_signMessageInit != IntPtr.Zero && s_verifyMessageInit != IntPtr.Zero;
        }

        private static int SignMessageInit(IntPtr ctx, OsslParam* parameters) =>
            ((delegate* unmanaged<IntPtr, IntPtr, OsslParam*, int>)s_signMessageInit)(ctx, IntPtr.Zero, parameters);

        private static int VerifyMessageInit(IntPtr ctx, OsslParam* parameters) =>
            ((delegate* unmanaged<IntPtr, IntPtr, OsslParam*, int>)s_verifyMessageInit)(ctx, IntPtr.Zero, parameters);

        // Sign into exactly destinationLen bytes: 1 signed, 0 failed, -1 unavailable or the wrong length.
        private static int SignWithParams(IntPtr pkey, IntPtr extraHandle, OsslParam* parameters, byte* msg, int msgLen, byte* destination, int destinationLen)
        {
            if (!HaveMessageApi()) return -1;
            ERR_clear_error();
            if (msgLen < 0 || destinationLen < 0) return -1;
            int ret = -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            if (ctx != IntPtr.Zero && SignMessageInit(ctx, parameters) > 0)
            {
                nuint dstLen = (nuint)destinationLen;
                if (EVP_PKEY_sign(ctx, destination, &dstLen, msg, (nuint)msgLen) == 1) ret = dstLen != (nuint)destinationLen ? -1 : 1;
                else ret = 0;
            }
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        private static int VerifyWithParams(IntPtr pkey, IntPtr extraHandle, OsslParam* parameters, byte* msg, int msgLen, byte* sig, int sigLen)
        {
            if (!HaveMessageApi()) return -1;
            ERR_clear_error();
            if (msgLen < 0 || sigLen < 0) return -1;
            int ret = -1;
            IntPtr ctx = EvpPKeyCtxCreateFromPKey(pkey, extraHandle);
            if (ctx != IntPtr.Zero && VerifyMessageInit(ctx, parameters) > 0)
                ret = EVP_PKEY_verify(ctx, sig, (nuint)sigLen, msg, (nuint)msgLen) == 1 ? 1 : 0;
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return ret;
        }

        private static int SignPure(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* context, int contextLen, byte* destination, int destinationLen)
        {
            if (contextLen < 0) return -1;
            OsslParam* p = stackalloc OsslParam[2];
            p[0] = EndParam(); p[1] = EndParam();
            fixed (byte* name = "context-string\0"u8)
            {
                if (context != null) p[0] = OctetParam(name, context, (nuint)contextLen);
                return SignWithParams(pkey, extraHandle, p, msg, msgLen, destination, destinationLen);
            }
        }

        private static int VerifyPure(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* context, int contextLen, byte* sig, int sigLen)
        {
            if (contextLen < 0) return -1;
            OsslParam* p = stackalloc OsslParam[2];
            p[0] = EndParam(); p[1] = EndParam();
            fixed (byte* name = "context-string\0"u8)
            {
                if (context != null) p[0] = OctetParam(name, context, (nuint)contextLen);
                return VerifyWithParams(pkey, extraHandle, p, msg, msgLen, sig, sigLen);
            }
        }

        private static int SignOrVerifyIntParam(ReadOnlySpan<byte> name, int value, bool sign, IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* buffer, int bufferLen)
        {
            OsslParam* p = stackalloc OsslParam[2];
            fixed (byte* n = name)
            {
                p[0] = Int32Param(n, &value);
                p[1] = EndParam();
                return sign ? SignWithParams(pkey, extraHandle, p, msg, msgLen, buffer, bufferLen) : VerifyWithParams(pkey, extraHandle, p, msg, msgLen, buffer, bufferLen);
            }
        }

        private static IntPtr GeneratePqcKey(byte* keyType, ReadOnlySpan<byte> seedName, byte* seed, int seedLen)
        {
            if (!HaveMessageApi()) return IntPtr.Zero;
            ERR_clear_error();
            IntPtr pctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, keyType, null);
            if (pctx == IntPtr.Zero) return IntPtr.Zero;
            IntPtr pkey = IntPtr.Zero;
            if (EVP_PKEY_keygen_init(pctx) > 0)
            {
                bool ok = true;
                if (seed != null)
                {
                    OsslParam* p = stackalloc OsslParam[2];
                    fixed (byte* n = seedName)
                    {
                        p[0] = OctetParam(n, seed, (nuint)seedLen);
                        p[1] = EndParam();
                        ok = EVP_PKEY_CTX_set_params(pctx, p) > 0;
                    }
                }
                if (ok && EVP_PKEY_keygen(pctx, &pkey) != 1 && pkey != IntPtr.Zero)
                {
                    EVP_PKEY_free(pkey);
                    pkey = IntPtr.Zero;
                }
            }
            EVP_PKEY_CTX_free(pctx);
            return pkey;
        }

        private static int ExportOctet(IntPtr pkey, ReadOnlySpan<byte> name, byte* destination, int destinationLength)
        {
            fixed (byte* n = name) return EvpPKeyGetKeyOctetStringParam(pkey, n, destination, destinationLength);
        }

        // ---- ML-DSA ----------------------------------------------------------------------------------------------------
        [RuntimeExport("CryptoNative_MLDsaGetPalId", Trigger + "CryptoNative_MLDsaGetPalId")]
        public static int MLDsaGetPalId(IntPtr pKey, int* mldsaId, int* hasSeed, int* hasSecretKey)
        {
            ERR_clear_error();
            if (PkeyIsA(pKey, "ML-DSA-44\0"u8)) *mldsaId = 1;
            else if (PkeyIsA(pKey, "ML-DSA-65\0"u8)) *mldsaId = 2;
            else if (PkeyIsA(pKey, "ML-DSA-87\0"u8)) *mldsaId = 3;
            else
            {
                *mldsaId = 0;
                *hasSeed = 0;
                *hasSecretKey = 0;
                return 1;
            }
            fixed (byte* seed = "seed\0"u8) *hasSeed = EvpPKeyHasKeyOctetStringParam(pKey, seed);
            fixed (byte* priv = "priv\0"u8) *hasSecretKey = EvpPKeyHasKeyOctetStringParam(pKey, priv);
            return 1;
        }

        [RuntimeExport("CryptoNative_MLDsaGenerateKey", Trigger + "CryptoNative_MLDsaGenerateKey")]
        public static IntPtr MLDsaGenerateKey(byte* keyType, byte* seed, int seedLen)
        {
            if (seed != null && seedLen != 32) return IntPtr.Zero;
            return GeneratePqcKey(keyType, "seed\0"u8, seed, seedLen);
        }

        [RuntimeExport("CryptoNative_MLDsaSignPure", Trigger + "CryptoNative_MLDsaSignPure")]
        public static int MLDsaSignPure(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* context, int contextLen, byte* destination, int destinationLen)
            => SignPure(pkey, extraHandle, msg, msgLen, context, contextLen, destination, destinationLen);

        [RuntimeExport("CryptoNative_MLDsaVerifyPure", Trigger + "CryptoNative_MLDsaVerifyPure")]
        public static int MLDsaVerifyPure(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* context, int contextLen, byte* sig, int sigLen)
            => VerifyPure(pkey, extraHandle, msg, msgLen, context, contextLen, sig, sigLen);

        [RuntimeExport("CryptoNative_MLDsaSignPreEncoded", Trigger + "CryptoNative_MLDsaSignPreEncoded")]
        public static int MLDsaSignPreEncoded(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* destination, int destinationLen)
            => SignOrVerifyIntParam("message-encoding\0"u8, 0, true, pkey, extraHandle, msg, msgLen, destination, destinationLen);

        [RuntimeExport("CryptoNative_MLDsaVerifyPreEncoded", Trigger + "CryptoNative_MLDsaVerifyPreEncoded")]
        public static int MLDsaVerifyPreEncoded(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* sig, int sigLen)
            => SignOrVerifyIntParam("message-encoding\0"u8, 0, false, pkey, extraHandle, msg, msgLen, sig, sigLen);

        [RuntimeExport("CryptoNative_MLDsaSignExternalMu", Trigger + "CryptoNative_MLDsaSignExternalMu")]
        public static int MLDsaSignExternalMu(IntPtr pKey, IntPtr extraHandle, byte* mu, int muLen, byte* destination, int destinationLen)
            => SignOrVerifyIntParam("mu\0"u8, 1, true, pKey, extraHandle, mu, muLen, destination, destinationLen);

        [RuntimeExport("CryptoNative_MLDsaVerifyExternalMu", Trigger + "CryptoNative_MLDsaVerifyExternalMu")]
        public static int MLDsaVerifyExternalMu(IntPtr pKey, IntPtr extraHandle, byte* mu, int muLen, byte* sig, int sigLen)
            => SignOrVerifyIntParam("mu\0"u8, 1, false, pKey, extraHandle, mu, muLen, sig, sigLen);

        [RuntimeExport("CryptoNative_MLDsaExportSecretKey", Trigger + "CryptoNative_MLDsaExportSecretKey")]
        public static int MLDsaExportSecretKey(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "priv\0"u8, destination, destinationLength);

        [RuntimeExport("CryptoNative_MLDsaExportSeed", Trigger + "CryptoNative_MLDsaExportSeed")]
        public static int MLDsaExportSeed(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "seed\0"u8, destination, destinationLength);

        [RuntimeExport("CryptoNative_MLDsaExportPublicKey", Trigger + "CryptoNative_MLDsaExportPublicKey")]
        public static int MLDsaExportPublicKey(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "pub\0"u8, destination, destinationLength);

        // ---- SLH-DSA ---------------------------------------------------------------------------------------------------
        [RuntimeExport("CryptoNative_SlhDsaGenerateKey", Trigger + "CryptoNative_SlhDsaGenerateKey")]
        public static IntPtr SlhDsaGenerateKey(byte* keyType) => GeneratePqcKey(keyType, default, null, 0);

        [RuntimeExport("CryptoNative_SlhDsaSignPure", Trigger + "CryptoNative_SlhDsaSignPure")]
        public static int SlhDsaSignPure(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* context, int contextLen, byte* destination, int destinationLen)
            => SignPure(pkey, extraHandle, msg, msgLen, context, contextLen, destination, destinationLen);

        [RuntimeExport("CryptoNative_SlhDsaVerifyPure", Trigger + "CryptoNative_SlhDsaVerifyPure")]
        public static int SlhDsaVerifyPure(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* context, int contextLen, byte* sig, int sigLen)
            => VerifyPure(pkey, extraHandle, msg, msgLen, context, contextLen, sig, sigLen);

        [RuntimeExport("CryptoNative_SlhDsaSignPreEncoded", Trigger + "CryptoNative_SlhDsaSignPreEncoded")]
        public static int SlhDsaSignPreEncoded(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* destination, int destinationLen)
            => SignOrVerifyIntParam("message-encoding\0"u8, 0, true, pkey, extraHandle, msg, msgLen, destination, destinationLen);

        [RuntimeExport("CryptoNative_SlhDsaVerifyPreEncoded", Trigger + "CryptoNative_SlhDsaVerifyPreEncoded")]
        public static int SlhDsaVerifyPreEncoded(IntPtr pkey, IntPtr extraHandle, byte* msg, int msgLen, byte* sig, int sigLen)
            => SignOrVerifyIntParam("message-encoding\0"u8, 0, false, pkey, extraHandle, msg, msgLen, sig, sigLen);

        [RuntimeExport("CryptoNative_SlhDsaExportSecretKey", Trigger + "CryptoNative_SlhDsaExportSecretKey")]
        public static int SlhDsaExportSecretKey(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "priv\0"u8, destination, destinationLength);

        [RuntimeExport("CryptoNative_SlhDsaExportPublicKey", Trigger + "CryptoNative_SlhDsaExportPublicKey")]
        public static int SlhDsaExportPublicKey(IntPtr pKey, byte* destination, int destinationLength) => ExportOctet(pKey, "pub\0"u8, destination, destinationLength);

        private static readonly string[] s_slhDsaNames =
        [
            "SLH-DSA-SHA2-128s", "SLH-DSA-SHAKE-128s", "SLH-DSA-SHA2-128f", "SLH-DSA-SHAKE-128f", "SLH-DSA-SHA2-192s", "SLH-DSA-SHAKE-192s",
            "SLH-DSA-SHA2-192f", "SLH-DSA-SHAKE-192f", "SLH-DSA-SHA2-256s", "SLH-DSA-SHAKE-256s", "SLH-DSA-SHA2-256f", "SLH-DSA-SHAKE-256f",
        ];

        [RuntimeExport("CryptoNative_SlhDsaGetPalId", Trigger + "CryptoNative_SlhDsaGetPalId")]
        public static int SlhDsaGetPalId(IntPtr pKey, int* slhDsaTypeId)
        {
            ERR_clear_error();
            *slhDsaTypeId = 0;
            byte* name = stackalloc byte[24];
            for (int i = 0; i < s_slhDsaNames.Length; i++)
            {
                string s = s_slhDsaNames[i];
                for (int j = 0; j < s.Length; j++) name[j] = (byte)s[j];
                name[s.Length] = 0;
                if (EVP_PKEY_is_a(pKey, name) != 0)
                {
                    *slhDsaTypeId = i + 1;
                    break;
                }
            }
            return 1;
        }

        internal static bool IsSlhDsaFamily(IntPtr pKey)
        {
            int id = 0;
            return SlhDsaGetPalId(pKey, &id) != 0 && id != 0;
        }
    }
}
#endif
