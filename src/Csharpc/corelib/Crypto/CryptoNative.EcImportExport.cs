// The shim's EC key import and export over EVP_PKEY: pal_ecc_import_export.c (dotnet/runtime, .NET 11 GA), function by function: OpenSSL 3's
// provider path, and for keys without a provider (an EC_KEY wrapped by EVP_PKEY_set1_EC_KEY) the shim's legacy EC_KEY path. See CryptoNative.cs.
// Not ported: the legacy export of an explicit curve of a provider-less key (explicit diagnostic in EvpPKeyGetEcCurveParameters).
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_get_utf8_string_param(IntPtr pkey, byte* keyName, byte* str, nuint maxBufSize, nuint* outLen);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_get0_provider(IntPtr pkey);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_todata(IntPtr pkey, int selection, IntPtr* parameters);
        [DllImport(LibCrypto)] private static extern void OSSL_PARAM_free(IntPtr parameters);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_PARAM_locate_const(IntPtr parameters, byte* key);
        [DllImport(LibCrypto)] private static extern int OSSL_PARAM_get_octet_string_ptr(IntPtr p, void** val, nuint* usedLen);
        [DllImport(LibCrypto)] private static extern IntPtr EC_GROUP_new_by_curve_name(int nid);
        [DllImport(LibCrypto)] private static extern IntPtr EC_GROUP_new_curve_GFp(IntPtr p, IntPtr a, IntPtr b, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr EC_GROUP_new_curve_GF2m(IntPtr p, IntPtr a, IntPtr b, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern void EC_GROUP_free(IntPtr group);
        [DllImport(LibCrypto)] private static extern int EC_GROUP_get_degree(IntPtr group);
        [DllImport(LibCrypto)] private static extern IntPtr EC_POINT_new(IntPtr group);
        [DllImport(LibCrypto)] private static extern void EC_POINT_free(IntPtr point);
        [DllImport(LibCrypto)] private static extern int EC_POINT_oct2point(IntPtr group, IntPtr p, byte* buf, nuint len, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern nuint EC_POINT_point2oct(IntPtr group, IntPtr p, int form, byte* buf, nuint len, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EC_POINT_get_affine_coordinates(IntPtr group, IntPtr p, IntPtr x, IntPtr y, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EC_POINT_set_affine_coordinates(IntPtr group, IntPtr p, IntPtr x, IntPtr y, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int EC_POINT_mul(IntPtr group, IntPtr r, IntPtr n, IntPtr q, IntPtr m, IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr BN_bin2bn(byte* s, int len, IntPtr ret);
        [DllImport(LibCrypto)] private static extern void BN_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern IntPtr EVP_PKEY_get1_EC_KEY(IntPtr pkey);
        [DllImport(LibCrypto)] private static extern void EC_KEY_free(IntPtr key);
        [DllImport(LibCrypto)] private static extern IntPtr EC_KEY_get0_group(IntPtr key);

        private static bool GetUtf8Param(IntPtr pkey, ReadOnlySpan<byte> name, byte* buffer, int size)
        {
            new Span<byte>(buffer, size).Clear();
            fixed (byte* n = name) return EVP_PKEY_get_utf8_string_param(pkey, n, buffer, (nuint)size, null) != 0;
        }

        private static bool CStringEquals(byte* s, ReadOnlySpan<byte> nulTerminated)
        {
            for (int i = 0; i < nulTerminated.Length; i++)
                if (s[i] != nulTerminated[i]) return false;
            return true;
        }

        [RuntimeExport("CryptoNative_EvpPKeyGetEcGroupNid", Trigger + "CryptoNative_EvpPKeyGetEcGroupNid")]
        public static int EvpPKeyGetEcGroupNid(IntPtr pkey, int* nidName)
        {
            if (nidName == null) return 0;
            *nidName = 0;
            if (pkey == IntPtr.Zero || EVP_PKEY_get_base_id(pkey) != EVP_PKEY_EC) return 0;
            byte* curveName = stackalloc byte[80];
            if (!GetUtf8Param(pkey, "group\0"u8, curveName, 80)) return 0;
            *nidName = OBJ_txt2nid(curveName);
            return 1;
        }

        [RuntimeExport("CryptoNative_EvpPKeyEcHasExplicitEncoding", Trigger + "CryptoNative_EvpPKeyEcHasExplicitEncoding")]
        public static int EvpPKeyEcHasExplicitEncoding(IntPtr pkey)
        {
            if (pkey == IntPtr.Zero || EVP_PKEY_get_base_id(pkey) != EVP_PKEY_EC) return -1;
            byte* encoding = stackalloc byte[32];
            if (!GetUtf8Param(pkey, "encoding\0"u8, encoding, 32)) return 0;
            return CStringEquals(encoding, "explicit\0"u8) ? 1 : 0;
        }

        [RuntimeExport("CryptoNative_EvpPKeyGetEcKeySize", Trigger + "CryptoNative_EvpPKeyGetEcKeySize")]
        public static int EvpPKeyGetEcKeySize(IntPtr pkey)
        {
            if (pkey == IntPtr.Zero || EVP_PKEY_get_base_id(pkey) != EVP_PKEY_EC) return 0;
            if (EVP_PKEY_get0_provider(pkey) != IntPtr.Zero)
            {
                int nid = 0;
                if (EvpPKeyGetEcGroupNid(pkey, &nid) != 0 && nid != 0)
                {
                    IntPtr group = EC_GROUP_new_by_curve_name(nid);
                    if (group != IntPtr.Zero)
                    {
                        int degree = EC_GROUP_get_degree(group);
                        EC_GROUP_free(group);
                        return degree;
                    }
                }
                IntPtr p = GetBn(pkey, "p\0"u8, out bool okP);
                if (!okP || p == IntPtr.Zero) return 0;
                int bits = BN_num_bits(p);
                byte* fieldType = stackalloc byte[32];
                if (GetUtf8Param(pkey, "field-type\0"u8, fieldType, 32) && CStringEquals(fieldType, "characteristic-two-field\0"u8))
                    bits = bits > 0 ? bits - 1 : 0;
                BN_free(p);
                return bits;
            }
            IntPtr ecKey = EVP_PKEY_get1_EC_KEY(pkey);
            if (ecKey == IntPtr.Zero) return 0;
            IntPtr legacyGroup = EC_KEY_get0_group(ecKey);
            int legacyDegree = legacyGroup != IntPtr.Zero ? EC_GROUP_get_degree(legacyGroup) : 0;
            EC_KEY_free(ecKey);
            return legacyDegree;
        }

        // 1: exported; 2: the key has no provider (take the legacy path); 0: failed; -1: no private key.
        private static int EvpPKeyGetEcKeyParametersProvider(IntPtr pkey, int includePrivate, IntPtr* qx, int* cbQx, IntPtr* qy, int* cbQy, IntPtr* d, int* cbD)
        {
            *cbQx = *cbQy = *cbD = 0;
            *qx = *qy = *d = IntPtr.Zero;
            if (EVP_PKEY_get0_provider(pkey) == IntPtr.Zero) return 2;
            int rc = 0;
            IntPtr xBn = IntPtr.Zero, yBn = IntPtr.Zero, dBn = IntPtr.Zero, ecP = IntPtr.Zero, ecA = IntPtr.Zero, ecB = IntPtr.Zero;
            byte* pubKeyBuf = null;
            byte* pubKeyData = null;
            nuint pubKeyLen = 0;
            IntPtr exported = IntPtr.Zero, group = IntPtr.Zero, point = IntPtr.Zero;
            byte* curveName = stackalloc byte[80];
            fixed (byte* pubName = "pub\0"u8)
            {
                if (EVP_PKEY_get_base_id(pkey) != EVP_PKEY_EC) goto error;
                ERR_clear_error();
                if (EVP_PKEY_get_octet_string_param(pkey, pubName, null, 0, &pubKeyLen) != 0)
                {
                    if (pubKeyLen == 0) goto error;
                    pubKeyBuf = (byte*)OpensslZalloc(pubKeyLen);
                    if (pubKeyBuf == null) goto error;
                    if (EVP_PKEY_get_octet_string_param(pkey, pubName, pubKeyBuf, pubKeyLen, &pubKeyLen) == 0) goto error;
                    pubKeyData = pubKeyBuf;
                }
                else
                {
                    ERR_clear_error();
                    if (EVP_PKEY_todata(pkey, 134 /* EVP_PKEY_PUBLIC_KEY */, &exported) == 0 || exported == IntPtr.Zero) goto error;
                    IntPtr publicKeyParameter = OSSL_PARAM_locate_const(exported, pubName);
                    void* publicKeyValue = null;
                    if (publicKeyParameter == IntPtr.Zero || OSSL_PARAM_get_octet_string_ptr(publicKeyParameter, &publicKeyValue, &pubKeyLen) == 0
                        || publicKeyValue == null || pubKeyLen == 0)
                        goto error;
                    pubKeyData = (byte*)publicKeyValue;
                }
            }
            if (GetUtf8Param(pkey, "group\0"u8, curveName, 80))
            {
                int nid = OBJ_txt2nid(curveName);
                if (nid != 0) group = EC_GROUP_new_by_curve_name(nid);
            }
            if (group == IntPtr.Zero)
            {
                ecP = GetBn(pkey, "p\0"u8, out bool okP);
                ecA = okP ? GetBn(pkey, "a\0"u8, out okP) : IntPtr.Zero;
                ecB = okP ? GetBn(pkey, "b\0"u8, out okP) : IntPtr.Zero;
                if (!okP) goto error;
                byte* fieldType = stackalloc byte[64];
                bool isChar2 = GetUtf8Param(pkey, "field-type\0"u8, fieldType, 64) && CStringEquals(fieldType, "characteristic-two-field\0"u8);
                if (isChar2)
                {
                    IntPtr gf2m = OptionalExport("EC_GROUP_new_curve_GF2m");
                    if (gf2m != IntPtr.Zero) group = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)gf2m)(ecP, ecA, ecB, IntPtr.Zero);
                }
                else
                {
                    group = EC_GROUP_new_curve_GFp(ecP, ecA, ecB, IntPtr.Zero);
                }
                if (group == IntPtr.Zero) goto error;
            }
            point = EC_POINT_new(group);
            if (point == IntPtr.Zero || EC_POINT_oct2point(group, point, pubKeyData, pubKeyLen, IntPtr.Zero) == 0) goto error;
            xBn = BN_new();
            yBn = BN_new();
            if (xBn == IntPtr.Zero || yBn == IntPtr.Zero) goto error;
            if (EC_POINT_get_affine_coordinates(group, point, xBn, yBn, IntPtr.Zero) == 0) goto error;
            if (includePrivate != 0)
            {
                dBn = GetBn(pkey, "priv\0"u8, out bool okD);
                if (!okD)
                {
                    rc = -1;
                    goto error;
                }
                *d = dBn;
                dBn = IntPtr.Zero;
                *cbD = (BN_num_bits(*d) + 7) / 8;
            }
            *qx = xBn;
            xBn = IntPtr.Zero;
            *cbQx = (BN_num_bits(*qx) + 7) / 8;
            *qy = yBn;
            yBn = IntPtr.Zero;
            *cbQy = (BN_num_bits(*qy) + 7) / 8;
            rc = 1;
            goto exit;
        error:
            *cbQx = *cbQy = *cbD = 0;
            *qx = *qy = *d = IntPtr.Zero;
        exit:
            if (exported != IntPtr.Zero) OSSL_PARAM_free(exported);
            if (xBn != IntPtr.Zero) BN_free(xBn);
            if (yBn != IntPtr.Zero) BN_free(yBn);
            if (dBn != IntPtr.Zero) BN_clear_free(dBn);
            if (pubKeyBuf != null) OpensslFree(pubKeyBuf);
            if (point != IntPtr.Zero) EC_POINT_free(point);
            if (group != IntPtr.Zero) EC_GROUP_free(group);
            if (ecP != IntPtr.Zero) BN_free(ecP);
            if (ecA != IntPtr.Zero) BN_free(ecA);
            if (ecB != IntPtr.Zero) BN_free(ecB);
            return rc;
        }
        [DllImport(LibCrypto)] private static extern IntPtr EC_KEY_get0_public_key(IntPtr key);
        [DllImport(LibCrypto)] private static extern IntPtr EC_KEY_get0_private_key(IntPtr key);
        [DllImport(LibCrypto)] private static extern IntPtr EC_GROUP_method_of(IntPtr group);
        [DllImport(LibCrypto)] private static extern int EC_METHOD_get_field_type(IntPtr method);
        [DllImport(LibCrypto)] private static extern IntPtr EC_GFp_mont_method();
        [DllImport(LibCrypto)] private static extern IntPtr EC_GFp_simple_method();
        [DllImport(LibCrypto)] private static extern IntPtr BN_dup(IntPtr a);

        // ECCurveType (pal_ecc_import_export.h).
        private const int CurveUnspecified = 0, CurvePrimeShortWeierstrass = 1, CurvePrimeTwistedEdwards = 2, CurvePrimeMontgomery = 3, CurveCharacteristic2 = 4;

        private static int MethodToCurveType(IntPtr method)
        {
            if (method == EC_GFp_mont_method()) return CurvePrimeMontgomery;
            int fieldType = EC_METHOD_get_field_type(method);
            if (fieldType == 407 /* NID_X9_62_characteristic_two_field */) return CurveCharacteristic2;
            if (fieldType == 406 /* NID_X9_62_prime_field */) return CurvePrimeShortWeierstrass;
            return CurveUnspecified;
        }

        private static int EcKeyGetCurveType(IntPtr key)
        {
            IntPtr group = EC_KEY_get0_group(key);
            if (group == IntPtr.Zero) return CurveUnspecified;
            IntPtr method = EC_GROUP_method_of(group);
            return method == IntPtr.Zero ? CurveUnspecified : MethodToCurveType(method);
        }

        // The legacy EC_KEY path: Q's coordinates (new BIGNUMs) and d (borrowed from the key).
        private static int GetECKeyParametersLegacy(IntPtr key, int includePrivate, IntPtr* qx, int* cbQx, IntPtr* qy, int* cbQy, IntPtr* d, int* cbD)
        {
            int rc = 0;
            ERR_clear_error();
            IntPtr q = EC_KEY_get0_public_key(key), group = EC_KEY_get0_group(key);
            IntPtr xBn = IntPtr.Zero, yBn = IntPtr.Zero;
            if (EcKeyGetCurveType(key) != CurveUnspecified && q != IntPtr.Zero && group != IntPtr.Zero)
            {
                xBn = BN_new();
                yBn = BN_new();
                if (xBn != IntPtr.Zero && yBn != IntPtr.Zero && EC_POINT_get_affine_coordinates(group, q, xBn, yBn, IntPtr.Zero) != 0)
                {
                    *qx = xBn; *cbQx = BnNumBytes(xBn);
                    *qy = yBn; *cbQy = BnNumBytes(yBn);
                    if (includePrivate == 0)
                    {
                        *d = IntPtr.Zero;
                        *cbD = 0;
                        return 1;
                    }
                    IntPtr privateKey = EC_KEY_get0_private_key(key);
                    if (privateKey != IntPtr.Zero)
                    {
                        *d = privateKey;
                        *cbD = BnNumBytes(privateKey);
                        return 1;
                    }
                    rc = -1;
                }
            }
            *cbQx = *cbQy = *cbD = 0;
            *qx = *qy = *d = IntPtr.Zero;
            if (xBn != IntPtr.Zero) BN_free(xBn);
            if (yBn != IntPtr.Zero) BN_free(yBn);
            return rc;
        }

        [RuntimeExport("CryptoNative_EvpPKeyGetEcKeyParameters", Trigger + "CryptoNative_EvpPKeyGetEcKeyParameters")]
        public static int EvpPKeyGetEcKeyParameters(IntPtr pkey, int includePrivate, IntPtr* qx, int* cbQx, IntPtr* qy, int* cbQy, IntPtr* d, int* cbD)
        {
            int rc = EvpPKeyGetEcKeyParametersProvider(pkey, includePrivate, qx, cbQx, qy, cbQy, d, cbD);
            if (rc != 2) return rc;
            IntPtr ecKey = EVP_PKEY_get1_EC_KEY(pkey);
            if (ecKey == IntPtr.Zero) return 0;
            IntPtr qxB = IntPtr.Zero, qyB = IntPtr.Zero, dB = IntPtr.Zero;
            int lqx = 0, lqy = 0, ld = 0;
            int result = GetECKeyParametersLegacy(ecKey, includePrivate, &qxB, &lqx, &qyB, &lqy, &dB, &ld);
            *qx = *qy = *d = IntPtr.Zero;
            *cbQx = *cbQy = *cbD = 0;
            if (result == 1)
            {
                *qx = qxB; *cbQx = lqx;
                *qy = qyB; *cbQy = lqy;
                if (includePrivate != 0 && dB != IntPtr.Zero)
                {
                    // The caller frees d: a copy of the key's own.
                    *d = BN_dup(dB);
                    if (*d == IntPtr.Zero)
                    {
                        BN_free(*qx);
                        BN_free(*qy);
                        *qx = *qy = IntPtr.Zero;
                        *cbQx = *cbQy = 0;
                        EC_KEY_free(ecKey);
                        return 0;
                    }
                    *cbD = ld;
                }
            }
            EC_KEY_free(ecKey);
            return result;
        }
        [DllImport(LibCrypto)] private static extern int EC_GROUP_get_field_type(IntPtr group);

        private static int NIDToCurveType(int fieldType) =>
            fieldType == 407 ? CurveCharacteristic2 : fieldType == 406 ? CurvePrimeShortWeierstrass : CurveUnspecified;

        [RuntimeExport("CryptoNative_EvpPKeyGetEcCurveParameters", Trigger + "CryptoNative_EvpPKeyGetEcCurveParameters")]
        public static int EvpPKeyGetEcCurveParameters(IntPtr pkey, int includePrivate, int* curveType,
            IntPtr* qx, int* cbQx, IntPtr* qy, int* cbQy, IntPtr* d, int* cbD, IntPtr* p, int* cbP, IntPtr* a, int* cbA, IntPtr* b, int* cbB,
            IntPtr* gx, int* cbGx, IntPtr* gy, int* cbGy, IntPtr* order, int* cbOrder, IntPtr* cofactor, int* cbCofactor, IntPtr* seed, int* cbSeed)
        {
            *cbQx = *cbQy = *cbD = 0;
            *qx = *qy = *d = IntPtr.Zero;
            *curveType = CurveUnspecified;
            *cbP = *cbA = *cbB = *cbGx = *cbGy = *cbOrder = *cbCofactor = *cbSeed = 0;
            *p = *a = *b = *gx = *gy = *order = *cofactor = *seed = IntPtr.Zero;
            // A key without a provider is a legacy EC_KEY wrapped by EVP_PKEY_set1_EC_KEY (an ECDsaOpenSsl built from a native EC_KEY):
            // the shim's legacy export of its explicit curve is not ported.
            if (EVP_PKEY_get0_provider(pkey) == IntPtr.Zero)
                throw new PlatformNotSupportedException("ECDsa/ECDiffieHellman.ExportExplicitParameters and ExportParameters of a key wrapped from a legacy OpenSSL EC_KEY (ECDsaOpenSsl(IntPtr), ECDiffieHellmanOpenSsl(SafeEvpPKeyHandle) of an EC_KEY) are not supported by csharpc yet.");
            ERR_clear_error();
            int rc = EvpPKeyGetEcKeyParametersProvider(pkey, includePrivate, qx, cbQx, qy, cbQy, d, cbD);
            IntPtr gPoint = IntPtr.Zero, xBn = BN_new(), yBn = BN_new(), pBn = IntPtr.Zero, aBn = IntPtr.Zero, bBn = IntPtr.Zero,
                orderBn = IntPtr.Zero, cofactorBn = IntPtr.Zero, group = IntPtr.Zero;
            byte* seedBuffer = null, generatorBuffer = null;
            nuint generatorBufferSize = 0, seedBufferSize = 0;
            int curveNid = 0, fieldTypeNid;
            if (rc != 1 || xBn == IntPtr.Zero || yBn == IntPtr.Zero) goto error;
            if (EvpPKeyGetEcGroupNid(pkey, &curveNid) == 0 || curveNid == 0)
            {
                byte* fieldTypeStr = stackalloc byte[32];
                if (!GetUtf8Param(pkey, "field-type\0"u8, fieldTypeStr, 32)) goto error;
                fieldTypeNid = OBJ_txt2nid(fieldTypeStr);
                if (fieldTypeNid == 0) goto error;
            }
            else
            {
                group = EC_GROUP_new_by_curve_name(curveNid);
                if (group == IntPtr.Zero) goto error;
                fieldTypeNid = EC_GROUP_get_field_type(group);
            }
            pBn = GetBn(pkey, "p\0"u8, out bool ok);
            if (!ok) goto error;
            aBn = GetBn(pkey, "a\0"u8, out ok);
            if (!ok) goto error;
            bBn = GetBn(pkey, "b\0"u8, out ok);
            if (!ok) goto error;
            *curveType = NIDToCurveType(fieldTypeNid);
            if (*curveType == CurveUnspecified) goto error;
            if (group == IntPtr.Zero)
            {
                if (fieldTypeNid == 407)
                {
                    IntPtr gf2m = OptionalExport("EC_GROUP_new_curve_GF2m");
                    if (gf2m != IntPtr.Zero) group = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)gf2m)(pBn, aBn, bBn, IntPtr.Zero);
                }
                else if (fieldTypeNid == 406)
                {
                    group = EC_GROUP_new_curve_GFp(pBn, aBn, bBn, IntPtr.Zero);
                }
            }
            if (group == IntPtr.Zero) goto error;
            fixed (byte* generatorName = "generator\0"u8)
            {
                if (EVP_PKEY_get_octet_string_param(pkey, generatorName, null, 0, &generatorBufferSize) == 0) goto error;
                generatorBuffer = (byte*)OpensslMalloc(generatorBufferSize);
                if (generatorBuffer == null || EVP_PKEY_get_octet_string_param(pkey, generatorName, generatorBuffer, generatorBufferSize, &generatorBufferSize) == 0) goto error;
            }
            gPoint = EC_POINT_new(group);
            if (gPoint == IntPtr.Zero || EC_POINT_oct2point(group, gPoint, generatorBuffer, generatorBufferSize, IntPtr.Zero) == 0) goto error;
            if (EC_POINT_get_affine_coordinates(group, gPoint, xBn, yBn, IntPtr.Zero) == 0) goto error;
            orderBn = GetBn(pkey, "order\0"u8, out ok);
            if (!ok) goto error;
            cofactorBn = GetBn(pkey, "cofactor\0"u8, out ok);
            if (!ok) goto error;
            fixed (byte* seedName = "seed\0"u8)
            {
                if (EVP_PKEY_get_octet_string_param(pkey, seedName, null, 0, &seedBufferSize) != 0)
                {
                    seedBuffer = (byte*)OpensslMalloc(seedBufferSize == 0 ? 1 : seedBufferSize);
                    nuint actualSeedSize;
                    if (seedBuffer != null && EVP_PKEY_get_octet_string_param(pkey, seedName, seedBuffer, seedBufferSize, &actualSeedSize) != 0)
                    {
                        *cbSeed = (int)actualSeedSize;
                        *seed = BN_bin2bn(seedBuffer, *cbSeed, IntPtr.Zero);
                        if (*seed == IntPtr.Zero) *cbSeed = 0;
                    }
                }
                else
                {
                    ERR_clear_error();
                }
            }
            *gx = xBn; *cbGx = BnNumBytes(xBn);
            *gy = yBn; *cbGy = BnNumBytes(yBn);
            *p = pBn; *cbP = BnNumBytes(pBn);
            *a = aBn; *cbA = BnNumBytes(aBn);
            *b = bBn; *cbB = BnNumBytes(bBn);
            *order = orderBn; *cbOrder = BnNumBytes(orderBn);
            *cofactor = cofactorBn; *cbCofactor = BnNumBytes(cofactorBn);
            rc = 1;
            goto exit;
        error:
            if (*qx != IntPtr.Zero) BN_free(*qx);
            if (*qy != IntPtr.Zero) BN_free(*qy);
            if (*d != IntPtr.Zero) BN_clear_free(*d);
            if (*seed != IntPtr.Zero) BN_free(*seed);
            *cbQx = *cbQy = *cbD = 0;
            *qx = *qy = *d = IntPtr.Zero;
            *curveType = CurveUnspecified;
            *cbP = *cbA = *cbB = *cbGx = *cbGy = *cbOrder = *cbCofactor = *cbSeed = 0;
            *p = *a = *b = *gx = *gy = *order = *cofactor = *seed = IntPtr.Zero;
            if (xBn != IntPtr.Zero) BN_free(xBn);
            if (yBn != IntPtr.Zero) BN_free(yBn);
            if (pBn != IntPtr.Zero) BN_free(pBn);
            if (aBn != IntPtr.Zero) BN_free(aBn);
            if (bBn != IntPtr.Zero) BN_free(bBn);
            if (orderBn != IntPtr.Zero) BN_free(orderBn);
            if (cofactorBn != IntPtr.Zero) BN_free(cofactorBn);
            if (rc == 1) rc = 0;
        exit:
            if (group != IntPtr.Zero) EC_GROUP_free(group);
            if (generatorBuffer != null) OpensslFree(generatorBuffer);
            if (gPoint != IntPtr.Zero) EC_POINT_free(gPoint);
            if (seedBuffer != null) OpensslFree(seedBuffer);
            return rc;
        }
        [DllImport(LibCrypto)] private static extern byte* OBJ_nid2sn(int n);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_CTX_set_group_name(IntPtr ctx, byte* name);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_PARAM_BLD_new();
        [DllImport(LibCrypto)] private static extern void OSSL_PARAM_BLD_free(IntPtr bld);
        [DllImport(LibCrypto)] private static extern int OSSL_PARAM_BLD_push_utf8_string(IntPtr bld, byte* key, byte* buf, nuint bsize);
        [DllImport(LibCrypto)] private static extern int OSSL_PARAM_BLD_push_octet_string(IntPtr bld, byte* key, void* buf, nuint bsize);
        [DllImport(LibCrypto)] private static extern int OSSL_PARAM_BLD_push_BN(IntPtr bld, byte* key, IntPtr bn);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_PARAM_BLD_to_param(IntPtr bld);

        [RuntimeExport("CryptoNative_EvpPKeyGenerateByEcCurveOid", Trigger + "CryptoNative_EvpPKeyGenerateByEcCurveOid")]
        public static int EvpPKeyGenerateByEcCurveOid(IntPtr* pkey, byte* oid, int* keySize)
        {
            if (pkey == null || oid == null) return 0;
            *pkey = IntPtr.Zero;
            ERR_clear_error();
            int rc = 0;
            IntPtr ctx = IntPtr.Zero;
            int nid = OBJ_txt2nid(oid);
            if (nid == 0) { rc = 2; goto error; }
            byte* groupName = OBJ_nid2sn(nid);
            if (groupName == null) goto error;
            fixed (byte* ec = "EC\0"u8) ctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, ec, null);
            if (ctx == IntPtr.Zero || EVP_PKEY_keygen_init(ctx) <= 0) goto error;
            // An unknown curve is "not supported" (2), not an error.
            if (EVP_PKEY_CTX_set_group_name(ctx, groupName) <= 0) { rc = 2; goto error; }
            if (EVP_PKEY_keygen(ctx, pkey) <= 0) { rc = 2; goto error; }
            if (keySize != null) *keySize = EvpPKeyGetEcKeySize(*pkey);
            rc = 1;
            goto exit;
        error:
            if (keySize != null) *keySize = 0;
            if (*pkey != IntPtr.Zero)
            {
                EVP_PKEY_free(*pkey);
                *pkey = IntPtr.Zero;
            }
        exit:
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            return rc;
        }

        // The uncompressed point of (x, y), or of d·G when only d is given, as an OPENSSL_zalloc'd buffer.
        private static byte* EncodeEcPointFromPoint(IntPtr group, IntPtr point, nuint* outLength)
        {
            nuint len = EC_POINT_point2oct(group, point, 4 /* POINT_CONVERSION_UNCOMPRESSED */, null, 0, IntPtr.Zero);
            if (len == 0) return null;
            byte* buf = (byte*)OpensslZalloc(len);
            if (buf == null) return null;
            if (EC_POINT_point2oct(group, point, 4, buf, len, IntPtr.Zero) != len)
            {
                OpensslFree(buf);
                return null;
            }
            *outLength = len;
            return buf;
        }

        private static byte* EncodeEcPointFromCoordinates(byte* x, int xLength, byte* y, int yLength, IntPtr group, nuint* outLength)
        {
            byte* buf = null;
            IntPtr xBn = BN_bin2bn(x, xLength, IntPtr.Zero), yBn = BN_bin2bn(y, yLength, IntPtr.Zero), point = IntPtr.Zero;
            if (xBn != IntPtr.Zero && yBn != IntPtr.Zero)
            {
                point = EC_POINT_new(group);
                if (point != IntPtr.Zero && EC_POINT_set_affine_coordinates(group, point, xBn, yBn, IntPtr.Zero) != 0)
                    buf = EncodeEcPointFromPoint(group, point, outLength);
            }
            if (point != IntPtr.Zero) EC_POINT_free(point);
            if (xBn != IntPtr.Zero) BN_free(xBn);
            if (yBn != IntPtr.Zero) BN_free(yBn);
            return buf;
        }

        private static bool PushPublicKeyParam(IntPtr bld, IntPtr group, byte* qx, int qxLength, byte* qy, int qyLength, IntPtr dBn, byte** outPubKeyBuf)
        {
            bool hasPublicKey = qx != null && qy != null;
            if (!hasPublicKey && dBn == IntPtr.Zero) return true;
            byte* pubKeyBuf;
            nuint pubKeyLen = 0;
            if (hasPublicKey)
            {
                pubKeyBuf = EncodeEcPointFromCoordinates(qx, qxLength, qy, qyLength, group, &pubKeyLen);
            }
            else
            {
                IntPtr pubPoint = EC_POINT_new(group);
                if (pubPoint == IntPtr.Zero || EC_POINT_mul(group, pubPoint, dBn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0)
                {
                    if (pubPoint != IntPtr.Zero) EC_POINT_free(pubPoint);
                    return false;
                }
                pubKeyBuf = EncodeEcPointFromPoint(group, pubPoint, &pubKeyLen);
                EC_POINT_free(pubPoint);
            }
            if (pubKeyBuf == null) return false;
            *outPubKeyBuf = pubKeyBuf;
            fixed (byte* pub = "pub\0"u8) return OSSL_PARAM_BLD_push_octet_string(bld, pub, pubKeyBuf, pubKeyLen) != 0;
        }

        [RuntimeExport("CryptoNative_EvpPKeyCreateByEcParameters", Trigger + "CryptoNative_EvpPKeyCreateByEcParameters")]
        public static int EvpPKeyCreateByEcParameters(IntPtr* pkey, byte* oid, byte* qx, int qxLength, byte* qy, int qyLength, byte* d, int dLength, int* keySize)
        {
            if (pkey == null || oid == null) return 0;
            *pkey = IntPtr.Zero;
            ERR_clear_error();
            if (qxLength < 0 || qyLength < 0 || dLength < 0) return 0;
            int nid = OBJ_txt2nid(oid);
            if (nid == 0) return 2;
            byte* groupName = OBJ_nid2sn(nid);
            if (groupName == null) return 0;
            int ret = 0;
            IntPtr ctx = IntPtr.Zero, bld = IntPtr.Zero, parameters = IntPtr.Zero, dBn = IntPtr.Zero, group = IntPtr.Zero;
            byte* pubKeyBuf = null;
            bool hasPrivateKey = d != null && dLength > 0;
            bld = OSSL_PARAM_BLD_new();
            if (bld == IntPtr.Zero) goto error;
            fixed (byte* groupKey = "group\0"u8) if (OSSL_PARAM_BLD_push_utf8_string(bld, groupKey, groupName, 0) == 0) goto error;
            if (hasPrivateKey)
            {
                dBn = BN_bin2bn(d, dLength, IntPtr.Zero);
                if (dBn == IntPtr.Zero) goto error;
                fixed (byte* privKey = "priv\0"u8) if (OSSL_PARAM_BLD_push_BN(bld, privKey, dBn) == 0) goto error;
            }
            group = EC_GROUP_new_by_curve_name(nid);
            if (group == IntPtr.Zero) goto error;
            if (!PushPublicKeyParam(bld, group, qx, qxLength, qy, qyLength, dBn, &pubKeyBuf)) goto error;
            parameters = OSSL_PARAM_BLD_to_param(bld);
            if (parameters == IntPtr.Zero) goto error;
            fixed (byte* ec = "EC\0"u8) ctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, ec, null);
            if (ctx == IntPtr.Zero || EVP_PKEY_fromdata_init(ctx) != 1) goto error;
            if (EVP_PKEY_fromdata(ctx, pkey, hasPrivateKey ? 135 : 134, (OsslParam*)parameters) != 1) goto error;
            if (keySize != null) *keySize = EvpPKeyGetEcKeySize(*pkey);
            ret = 1;
            goto exit;
        error:
            if (keySize != null) *keySize = 0;
            if (*pkey != IntPtr.Zero)
            {
                EVP_PKEY_free(*pkey);
                *pkey = IntPtr.Zero;
            }
        exit:
            if (parameters != IntPtr.Zero) OSSL_PARAM_free(parameters);
            if (bld != IntPtr.Zero) OSSL_PARAM_BLD_free(bld);
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            if (dBn != IntPtr.Zero) BN_clear_free(dBn);
            if (group != IntPtr.Zero) EC_GROUP_free(group);
            if (pubKeyBuf != null) OpensslFree(pubKeyBuf);
            return ret;
        }
        [DllImport(LibCrypto)] private static extern int EC_GROUP_set_generator(IntPtr group, IntPtr generator, IntPtr order, IntPtr cofactor);
        [DllImport(LibCrypto)] private static extern int EVP_PKEY_generate(IntPtr ctx, IntPtr* ppkey);

        [RuntimeExport("CryptoNative_EvpPKeyCreateByEcExplicitParameters", Trigger + "CryptoNative_EvpPKeyCreateByEcExplicitParameters")]
        public static int EvpPKeyCreateByEcExplicitParameters(int curveType, byte* qx, int qxLength, byte* qy, int qyLength, byte* d, int dLength,
            byte* p, int pLength, byte* a, int aLength, byte* b, int bLength, byte* gx, int gxLength, byte* gy, int gyLength,
            byte* order, int orderLength, byte* cofactor, int cofactorLength, byte* seed, int seedLength, IntPtr* pkey, int* keySize)
        {
            if (p == null || a == null || b == null || gx == null || gy == null || order == null || cofactor == null || pkey == null) return 0;
            *pkey = IntPtr.Zero;
            ERR_clear_error();
            if (qxLength < 0 || qyLength < 0 || dLength < 0 || pLength < 0 || aLength < 0 || bLength < 0 || gxLength < 0 || gyLength < 0
                || orderLength < 0 || cofactorLength < 0 || seedLength < 0)
                return 0;
            int ret = 0;
            IntPtr ctx = IntPtr.Zero, bld = IntPtr.Zero, parameters = IntPtr.Zero, pBn = IntPtr.Zero, aBn = IntPtr.Zero, bBn = IntPtr.Zero,
                orderBn = IntPtr.Zero, cofactorBn = IntPtr.Zero, dBn = IntPtr.Zero, gxBn = IntPtr.Zero, gyBn = IntPtr.Zero, group = IntPtr.Zero, g = IntPtr.Zero;
            byte* generatorBuf = null, pubKeyBuf = null;
            nuint genLen = 0;
            bool hasPublicKey = qx != null && qy != null, hasPrivateKey = d != null && dLength > 0;
            fixed (byte* fieldTypeKey = "field-type\0"u8) fixed (byte* char2 = "characteristic-two-field\0"u8) fixed (byte* prime = "prime-field\0"u8)
            fixed (byte* encodingKey = "encoding\0"u8) fixed (byte* explicitValue = "explicit\0"u8) fixed (byte* pKey = "p\0"u8) fixed (byte* aKey = "a\0"u8)
            fixed (byte* bKey = "b\0"u8) fixed (byte* generatorKey = "generator\0"u8) fixed (byte* orderKey = "order\0"u8)
            fixed (byte* cofactorKey = "cofactor\0"u8) fixed (byte* seedKey = "seed\0"u8) fixed (byte* privKey = "priv\0"u8) fixed (byte* ec = "EC\0"u8)
            {
                bld = OSSL_PARAM_BLD_new();
                if (bld == IntPtr.Zero) goto error;
                if (OSSL_PARAM_BLD_push_utf8_string(bld, fieldTypeKey, curveType == CurveCharacteristic2 ? char2 : prime, 0) == 0) goto error;
                if (OSSL_PARAM_BLD_push_utf8_string(bld, encodingKey, explicitValue, 0) == 0) goto error;
                pBn = BN_bin2bn(p, pLength, IntPtr.Zero);
                aBn = BN_bin2bn(a, aLength, IntPtr.Zero);
                bBn = BN_bin2bn(b, bLength, IntPtr.Zero);
                if (pBn == IntPtr.Zero || aBn == IntPtr.Zero || bBn == IntPtr.Zero) goto error;
                if (OSSL_PARAM_BLD_push_BN(bld, pKey, pBn) == 0 || OSSL_PARAM_BLD_push_BN(bld, aKey, aBn) == 0 || OSSL_PARAM_BLD_push_BN(bld, bKey, bBn) == 0) goto error;
                if (curveType == CurveCharacteristic2)
                {
                    IntPtr gf2m = OptionalExport("EC_GROUP_new_curve_GF2m");
                    if (gf2m != IntPtr.Zero) group = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)gf2m)(pBn, aBn, bBn, IntPtr.Zero);
                }
                else
                {
                    group = EC_GROUP_new_curve_GFp(pBn, aBn, bBn, IntPtr.Zero);
                }
                if (group == IntPtr.Zero) goto error;
                g = EC_POINT_new(group);
                gxBn = BN_bin2bn(gx, gxLength, IntPtr.Zero);
                gyBn = BN_bin2bn(gy, gyLength, IntPtr.Zero);
                if (g == IntPtr.Zero || gxBn == IntPtr.Zero || gyBn == IntPtr.Zero) goto error;
                if (EC_POINT_set_affine_coordinates(group, g, gxBn, gyBn, IntPtr.Zero) == 0) goto error;
                orderBn = BN_bin2bn(order, orderLength, IntPtr.Zero);
                cofactorBn = BN_bin2bn(cofactor, cofactorLength, IntPtr.Zero);
                if (orderBn == IntPtr.Zero || cofactorBn == IntPtr.Zero) goto error;
                if (EC_GROUP_set_generator(group, g, orderBn, cofactorBn) == 0) goto error;
                generatorBuf = EncodeEcPointFromPoint(group, g, &genLen);
                if (generatorBuf == null) goto error;
                if (OSSL_PARAM_BLD_push_octet_string(bld, generatorKey, generatorBuf, genLen) == 0) goto error;
                if (OSSL_PARAM_BLD_push_BN(bld, orderKey, orderBn) == 0 || OSSL_PARAM_BLD_push_BN(bld, cofactorKey, cofactorBn) == 0) goto error;
                if (seed != null && seedLength > 0 && OSSL_PARAM_BLD_push_octet_string(bld, seedKey, seed, (nuint)seedLength) == 0) goto error;
                if (hasPrivateKey)
                {
                    dBn = BN_bin2bn(d, dLength, IntPtr.Zero);
                    if (dBn == IntPtr.Zero || OSSL_PARAM_BLD_push_BN(bld, privKey, dBn) == 0) goto error;
                }
                if (!PushPublicKeyParam(bld, group, qx, qxLength, qy, qyLength, dBn, &pubKeyBuf)) goto error;
                parameters = OSSL_PARAM_BLD_to_param(bld);
                if (parameters == IntPtr.Zero) goto error;
                ctx = EVP_PKEY_CTX_new_from_name(IntPtr.Zero, ec, null);
                if (ctx == IntPtr.Zero) goto error;
                if (!hasPublicKey && !hasPrivateKey)
                {
                    // Only the curve: a new key on it, generated from a parameters-only template.
                    IntPtr templateKey = IntPtr.Zero;
                    if (EVP_PKEY_fromdata_init(ctx) != 1) goto error;
                    if (EVP_PKEY_fromdata(ctx, &templateKey, 132 /* EVP_PKEY_KEY_PARAMETERS */, (OsslParam*)parameters) != 1) goto error;
                    EVP_PKEY_CTX_free(ctx);
                    ctx = EVP_PKEY_CTX_new_from_pkey(IntPtr.Zero, templateKey, null);
                    EVP_PKEY_free(templateKey);
                    if (ctx == IntPtr.Zero || EVP_PKEY_keygen_init(ctx) != 1 || EVP_PKEY_generate(ctx, pkey) != 1) goto error;
                }
                else
                {
                    if (EVP_PKEY_fromdata_init(ctx) != 1) goto error;
                    if (EVP_PKEY_fromdata(ctx, pkey, hasPrivateKey ? 135 : 134, (OsslParam*)parameters) != 1) goto error;
                }
            }
            if (keySize != null) *keySize = EvpPKeyGetEcKeySize(*pkey);
            ret = 1;
            goto exit;
        error:
            if (keySize != null) *keySize = 0;
            if (*pkey != IntPtr.Zero)
            {
                EVP_PKEY_free(*pkey);
                *pkey = IntPtr.Zero;
            }
        exit:
            if (parameters != IntPtr.Zero) OSSL_PARAM_free(parameters);
            if (bld != IntPtr.Zero) OSSL_PARAM_BLD_free(bld);
            if (ctx != IntPtr.Zero) EVP_PKEY_CTX_free(ctx);
            if (generatorBuf != null) OpensslFree(generatorBuf);
            if (pubKeyBuf != null) OpensslFree(pubKeyBuf);
            if (pBn != IntPtr.Zero) BN_free(pBn);
            if (aBn != IntPtr.Zero) BN_free(aBn);
            if (bBn != IntPtr.Zero) BN_free(bBn);
            if (orderBn != IntPtr.Zero) BN_free(orderBn);
            if (cofactorBn != IntPtr.Zero) BN_free(cofactorBn);
            if (dBn != IntPtr.Zero) BN_clear_free(dBn);
            if (gxBn != IntPtr.Zero) BN_free(gxBn);
            if (gyBn != IntPtr.Zero) BN_free(gyBn);
            if (g != IntPtr.Zero) EC_POINT_free(g);
            if (group != IntPtr.Zero) EC_GROUP_free(group);
            return ret;
        }
    }
}
#endif
