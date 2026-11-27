// The shim's PKCS#7 certificate collections and OCSP request/response helpers over libcrypto: pal_pkcs7.c and pal_ocsp.c (dotnet/runtime,
// .NET 11 GA). See CryptoNative.cs.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        [DllImport(LibCrypto)] private static extern IntPtr PEM_read_bio_PKCS7(IntPtr bp, IntPtr x, IntPtr cb, IntPtr u);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_PKCS7(IntPtr a, byte** pp, long length);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_PKCS7_bio(IntPtr bp, IntPtr p7);
        [DllImport(LibCrypto)] private static extern IntPtr PKCS7_sign(IntPtr signcert, IntPtr pkey, IntPtr certs, IntPtr data, int flags);
        [DllImport(LibCrypto)] private static extern void PKCS7_free(IntPtr p7);
        [DllImport(LibCrypto)] private static extern int i2d_PKCS7(IntPtr a, byte** pp);
        [DllImport(LibCrypto)] private static extern int i2d_OCSP_REQUEST(IntPtr a, byte** pp);

        [RuntimeExport("CryptoNative_PemReadBioPkcs7", Trigger + "CryptoNative_PemReadBioPkcs7")]
        public static IntPtr PemReadBioPkcs7(IntPtr bp)
        {
            ERR_clear_error();
            return PEM_read_bio_PKCS7(bp, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        [RuntimeExport("CryptoNative_DecodePkcs7", Trigger + "CryptoNative_DecodePkcs7")]
        public static IntPtr DecodePkcs7(byte* buf, int len)
        {
            ERR_clear_error();
            if (buf == null || len <= 0) return IntPtr.Zero;
            return d2i_PKCS7(IntPtr.Zero, &buf, len);
        }

        [RuntimeExport("CryptoNative_D2IPkcs7Bio", Trigger + "CryptoNative_D2IPkcs7Bio")]
        public static IntPtr D2IPkcs7Bio(IntPtr bp)
        {
            ERR_clear_error();
            return d2i_PKCS7_bio(bp, IntPtr.Zero);
        }

        [RuntimeExport("CryptoNative_Pkcs7CreateCertificateCollection", Trigger + "CryptoNative_Pkcs7CreateCertificateCollection")]
        public static IntPtr Pkcs7CreateCertificateCollection(IntPtr certs)
        {
            ERR_clear_error();
            return PKCS7_sign(IntPtr.Zero, IntPtr.Zero, certs, IntPtr.Zero, 0x4000 /* PKCS7_PARTIAL */);
        }

        [RuntimeExport("CryptoNative_Pkcs7Destroy", Trigger + "CryptoNative_Pkcs7Destroy")]
        public static void Pkcs7Destroy(IntPtr p7)
        {
            if (p7 != IntPtr.Zero) PKCS7_free(p7);
        }

        // PKCS7 (OpenSSL 3): type at 24, the d union at 32; PKCS7_SIGNED and PKCS7_SIGN_ENVELOPE hold `cert` at 16. The stack is
        // borrowed from the PKCS7, as there.
        [RuntimeExport("CryptoNative_GetPkcs7Certificates", Trigger + "CryptoNative_GetPkcs7Certificates")]
        public static int GetPkcs7Certificates(IntPtr p7, IntPtr* certs)
        {
            if (p7 == IntPtr.Zero || certs == null) return 0;
            int nid = OBJ_obj2nid(*(IntPtr*)((byte*)p7 + 24));
            if (nid != 22 /* NID_pkcs7_signed */ && nid != 24 /* NID_pkcs7_signedAndEnveloped */) return 0;
            IntPtr d = *(IntPtr*)((byte*)p7 + 32);
            if (d == IntPtr.Zero) return 0;
            *certs = *(IntPtr*)((byte*)d + 16);
            return 1;
        }

        [RuntimeExport("CryptoNative_GetPkcs7DerSize", Trigger + "CryptoNative_GetPkcs7DerSize")]
        public static int GetPkcs7DerSize(IntPtr p7)
        {
            ERR_clear_error();
            return i2d_PKCS7(p7, null);
        }

        [RuntimeExport("CryptoNative_EncodePkcs7", Trigger + "CryptoNative_EncodePkcs7")]
        public static int EncodePkcs7(IntPtr p7, byte* buf)
        {
            ERR_clear_error();
            return i2d_PKCS7(p7, &buf);
        }

        [RuntimeExport("CryptoNative_OcspRequestDestroy", Trigger + "CryptoNative_OcspRequestDestroy")]
        public static void OcspRequestDestroy(IntPtr request)
        {
            if (request != IntPtr.Zero) OCSP_REQUEST_free(request);
        }

        [RuntimeExport("CryptoNative_GetOcspRequestDerSize", Trigger + "CryptoNative_GetOcspRequestDerSize")]
        public static int GetOcspRequestDerSize(IntPtr req)
        {
            ERR_clear_error();
            return i2d_OCSP_REQUEST(req, null);
        }

        [RuntimeExport("CryptoNative_EncodeOcspRequest", Trigger + "CryptoNative_EncodeOcspRequest")]
        public static int EncodeOcspRequest(IntPtr req, byte* buf)
        {
            ERR_clear_error();
            return i2d_OCSP_REQUEST(req, &buf);
        }

        [RuntimeExport("CryptoNative_DecodeOcspResponse", Trigger + "CryptoNative_DecodeOcspResponse")]
        public static IntPtr DecodeOcspResponse(byte* buf, int len)
        {
            ERR_clear_error();
            if (buf == null || len <= 0) return IntPtr.Zero;
            return d2i_OCSP_RESPONSE(IntPtr.Zero, &buf, len);
        }

        [RuntimeExport("CryptoNative_OcspResponseDestroy", Trigger + "CryptoNative_OcspResponseDestroy")]
        public static void OcspResponseDestroy(IntPtr response)
        {
            if (response != IntPtr.Zero) OCSP_RESPONSE_free(response);
        }
    }
}
#endif
