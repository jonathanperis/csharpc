// The shim's DSA over libcrypto's DSA objects: pal_dsa.c and pal_evp_pkey_dsa.c (dotnet/runtime, .NET 11 GA), function by function.
// See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int DSA_up_ref(IntPtr dsa);
        [DllImport(LibCrypto)] private static extern void DSA_free(IntPtr dsa);
        [DllImport(LibCrypto)] private static extern IntPtr DSA_new();
        [DllImport(LibCrypto)] private static extern int DSA_generate_parameters_ex(IntPtr dsa, int bits, byte* seed, int seedLen, int* counterRet, ulong* hRet, IntPtr cb);
        [DllImport(LibCrypto)] private static extern int DSA_generate_key(IntPtr dsa);
        [DllImport(LibCrypto)] private static extern int DSA_size(IntPtr dsa);
        [DllImport(LibCrypto)] private static extern void DSA_get0_pqg(IntPtr dsa, IntPtr* p, IntPtr* q, IntPtr* g);
        [DllImport(LibCrypto)] private static extern void DSA_get0_key(IntPtr dsa, IntPtr* pubKey, IntPtr* privKey);
        [DllImport(LibCrypto)] private static extern int DSA_set0_pqg(IntPtr dsa, IntPtr p, IntPtr q, IntPtr g);
        [DllImport(LibCrypto)] private static extern int DSA_set0_key(IntPtr dsa, IntPtr pubKey, IntPtr privKey);
        [DllImport(LibCrypto)] private static extern IntPtr DSA_get_method(IntPtr dsa);
        [DllImport(LibCrypto)] private static extern IntPtr DSA_OpenSSL();
        [DllImport(LibCrypto)] private static extern int DSA_sign(int type, byte* dgst, int dlen, byte* sig, uint* siglen, IntPtr dsa);
        [DllImport(LibCrypto)] private static extern int DSA_verify(int type, byte* dgst, int dgstLen, byte* sigbuf, int siglen, IntPtr dsa);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_get1_DSA(IntPtr pkey);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_set1_DSA(IntPtr pkey, IntPtr key);

        private static int BnNumBytes(IntPtr bn) => (BN_num_bits(bn) + 7) / 8;

        [RuntimeExport("CryptoNative_DsaUpRef", Trigger + "CryptoNative_DsaUpRef")]
        public static int DsaUpRef(IntPtr dsa) => DSA_up_ref(dsa);

        [RuntimeExport("CryptoNative_DsaDestroy", Trigger + "CryptoNative_DsaDestroy")]
        public static void DsaDestroy(IntPtr dsa)
        {
            if (dsa != IntPtr.Zero) DSA_free(dsa);
        }

        [RuntimeExport("CryptoNative_DsaGenerateKey", Trigger + "CryptoNative_DsaGenerateKey")]
        public static int DsaGenerateKey(IntPtr* dsa, int bits)
        {
            if (dsa == null) return 0;
            ERR_clear_error();
            *dsa = DSA_new();
            if (*dsa == IntPtr.Zero) return 0;
            if (DSA_generate_parameters_ex(*dsa, bits, null, 0, null, null, IntPtr.Zero) == 0 || DSA_generate_key(*dsa) == 0)
            {
                DSA_free(*dsa);
                *dsa = IntPtr.Zero;
                return 0;
            }
            return 1;
        }

        [RuntimeExport("CryptoNative_DsaSizeSignature", Trigger + "CryptoNative_DsaSizeSignature")]
        public static int DsaSizeSignature(IntPtr dsa) => DSA_size(dsa);

        [RuntimeExport("CryptoNative_DsaSizeP", Trigger + "CryptoNative_DsaSizeP")]
        public static int DsaSizeP(IntPtr dsa)
        {
            if (dsa == IntPtr.Zero) return -1;
            IntPtr p;
            DSA_get0_pqg(dsa, &p, null, null);
            return p != IntPtr.Zero ? BnNumBytes(p) : -1;
        }

        [RuntimeExport("CryptoNative_DsaSizeQ", Trigger + "CryptoNative_DsaSizeQ")]
        public static int DsaSizeQ(IntPtr dsa)
        {
            if (dsa == IntPtr.Zero) return -1;
            IntPtr q;
            DSA_get0_pqg(dsa, null, &q, null);
            return q != IntPtr.Zero ? BnNumBytes(q) : -1;
        }

        // The framework sizes refsignature with DsaSizeSignature first.
        [RuntimeExport("CryptoNative_DsaSign", Trigger + "CryptoNative_DsaSign")]
        public static int DsaSign(IntPtr dsa, byte* hash, int hashLength, byte* refsignature, int* outSignatureLength)
        {
            if (outSignatureLength == null || dsa == IntPtr.Zero || hashLength < 0) return 0;
            ERR_clear_error();
            if (DSA_get_method(dsa) == DSA_OpenSSL())
            {
                IntPtr privKey;
                DSA_get0_key(dsa, null, &privKey);
                // A public key only: OpenSSL would crash in DSA_do_sign, so the shim reports DSA_R_MISSING_PARAMETERS itself.
                if (privKey == IntPtr.Zero)
                {
                    *outSignatureLength = 0;
                    ERR_put_error(10 /* ERR_LIB_DSA */, 0, 101 /* DSA_R_MISSING_PARAMETERS */, null, 0);
                    return 0;
                }
            }
            uint unsignedSigLen = 0;
            if (DSA_sign(0, hash, hashLength, refsignature, &unsignedSigLen, dsa) == 0)
            {
                *outSignatureLength = 0;
                return 0;
            }
            *outSignatureLength = (int)unsignedSigLen;
            return 1;
        }

        [RuntimeExport("CryptoNative_DsaVerify", Trigger + "CryptoNative_DsaVerify")]
        public static int DsaVerify(IntPtr dsa, byte* hash, int hashLength, byte* signature, int signatureLength)
        {
            int success = DSA_verify(0, hash, hashLength, signature, signatureLength, dsa);
            if (success != 1)
            {
                if (success == -1) ERR_clear_error();
                return 0;
            }
            return 1;
        }

        [RuntimeExport("CryptoNative_GetDsaParameters", Trigger + "CryptoNative_GetDsaParameters")]
        public static int GetDsaParameters(IntPtr dsa, IntPtr* p, int* pLength, IntPtr* q, int* qLength, IntPtr* g, int* gLength,
            IntPtr* y, int* yLength, IntPtr* x, int* xLength)
        {
            DSA_get0_pqg(dsa, p, q, g);
            *pLength = BnNumBytes(*p);
            *qLength = BnNumBytes(*q);
            *gLength = BnNumBytes(*g);
            DSA_get0_key(dsa, y, x);
            *yLength = BnNumBytes(*y);
            *xLength = *x == IntPtr.Zero ? 0 : BnNumBytes(*x);
            return 1;
        }

        private static IntPtr MakeBignum(byte* buffer, int bufferLength) =>
            buffer != null && bufferLength > 0 ? BN_bin2bn(buffer, bufferLength, IntPtr.Zero) : IntPtr.Zero;

        [RuntimeExport("CryptoNative_DsaKeyCreateByExplicitParameters", Trigger + "CryptoNative_DsaKeyCreateByExplicitParameters")]
        public static int DsaKeyCreateByExplicitParameters(IntPtr* outDsa, byte* p, int pLength, byte* q, int qLength, byte* g, int gLength,
            byte* y, int yLength, byte* x, int xLength)
        {
            if (outDsa == null) return 0;
            ERR_clear_error();
            *outDsa = DSA_new();
            if (*outDsa == IntPtr.Zero) return 0;
            IntPtr dsa = *outDsa;
            IntPtr bnP = MakeBignum(p, pLength), bnQ = MakeBignum(q, qLength), bnG = MakeBignum(g, gLength);
            if (DSA_set0_pqg(dsa, bnP, bnQ, bnG) == 0)
            {
                BN_free(bnP); BN_free(bnQ); BN_free(bnG);
                return 0;
            }
            IntPtr bnY = MakeBignum(y, yLength), bnX = MakeBignum(x, xLength);
            if (DSA_set0_key(dsa, bnY, bnX) == 0)
            {
                BN_free(bnY); BN_free(bnX);
                return 0;
            }
            return 1;
        }

        [RuntimeExport("CryptoNative_EvpPkeyGetDsa", Trigger + "CryptoNative_EvpPkeyGetDsa")]
        public static IntPtr EvpPkeyGetDsa(IntPtr pkey)
        {
            ERR_clear_error();
            return EVP_PKEY_get1_DSA(pkey);
        }

        [RuntimeExport("CryptoNative_EvpPkeySetDsa", Trigger + "CryptoNative_EvpPkeySetDsa")]
        public static int EvpPkeySetDsa(IntPtr pkey, IntPtr dsa)
        {
            ERR_clear_error();
            return EVP_PKEY_set1_DSA(pkey, dsa);
        }
    }
}
#endif
