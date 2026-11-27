// The shim's certificates, chains, CRLs, OCSP and the root store's location over libcrypto: pal_x509.c and pal_x509_root.c (dotnet/runtime,
// .NET 11 GA), function by function. See CryptoNative.cs. The chain engine is OpenSSL's X509_verify_cert, configured exactly as the shim
// configures it (X509_V_FLAG_CHECK_SS_SIGNATURE on every context, CRL flags from the revocation flag, the framework's verify callback);
// the policy decisions above it (trust mode, revocation mode, the status mapping) are the framework's IL, unchanged.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        // ---- libcrypto -------------------------------------------------------------------------------------------------
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_X509_PUBKEY(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr X509_PUBKEY_get(IntPtr key);
        [DllImport(LibCrypto)] private static extern int i2d_X509_PUBKEY(IntPtr a, byte** pp);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_X509_CRL(IntPtr a, byte** pp, long length);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_X509(IntPtr a, byte** pp, long length);
        [DllImport(LibCrypto)] private static extern int i2d_X509(IntPtr x, byte** pp);
        [DllImport(LibCrypto)] internal static extern void X509_free(IntPtr x);
        [DllImport(LibCrypto)] internal static extern int X509_up_ref(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr X509_dup(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr PEM_read_bio_X509(IntPtr bio, IntPtr x, IntPtr cb, IntPtr u);
        [DllImport(LibCrypto)] private static extern IntPtr PEM_read_bio_X509_AUX(IntPtr bio, IntPtr x, IntPtr cb, IntPtr u);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_serialNumber(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_issuer_name(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_subject_name(IntPtr x);
        [DllImport(LibCrypto)] private static extern int X509_check_purpose(IntPtr x, int id, int ca);
        [DllImport(LibCrypto)] private static extern ulong X509_issuer_name_hash(IntPtr x);
        [DllImport(LibCrypto)] private static extern ulong X509_subject_name_hash(IntPtr x);
        [DllImport(LibCrypto)] private static extern int X509_get_ext_count(IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_ext(IntPtr x, int loc);
        [DllImport(LibCrypto)] private static extern int X509_get_ext_by_NID(IntPtr x, int nid, int lastpos);
        [DllImport(LibCrypto)] private static extern IntPtr X509_EXTENSION_get_object(IntPtr ex);
        [DllImport(LibCrypto)] private static extern IntPtr X509_EXTENSION_get_data(IntPtr ex);
        [DllImport(LibCrypto)] private static extern int X509_EXTENSION_get_critical(IntPtr ex);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_ex_data(IntPtr x, int idx);
        [DllImport(LibCrypto)] internal static extern int X509_set_ex_data(IntPtr x, int idx, IntPtr data);

        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_new();
        [DllImport(LibCrypto)] private static extern void X509_STORE_free(IntPtr v);
        [DllImport(LibCrypto)] private static extern int X509_STORE_add_cert(IntPtr ctx, IntPtr x);
        [DllImport(LibCrypto)] private static extern int X509_STORE_add_crl(IntPtr ctx, IntPtr x);
        [DllImport(LibCrypto)] private static extern int X509_STORE_set_flags(IntPtr ctx, ulong flags);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_get0_param(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_new();
        [DllImport(LibCrypto)] private static extern void X509_STORE_CTX_free(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int X509_STORE_CTX_init(IntPtr ctx, IntPtr store, IntPtr x509, IntPtr chain);
        [DllImport(LibCrypto)] private static extern void X509_STORE_CTX_cleanup(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern void X509_STORE_CTX_set_flags(IntPtr ctx, ulong flags);
        [DllImport(LibCrypto)] private static extern int X509_verify_cert(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_get1_chain(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_get0_chain(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_get_current_cert(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_get0_untrusted(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_get0_cert(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_get0_store(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int X509_STORE_CTX_get_error(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern int X509_STORE_CTX_get_error_depth(IntPtr ctx);
        [DllImport(LibCrypto)] private static extern void X509_STORE_CTX_set_verify_cb(IntPtr ctx, IntPtr cb);
        [DllImport(LibCrypto)] private static extern int X509_STORE_CTX_set_ex_data(IntPtr ctx, int idx, IntPtr data);
        [DllImport(LibCrypto)] private static extern IntPtr X509_STORE_CTX_get_ex_data(IntPtr ctx, int idx);
        [DllImport(LibCrypto)] private static extern int X509_STORE_CTX_get1_issuer(IntPtr* issuer, IntPtr ctx, IntPtr x);
        [DllImport(LibCrypto)] private static extern byte* X509_verify_cert_error_string(long n);
        [DllImport(LibCrypto)] private static extern ulong X509_VERIFY_PARAM_get_flags(IntPtr param);
        [DllImport(LibCrypto)] private static extern int X509_VERIFY_PARAM_clear_flags(IntPtr param, ulong flags);
        [DllImport(LibCrypto)] private static extern int X509_VERIFY_PARAM_set_time(IntPtr param, long t);
        [DllImport(LibCrypto)] private static extern void X509_CRL_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern int PEM_write_bio_X509_CRL(IntPtr bio, IntPtr crl);
        [DllImport(LibCrypto)] private static extern IntPtr PEM_read_bio_X509_CRL(IntPtr bio, IntPtr x, IntPtr cb, IntPtr u);
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_default_cert_dir_env();
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_default_cert_dir();
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_default_cert_file_env();
        [DllImport(LibCrypto)] private static extern IntPtr X509_get_default_cert_file();

        [DllImport(LibCrypto)] internal static extern IntPtr OPENSSL_sk_new_null();
        [DllImport(LibCrypto)] internal static extern int OPENSSL_sk_num(IntPtr st);
        [DllImport(LibCrypto)] internal static extern IntPtr OPENSSL_sk_value(IntPtr st, int i);
        [DllImport(LibCrypto)] internal static extern int OPENSSL_sk_push(IntPtr st, IntPtr data);
        [DllImport(LibCrypto)] internal static extern IntPtr OPENSSL_sk_pop(IntPtr st);
        [DllImport(LibCrypto)] internal static extern void OPENSSL_sk_free(IntPtr st);

        [DllImport(LibCrypto)] private static extern IntPtr BIO_new_file(byte* filename, byte* mode);
        [DllImport(LibCrypto)] internal static extern int BIO_free(IntPtr a);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_PKCS12_bio(IntPtr bio, IntPtr p12);
        [DllImport(LibCrypto)] private static extern int PKCS12_parse(IntPtr p12, byte* pass, IntPtr* pkey, IntPtr* cert, IntPtr* ca);
        [DllImport(LibCrypto)] private static extern void PKCS12_free(IntPtr p12);
        [DllImport(LibCrypto)] internal static extern void EVP_PKEY_free(IntPtr pkey);

        [DllImport(LibCrypto)] private static extern IntPtr OCSP_cert_to_id(IntPtr dgst, IntPtr subject, IntPtr issuer);
        [DllImport(LibCrypto)] private static extern void OCSP_CERTID_free(IntPtr id);
        [DllImport(LibCrypto)] private static extern IntPtr OCSP_response_get1_basic(IntPtr resp);
        [DllImport(LibCrypto)] private static extern void OCSP_BASICRESP_free(IntPtr bs);
        [DllImport(LibCrypto)] private static extern int OCSP_check_nonce(IntPtr req, IntPtr bs);
        [DllImport(LibCrypto)] private static extern int OCSP_basic_verify(IntPtr bs, IntPtr certs, IntPtr st, ulong flags);
        [DllImport(LibCrypto)] private static extern int OCSP_resp_find_status(IntPtr bs, IntPtr id, int* status, int* reason, IntPtr* revtime, IntPtr* thisupd, IntPtr* nextupd);
        [DllImport(LibCrypto)] private static extern IntPtr OCSP_REQUEST_new();
        [DllImport(LibCrypto)] internal static extern void OCSP_REQUEST_free(IntPtr req);
        [DllImport(LibCrypto)] private static extern IntPtr OCSP_request_add0_id(IntPtr req, IntPtr cid);
        [DllImport(LibCrypto)] internal static extern void OCSP_RESPONSE_free(IntPtr resp);
        [DllImport(LibCrypto)] private static extern IntPtr d2i_OCSP_RESPONSE(IntPtr a, byte** pp, long length);
        [DllImport(LibCrypto)] private static extern IntPtr ASN1_item_d2i_bio(IntPtr it, IntPtr bio, IntPtr x);
        [DllImport(LibCrypto)] private static extern int ASN1_item_i2d_bio(IntPtr it, IntPtr bio, IntPtr x);
        [DllImport(LibCrypto)] private static extern IntPtr OCSP_RESPONSE_it();
        [DllImport(LibCrypto)] private static extern int X509_cmp_time(IntPtr s, long* t);
        [DllImport(LibCrypto)] private static extern int ASN1_TIME_to_tm(IntPtr s, void* tm);

        [DllImport("libc")] private static extern IntPtr getenv(byte* name);
        [DllImport("libc")] private static extern int unlink(byte* path);
        [DllImport("libc")] private static extern long time(long* t);
        [DllImport("libc")] private static extern long timegm(void* tm);
        [DllImport("libc")] private static extern IntPtr opendir(byte* name);
        [DllImport("libc")] private static extern byte* readdir(IntPtr dir);
        [DllImport("libc")] private static extern int closedir(IntPtr dir);

        private const int ERR_LIB_X509 = 11;
        private const ulong ERR_PACK_X509_CERT_ALREADY_IN_HASH_TABLE = (11UL << 23) | 101;
        private const int PAL_X509_V_OK = 0, PAL_X509_V_ERR_UNABLE_TO_GET_CRL = 3, PAL_X509_V_ERR_CRL_HAS_EXPIRED = 12, PAL_X509_V_ERR_CERT_REVOKED = 23;
        // struct dirent's d_name (glibc, every 64-bit Linux target) and NAME_MAX.
        private const int DirentNameOffset = 19, NAME_MAX = 255;

        private static void X509PutMallocFailure() => ERR_put_error(ERR_LIB_X509, 0, ERR_R_MALLOC_FAILURE, null, 0);

        // ---- certificates ----------------------------------------------------------------------------------------------
        [RuntimeExport("CryptoNative_GetX509EvpPublicKey", Trigger + "CryptoNative_GetX509EvpPublicKey")]
        public static IntPtr GetX509EvpPublicKey(IntPtr x509)
        {
            ERR_clear_error();
            return X509_PUBKEY_get(X509_get_X509_PUBKEY(x509));
        }

        [RuntimeExport("CryptoNative_DecodeX509Crl", Trigger + "CryptoNative_DecodeX509Crl")]
        public static IntPtr DecodeX509Crl(byte* buf, int len)
        {
            ERR_clear_error();
            if (buf == null || len <= 0) return IntPtr.Zero;
            return d2i_X509_CRL(IntPtr.Zero, &buf, len);
        }

        [RuntimeExport("CryptoNative_DecodeX509", Trigger + "CryptoNative_DecodeX509")]
        public static IntPtr DecodeX509(byte* buf, int len)
        {
            ERR_clear_error();
            if (buf == null || len <= 0) return IntPtr.Zero;
            return d2i_X509(IntPtr.Zero, &buf, len);
        }

        [RuntimeExport("CryptoNative_GetX509DerSize", Trigger + "CryptoNative_GetX509DerSize")]
        public static int GetX509DerSize(IntPtr x)
        {
            ERR_clear_error();
            return i2d_X509(x, null);
        }

        // The framework sizes buf with GetX509DerSize first, as with every i2d pair below.
        [RuntimeExport("CryptoNative_EncodeX509", Trigger + "CryptoNative_EncodeX509")]
        public static int EncodeX509(IntPtr x, byte* buf)
        {
            ERR_clear_error();
            return i2d_X509(x, &buf);
        }

        [RuntimeExport("CryptoNative_X509Destroy", Trigger + "CryptoNative_X509Destroy")]
        public static void X509Destroy(IntPtr a)
        {
            if (a != IntPtr.Zero) X509_free(a);
        }

        [RuntimeExport("CryptoNative_PemReadX509FromBio", Trigger + "CryptoNative_PemReadX509FromBio")]
        public static IntPtr PemReadX509FromBio(IntPtr bio)
        {
            ERR_clear_error();
            return PEM_read_bio_X509(bio, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        [RuntimeExport("CryptoNative_PemReadX509FromBioAux", Trigger + "CryptoNative_PemReadX509FromBioAux")]
        public static IntPtr PemReadX509FromBioAux(IntPtr bio)
        {
            ERR_clear_error();
            return PEM_read_bio_X509_AUX(bio, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        [RuntimeExport("CryptoNative_X509GetSerialNumber", Trigger + "CryptoNative_X509GetSerialNumber")]
        public static IntPtr X509GetSerialNumber(IntPtr x509) => X509_get_serialNumber(x509);

        [RuntimeExport("CryptoNative_X509GetIssuerName", Trigger + "CryptoNative_X509GetIssuerName")]
        public static IntPtr X509GetIssuerName(IntPtr x509) => X509_get_issuer_name(x509);

        [RuntimeExport("CryptoNative_X509GetSubjectName", Trigger + "CryptoNative_X509GetSubjectName")]
        public static IntPtr X509GetSubjectName(IntPtr x509) => X509_get_subject_name(x509);

        [RuntimeExport("CryptoNative_X509CheckPurpose", Trigger + "CryptoNative_X509CheckPurpose")]
        public static int X509CheckPurpose(IntPtr x, int id, int ca)
        {
            ERR_clear_error();
            return X509_check_purpose(x, id, ca);
        }

        [RuntimeExport("CryptoNative_X509IssuerNameHash", Trigger + "CryptoNative_X509IssuerNameHash")]
        public static ulong X509IssuerNameHash(IntPtr x)
        {
            ERR_clear_error();
            return X509_issuer_name_hash(x);
        }

        [RuntimeExport("CryptoNative_X509GetExtCount", Trigger + "CryptoNative_X509GetExtCount")]
        public static int X509GetExtCount(IntPtr x) => X509_get_ext_count(x);

        [RuntimeExport("CryptoNative_X509GetExt", Trigger + "CryptoNative_X509GetExt")]
        public static IntPtr X509GetExt(IntPtr x, int loc) => X509_get_ext(x, loc);

        [RuntimeExport("CryptoNative_X509ExtensionGetOid", Trigger + "CryptoNative_X509ExtensionGetOid")]
        public static IntPtr X509ExtensionGetOid(IntPtr x) => X509_EXTENSION_get_object(x);

        [RuntimeExport("CryptoNative_X509ExtensionGetData", Trigger + "CryptoNative_X509ExtensionGetData")]
        public static IntPtr X509ExtensionGetData(IntPtr x) => X509_EXTENSION_get_data(x);

        [RuntimeExport("CryptoNative_X509ExtensionGetCritical", Trigger + "CryptoNative_X509ExtensionGetCritical")]
        public static int X509ExtensionGetCritical(IntPtr x) => X509_EXTENSION_get_critical(x);

        [RuntimeExport("CryptoNative_X509FindExtensionData", Trigger + "CryptoNative_X509FindExtensionData")]
        public static IntPtr X509FindExtensionData(IntPtr x, int nid)
        {
            ERR_clear_error();
            if (x == IntPtr.Zero || nid == 0) return IntPtr.Zero;
            int idx = X509_get_ext_by_NID(x, nid, -1);
            if (idx < 0) return IntPtr.Zero;
            IntPtr ext = X509_get_ext(x, idx);
            return ext == IntPtr.Zero ? IntPtr.Zero : X509_EXTENSION_get_data(ext);
        }

        [RuntimeExport("CryptoNative_X509UpRef", Trigger + "CryptoNative_X509UpRef")]
        public static IntPtr X509UpRef(IntPtr x509)
        {
            if (x509 != IntPtr.Zero) X509_up_ref(x509);
            return x509;
        }

        [RuntimeExport("CryptoNative_GetX509SubjectPublicKeyInfoDerSize", Trigger + "CryptoNative_GetX509SubjectPublicKeyInfoDerSize")]
        public static int GetX509SubjectPublicKeyInfoDerSize(IntPtr x509)
        {
            ERR_clear_error();
            if (x509 == IntPtr.Zero) return 0;
            return i2d_X509_PUBKEY(X509_get_X509_PUBKEY(x509), null);
        }

        [RuntimeExport("CryptoNative_EncodeX509SubjectPublicKeyInfo", Trigger + "CryptoNative_EncodeX509SubjectPublicKeyInfo")]
        public static int EncodeX509SubjectPublicKeyInfo(IntPtr x509, byte* buf)
        {
            ERR_clear_error();
            if (x509 == IntPtr.Zero) return 0;
            return i2d_X509_PUBKEY(X509_get_X509_PUBKEY(x509), &buf);
        }

        // ---- stores and chains -----------------------------------------------------------------------------------------
        [RuntimeExport("CryptoNative_X509StoreDestroy", Trigger + "CryptoNative_X509StoreDestroy")]
        public static void X509StoreDestroy(IntPtr v)
        {
            if (v != IntPtr.Zero) X509_STORE_free(v);
        }

        [RuntimeExport("CryptoNative_X509StoreAddCrl", Trigger + "CryptoNative_X509StoreAddCrl")]
        public static int X509StoreAddCrl(IntPtr ctx, IntPtr x)
        {
            ERR_clear_error();
            return X509_STORE_add_crl(ctx, x);
        }

        [RuntimeExport("CryptoNative_X509StoreSetRevocationFlag", Trigger + "CryptoNative_X509StoreSetRevocationFlag")]
        public static int X509StoreSetRevocationFlag(IntPtr ctx, int revocationFlag)
        {
            // X509RevocationFlag.EndCertificateOnly is 1.
            ulong verifyFlags = 0x4; // X509_V_FLAG_CRL_CHECK
            if (revocationFlag != 1) verifyFlags |= 0x8; // X509_V_FLAG_CRL_CHECK_ALL
            return X509_STORE_set_flags(ctx, verifyFlags);
        }

        [RuntimeExport("CryptoNative_X509StoreCtxCreate", Trigger + "CryptoNative_X509StoreCtxCreate")]
        public static IntPtr X509StoreCtxCreate()
        {
            ERR_clear_error();
            return X509_STORE_CTX_new();
        }

        [RuntimeExport("CryptoNative_X509StoreCtxDestroy", Trigger + "CryptoNative_X509StoreCtxDestroy")]
        public static void X509StoreCtxDestroy(IntPtr v)
        {
            if (v != IntPtr.Zero) X509_STORE_CTX_free(v);
        }

        [RuntimeExport("CryptoNative_X509StoreCtxInit", Trigger + "CryptoNative_X509StoreCtxInit")]
        public static int X509StoreCtxInit(IntPtr ctx, IntPtr store, IntPtr x509, IntPtr extraStore)
        {
            ERR_clear_error();
            int val = X509_STORE_CTX_init(ctx, store, x509, extraStore);
            // Self-signed certificates get their signature checked too, as .NET wants it.
            if (val != 0) X509_STORE_CTX_set_flags(ctx, 0x4000 /* X509_V_FLAG_CHECK_SS_SIGNATURE */);
            return val;
        }

        [RuntimeExport("CryptoNative_X509VerifyCert", Trigger + "CryptoNative_X509VerifyCert")]
        public static int X509VerifyCert(IntPtr ctx)
        {
            ERR_clear_error();
            return X509_verify_cert(ctx);
        }

        [RuntimeExport("CryptoNative_X509StoreCtxGetChain", Trigger + "CryptoNative_X509StoreCtxGetChain")]
        public static IntPtr X509StoreCtxGetChain(IntPtr ctx)
        {
            ERR_clear_error();
            return X509_STORE_CTX_get1_chain(ctx);
        }

        [RuntimeExport("CryptoNative_X509StoreCtxGetCurrentCert", Trigger + "CryptoNative_X509StoreCtxGetCurrentCert")]
        public static IntPtr X509StoreCtxGetCurrentCert(IntPtr ctx)
        {
            if (ctx == IntPtr.Zero) return IntPtr.Zero;
            IntPtr cert = X509_STORE_CTX_get_current_cert(ctx);
            if (cert != IntPtr.Zero) X509_up_ref(cert);
            return cert;
        }

        [RuntimeExport("CryptoNative_X509StoreCtxGetSharedUntrusted", Trigger + "CryptoNative_X509StoreCtxGetSharedUntrusted")]
        public static IntPtr X509StoreCtxGetSharedUntrusted(IntPtr ctx) => ctx != IntPtr.Zero ? X509_STORE_CTX_get0_untrusted(ctx) : IntPtr.Zero;

        [RuntimeExport("CryptoNative_X509StoreCtxGetError", Trigger + "CryptoNative_X509StoreCtxGetError")]
        public static int X509StoreCtxGetError(IntPtr ctx) => X509_STORE_CTX_get_error(ctx);

        [RuntimeExport("CryptoNative_X509StoreCtxReset", Trigger + "CryptoNative_X509StoreCtxReset")]
        public static int X509StoreCtxReset(IntPtr ctx)
        {
            ERR_clear_error();
            IntPtr leaf = X509_STORE_CTX_get0_cert(ctx);
            IntPtr untrusted = X509_STORE_CTX_get0_untrusted(ctx);
            IntPtr store = X509_STORE_CTX_get0_store(ctx);
            X509_STORE_CTX_cleanup(ctx);
            return X509StoreCtxInit(ctx, store, leaf, untrusted);
        }

        [RuntimeExport("CryptoNative_X509StoreCtxRebuildChain", Trigger + "CryptoNative_X509StoreCtxRebuildChain")]
        public static int X509StoreCtxRebuildChain(IntPtr ctx)
        {
            if (X509StoreCtxReset(ctx) == 0) return -1;
            return X509_verify_cert(ctx);
        }

        // The callback is the framework's [UnmanagedCallersOnly] function; app data is ex-data slot 0 (X509_STORE_CTX_set_app_data).
        [RuntimeExport("CryptoNative_X509StoreCtxSetVerifyCallback", Trigger + "CryptoNative_X509StoreCtxSetVerifyCallback")]
        public static int X509StoreCtxSetVerifyCallback(IntPtr ctx, IntPtr callback, IntPtr appData)
        {
            ERR_clear_error();
            X509_STORE_CTX_set_verify_cb(ctx, callback);
            return X509_STORE_CTX_set_ex_data(ctx, 0, appData);
        }

        [RuntimeExport("CryptoNative_X509StoreCtxGetAppData", Trigger + "CryptoNative_X509StoreCtxGetAppData")]
        public static IntPtr X509StoreCtxGetAppData(IntPtr ctx) => X509_STORE_CTX_get_ex_data(ctx, 0);

        [RuntimeExport("CryptoNative_X509StoreCtxGetErrorDepth", Trigger + "CryptoNative_X509StoreCtxGetErrorDepth")]
        public static int X509StoreCtxGetErrorDepth(IntPtr ctx) => X509_STORE_CTX_get_error_depth(ctx);

        [RuntimeExport("CryptoNative_X509VerifyCertErrorString", Trigger + "CryptoNative_X509VerifyCertErrorString")]
        public static byte* X509VerifyCertErrorString(int n) => X509_verify_cert_error_string(n);

        [RuntimeExport("CryptoNative_X509CrlDestroy", Trigger + "CryptoNative_X509CrlDestroy")]
        public static void X509CrlDestroy(IntPtr a)
        {
            if (a != IntPtr.Zero) X509_CRL_free(a);
        }

        [RuntimeExport("CryptoNative_PemWriteBioX509Crl", Trigger + "CryptoNative_PemWriteBioX509Crl")]
        public static int PemWriteBioX509Crl(IntPtr bio, IntPtr crl)
        {
            ERR_clear_error();
            return PEM_write_bio_X509_CRL(bio, crl);
        }

        [RuntimeExport("CryptoNative_PemReadBioX509Crl", Trigger + "CryptoNative_PemReadBioX509Crl")]
        public static IntPtr PemReadBioX509Crl(IntPtr bio)
        {
            ERR_clear_error();
            return PEM_read_bio_X509_CRL(bio, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        [RuntimeExport("CryptoNative_X509ChainNew", Trigger + "CryptoNative_X509ChainNew")]
        public static IntPtr X509ChainNew(IntPtr systemTrust, IntPtr userTrust)
        {
            ERR_clear_error();
            IntPtr store = X509_STORE_new();
            if (store == IntPtr.Zero) return IntPtr.Zero;
            if (systemTrust != IntPtr.Zero)
            {
                int count = OPENSSL_sk_num(systemTrust);
                for (int i = 0; i < count; i++)
                    if (X509_STORE_add_cert(store, OPENSSL_sk_value(systemTrust, i)) == 0)
                    {
                        X509_STORE_free(store);
                        return IntPtr.Zero;
                    }
            }
            if (userTrust != IntPtr.Zero)
            {
                int count = OPENSSL_sk_num(userTrust);
                bool clearError = false;
                for (int i = 0; i < count; i++)
                    if (X509_STORE_add_cert(store, OPENSSL_sk_value(userTrust, i)) == 0)
                    {
                        // A user-trusted certificate the system already trusts is not an error.
                        if (ERR_peek_last_error() != ERR_PACK_X509_CERT_ALREADY_IN_HASH_TABLE)
                        {
                            X509_STORE_free(store);
                            return IntPtr.Zero;
                        }
                        clearError = true;
                    }
                if (clearError) ERR_clear_error();
            }
            return store;
        }

        // The public certificates of the .pfx files in a directory (the CurrentUser stores, ~/.dotnet/corefx/cryptography/x509stores):
        // OpenSSL decodes each file, as the shim does; nothing here reads their contents.
        [RuntimeExport("CryptoNative_X509StackAddDirectoryStore", Trigger + "CryptoNative_X509StackAddDirectoryStore")]
        public static int X509StackAddDirectoryStore(IntPtr stack, byte* storePath)
        {
            if (stack == IntPtr.Zero || storePath == null) return -1;
            ERR_clear_error();
            int clearError = 1;
            IntPtr dir = opendir(storePath);
            if (dir == IntPtr.Zero) return clearError;
            nuint storePathLen = Strlen(storePath);
            nuint allocSize = storePathLen + NAME_MAX + 2;
            byte* pathTmp = (byte*)NativeMemory.AllocZeroed(allocSize);
            if (pathTmp == null)
            {
                closedir(dir);
                return clearError;
            }
            Buffer.MemoryCopy(storePath, pathTmp, (long)allocSize, (long)storePathLen);
            pathTmp[storePathLen] = (byte)'/';
            byte* nextFileWrite = pathTmp + storePathLen + 1;
            nuint remaining = allocSize - storePathLen - 1;
            IntPtr tmpStack = OPENSSL_sk_new_null();
            if (tmpStack == IntPtr.Zero)
            {
                NativeMemory.Free(pathTmp);
                closedir(dir);
                return 0;
            }
            IntPtr cert;
            while ((cert = ReadNextPublicCert(dir, tmpStack, pathTmp, nextFileWrite, remaining)) != IntPtr.Zero)
            {
                if (OPENSSL_sk_push(stack, cert) == 0)
                {
                    clearError = 0;
                    X509_free(cert);
                    break;
                }
            }
            OPENSSL_sk_free(tmpStack);
            NativeMemory.Free(pathTmp);
            closedir(dir);
            if (clearError != 0) ERR_clear_error();
            return clearError;
        }

        private static IntPtr ReadNextPublicCert(IntPtr dir, IntPtr tmpStack, byte* pathTmp, byte* nextFileWrite, nuint remaining)
        {
            byte* entry;
            while ((entry = readdir(dir)) != null)
            {
                byte* name = entry + DirentNameOffset;
                nuint len = 0;
                while (len < NAME_MAX && name[len] != 0) len++;
                if (len <= 4 || !EndsWithPfx(name + len - 4)) continue;
                // Leave room for the terminator.
                if (len >= remaining) continue;
                Buffer.MemoryCopy(name, nextFileWrite, (long)remaining, (long)len);
                nextFileWrite[len] = 0;
                IntPtr bio;
                fixed (byte* mode = "r\0"u8) bio = BIO_new_file(pathTmp, mode);
                if (bio == IntPtr.Zero) continue;
                IntPtr p12 = d2i_PKCS12_bio(bio, IntPtr.Zero);
                BIO_free(bio);
                if (p12 == IntPtr.Zero) continue;
                IntPtr key = IntPtr.Zero, cert = IntPtr.Zero;
                if (PKCS12_parse(p12, null, &key, &cert, &tmpStack) != 0)
                {
                    if (key != IntPtr.Zero) EVP_PKEY_free(key);
                    if (cert == IntPtr.Zero && OPENSSL_sk_num(tmpStack) > 0)
                    {
                        cert = OPENSSL_sk_value(tmpStack, 0);
                        X509_up_ref(cert);
                    }
                }
                IntPtr popped;
                while ((popped = OPENSSL_sk_pop(tmpStack)) != IntPtr.Zero) X509_free(popped);
                PKCS12_free(p12);
                if (cert != IntPtr.Zero) return cert;
            }
            return IntPtr.Zero;
        }

        private static bool EndsWithPfx(byte* s) =>
            s[0] == (byte)'.' && (s[1] | 0x20) == (byte)'p' && (s[2] | 0x20) == (byte)'f' && (s[3] | 0x20) == (byte)'x';

        [RuntimeExport("CryptoNative_X509StackAddMultiple", Trigger + "CryptoNative_X509StackAddMultiple")]
        public static int X509StackAddMultiple(IntPtr dest, IntPtr src)
        {
            if (dest == IntPtr.Zero) return -1;
            ERR_clear_error();
            int success = 1;
            if (src != IntPtr.Zero)
            {
                int count = OPENSSL_sk_num(src);
                for (int i = 0; i < count; i++)
                {
                    IntPtr cert = OPENSSL_sk_value(src, i);
                    X509_up_ref(cert);
                    if (OPENSSL_sk_push(dest, cert) == 0)
                    {
                        success = 0;
                        break;
                    }
                }
            }
            return success;
        }

        [RuntimeExport("CryptoNative_X509StoreCtxCommitToChain", Trigger + "CryptoNative_X509StoreCtxCommitToChain")]
        public static int X509StoreCtxCommitToChain(IntPtr storeCtx)
        {
            if (storeCtx == IntPtr.Zero) return -1;
            ERR_clear_error();
            IntPtr chain = X509_STORE_CTX_get1_chain(storeCtx);
            if (chain == IntPtr.Zero) return 0;
            IntPtr untrusted = X509_STORE_CTX_get0_untrusted(storeCtx);
            IntPtr leaf = X509_STORE_CTX_get0_cert(storeCtx);
            IntPtr cur;
            while ((cur = OPENSSL_sk_pop(untrusted)) != IntPtr.Zero) X509_free(cur);
            while ((cur = OPENSSL_sk_pop(chain)) != IntPtr.Zero)
            {
                if (cur == leaf)
                {
                    // Undo the reference get1_chain took.
                    X509_free(cur);
                }
                else if (OPENSSL_sk_push(untrusted, cur) == 0)
                {
                    X509PutMallocFailure();
                    X509_free(cur);
                    while ((cur = OPENSSL_sk_pop(chain)) != IntPtr.Zero) X509_free(cur);
                    OPENSSL_sk_free(chain);
                    return 0;
                }
            }
            OPENSSL_sk_free(chain);
            return 1;
        }

        [RuntimeExport("CryptoNative_X509StoreCtxResetForSignatureError", Trigger + "CryptoNative_X509StoreCtxResetForSignatureError")]
        public static int X509StoreCtxResetForSignatureError(IntPtr storeCtx, IntPtr* newStore)
        {
            if (storeCtx == IntPtr.Zero || newStore == null) return -1;
            *newStore = IntPtr.Zero;
            ERR_clear_error();
            int errorDepth = X509_STORE_CTX_get_error_depth(storeCtx);
            IntPtr chain = X509_STORE_CTX_get0_chain(storeCtx);
            int chainLength = OPENSSL_sk_num(chain);
            IntPtr store = X509_STORE_CTX_get0_store(storeCtx);
            // A self-signed root with a bad signature: build again against a store holding only a copy of it, so the copy (not the
            // trusted object) carries the error.
            if (chainLength - 1 == errorDepth)
            {
                IntPtr last = OPENSSL_sk_value(chain, errorDepth);
                IntPtr root;
                if (X509_STORE_CTX_get1_issuer(&root, storeCtx, last) != 0)
                {
                    if (root == last)
                    {
                        X509_free(root);
                        IntPtr tmpNew = X509_STORE_new();
                        if (tmpNew == IntPtr.Zero) return 0;
                        IntPtr duplicate = X509_dup(last);
                        if (duplicate == IntPtr.Zero)
                        {
                            X509_STORE_free(tmpNew);
                            return 0;
                        }
                        if (X509_STORE_add_cert(tmpNew, duplicate) == 0)
                        {
                            X509_free(duplicate);
                            X509_STORE_free(tmpNew);
                            return 0;
                        }
                        *newStore = tmpNew;
                        store = tmpNew;
                        chainLength--;
                    }
                    else
                    {
                        X509_free(root);
                    }
                }
            }
            IntPtr untrusted = X509_STORE_CTX_get0_untrusted(storeCtx);
            IntPtr cur;
            while ((cur = OPENSSL_sk_pop(untrusted)) != IntPtr.Zero) X509_free(cur);
            for (int i = chainLength - 1; i > 0; --i)
            {
                cur = OPENSSL_sk_value(chain, i);
                if (i <= errorDepth)
                {
                    IntPtr duplicate = X509_dup(cur);
                    if (duplicate == IntPtr.Zero) return 0;
                    if (OPENSSL_sk_push(untrusted, duplicate) == 0)
                    {
                        X509PutMallocFailure();
                        X509_free(duplicate);
                        return 0;
                    }
                }
                else if (OPENSSL_sk_push(untrusted, cur) != 0)
                {
                    X509_up_ref(cur);
                }
                else
                {
                    X509PutMallocFailure();
                    return 0;
                }
            }
            IntPtr leafDup = X509_dup(X509_STORE_CTX_get0_cert(storeCtx));
            if (leafDup == IntPtr.Zero) return 0;
            X509_STORE_CTX_cleanup(storeCtx);
            return X509StoreCtxInit(storeCtx, store, leafDup, untrusted);
        }

        [RuntimeExport("CryptoNative_X509StoreSetVerifyTime", Trigger + "CryptoNative_X509StoreSetVerifyTime")]
        public static int X509StoreSetVerifyTime(IntPtr ctx, long unixTime)
        {
            ERR_clear_error();
            if (ctx == IntPtr.Zero) return 0;
            IntPtr param = X509_STORE_get0_param(ctx);
            if (param == IntPtr.Zero) return 0;
            X509_VERIFY_PARAM_set_time(param, unixTime);
            return 1;
        }

        // ---- OCSP (pal_x509.c) -----------------------------------------------------------------------------------------
        private static byte* BuildOcspCacheFilename(byte* cachePath, IntPtr subject)
        {
            nuint len = Strlen(cachePath);
            // "/" + 8 hex + "." + 8 hex + ".ocsp" + NUL = 24.
            nuint allocSize = len + 24;
            byte* fullPath = (byte*)NativeMemory.AllocZeroed(allocSize);
            if (fullPath == null) return null;
            ulong issuerHash = X509_issuer_name_hash(subject);
            ulong subjectHash = X509_subject_name_hash(subject);
            Buffer.MemoryCopy(cachePath, fullPath, (long)allocSize, (long)len);
            byte* p = fullPath + len;
            *p++ = (byte)'/';
            p = WriteHex8(p, (uint)issuerHash);
            *p++ = (byte)'.';
            p = WriteHex8(p, (uint)subjectHash);
            *p++ = (byte)'.'; *p++ = (byte)'o'; *p++ = (byte)'c'; *p++ = (byte)'s'; *p++ = (byte)'p';
            *p = 0;
            // A failed hash computation leaves an error in the queue; the cache miss it causes is not an error.
            if (issuerHash == 0 || subjectHash == 0) ERR_clear_error();
            return fullPath;
        }

        private static byte* WriteHex8(byte* p, uint value)
        {
            for (int shift = 28; shift >= 0; shift -= 4)
            {
                uint d = (value >> shift) & 0xF;
                *p++ = (byte)(d < 10 ? '0' + d : 'a' + d - 10);
            }
            return p;
        }

        private static IntPtr MakeCertId(IntPtr subject, IntPtr issuer) => OCSP_cert_to_id(EVP_sha1(), subject, issuer);

        private static int CheckOcspGetExpiry(IntPtr req, IntPtr resp, IntPtr subject, IntPtr issuer, IntPtr storeCtx, int* canCache, long* expiry)
        {
            *canCache = 0;
            IntPtr certId = MakeCertId(subject, issuer);
            if (certId == IntPtr.Zero) return -1;
            IntPtr basicResp = OCSP_response_get1_basic(resp);
            int status = 2; // V_OCSP_CERTSTATUS_UNKNOWN
            int ret = PAL_X509_V_ERR_UNABLE_TO_GET_CRL;
            if (basicResp != IntPtr.Zero)
            {
                IntPtr store = X509_STORE_CTX_get0_store(storeCtx);
                IntPtr param = X509_STORE_get0_param(store);
                // The response is verified without the chain's own flags (CRL checks in particular), which are restored after.
                ulong currentFlags = X509_VERIFY_PARAM_get_flags(param);
                X509_VERIFY_PARAM_clear_flags(param, currentFlags);
                IntPtr untrusted = X509_STORE_CTX_get0_untrusted(storeCtx);
                int nonceCheck = req == IntPtr.Zero ? 1 : OCSP_check_nonce(req, basicResp);
                // -1: no nonce in the response; 2: none in either. Both are accepted, as the shim accepts them.
                if (nonceCheck == -1 || nonceCheck == 2) nonceCheck = 1;
                if (nonceCheck == 1 && OCSP_basic_verify(basicResp, untrusted, store, 0x200 /* OCSP_TRUSTOTHER */) > 0)
                {
                    IntPtr thisupd = IntPtr.Zero, nextupd = IntPtr.Zero;
                    if (OCSP_resp_find_status(basicResp, certId, &status, null, null, &thisupd, &nextupd) != 0)
                    {
                        long currentTime = time(null);
                        int nextUpdComparison = nextupd == IntPtr.Zero ? 0 : X509_cmp_time(nextupd, &currentTime);
                        if (status == 1 /* REVOKED */) ret = PAL_X509_V_ERR_CERT_REVOKED;
                        else if (nextupd != IntPtr.Zero && nextUpdComparison <= 0) ret = PAL_X509_V_ERR_CRL_HAS_EXPIRED;
                        else if (status == 0 /* GOOD */) ret = PAL_X509_V_OK;

                        // Cacheable: a definite answer, still current, issued within the last four days.
                        if (ret != PAL_X509_V_ERR_UNABLE_TO_GET_CRL && thisupd != IntPtr.Zero && nextUpdComparison > 0)
                        {
                            long oldest = currentTime - 4 * 24 * 60 * 60;
                            if (X509_cmp_time(thisupd, &oldest) > 0)
                            {
                                *canCache = 1;
                                if (expiry != null)
                                {
                                    byte* tm = stackalloc byte[56]; // struct tm (glibc)
                                    new Span<byte>(tm, 56).Clear();
                                    if (nextupd != IntPtr.Zero && ASN1_TIME_to_tm(nextupd, tm) == 1) *expiry = timegm(tm);
                                    else if (ASN1_TIME_to_tm(thisupd, tm) == 1) *expiry = timegm(tm) + 24 * 60 * 60;
                                }
                            }
                        }
                    }
                }
                X509_STORE_set_flags(store, currentFlags);
                OCSP_BASICRESP_free(basicResp);
            }
            OCSP_CERTID_free(certId);
            return ret;
        }

        private static bool Get0CertAndIssuer(IntPtr storeCtx, int chainDepth, out IntPtr subject, out IntPtr issuer)
        {
            IntPtr chain = X509_STORE_CTX_get0_chain(storeCtx);
            int chainSize = chain == IntPtr.Zero ? 0 : OPENSSL_sk_num(chain);
            subject = issuer = IntPtr.Zero;
            if (chainSize <= chainDepth) return false;
            subject = OPENSSL_sk_value(chain, chainDepth);
            // A self-signed certificate is its own issuer.
            issuer = OPENSSL_sk_value(chain, chainSize == chainDepth + 1 ? chainDepth : chainDepth + 1);
            return true;
        }

        private static int GetStapledOcspStatus(IntPtr storeCtx, IntPtr subject, IntPtr issuer)
        {
            IntPtr ocspResp = X509_get_ex_data(subject, g_x509_ocsp_index);
            if (ocspResp == IntPtr.Zero) return PAL_X509_V_ERR_UNABLE_TO_GET_CRL;
            int canCache = 0;
            return CheckOcspGetExpiry(IntPtr.Zero, ocspResp, subject, issuer, storeCtx, &canCache, null);
        }

        [RuntimeExport("CryptoNative_X509ChainGetCachedOcspStatus", Trigger + "CryptoNative_X509ChainGetCachedOcspStatus")]
        public static int X509ChainGetCachedOcspStatus(IntPtr storeCtx, byte* cachePath, int chainDepth)
        {
            if (storeCtx == IntPtr.Zero || cachePath == null) return -1;
            ERR_clear_error();
            if (!Get0CertAndIssuer(storeCtx, chainDepth, out IntPtr subject, out IntPtr issuer)) return -2;
            if (chainDepth == 0)
            {
                int stapled = GetStapledOcspStatus(storeCtx, subject, issuer);
                if (stapled == PAL_X509_V_OK || stapled == PAL_X509_V_ERR_CERT_REVOKED) return stapled;
            }
            int ret = PAL_X509_V_ERR_UNABLE_TO_GET_CRL;
            byte* fullPath = BuildOcspCacheFilename(cachePath, subject);
            if (fullPath == null) return ret;
            IntPtr resp = IntPtr.Zero;
            IntPtr bio;
            fixed (byte* mode = "rb\0"u8) bio = BIO_new_file(fullPath, mode);
            if (bio != IntPtr.Zero)
            {
                resp = ASN1_item_d2i_bio(OCSP_RESPONSE_it(), bio, IntPtr.Zero);
                BIO_free(bio);
            }
            if (resp != IntPtr.Zero)
            {
                int canCache = 0;
                ret = CheckOcspGetExpiry(IntPtr.Zero, resp, subject, issuer, storeCtx, &canCache, null);
                // A cached answer that could not be cached now is stale.
                if (canCache == 0) ret = PAL_X509_V_ERR_UNABLE_TO_GET_CRL;
            }
            if (ret == PAL_X509_V_ERR_UNABLE_TO_GET_CRL)
            {
                unlink(fullPath);
                ERR_clear_error();
            }
            NativeMemory.Free(fullPath);
            if (resp != IntPtr.Zero) OCSP_RESPONSE_free(resp);
            return ret;
        }

        private static IntPtr BuildOcspRequest(IntPtr subject, IntPtr issuer)
        {
            IntPtr certId = MakeCertId(subject, issuer);
            if (certId == IntPtr.Zero) return IntPtr.Zero;
            IntPtr req = OCSP_REQUEST_new();
            if (req == IntPtr.Zero)
            {
                OCSP_CERTID_free(certId);
                return IntPtr.Zero;
            }
            if (OCSP_request_add0_id(req, certId) == IntPtr.Zero)
            {
                OCSP_CERTID_free(certId);
                OCSP_REQUEST_free(req);
                return IntPtr.Zero;
            }
            return req;
        }

        [RuntimeExport("CryptoNative_X509BuildOcspRequest", Trigger + "CryptoNative_X509BuildOcspRequest")]
        public static IntPtr X509BuildOcspRequest(IntPtr subject, IntPtr issuer)
        {
            ERR_clear_error();
            if (subject == IntPtr.Zero || issuer == IntPtr.Zero) return IntPtr.Zero;
            return BuildOcspRequest(subject, issuer);
        }

        [RuntimeExport("CryptoNative_X509ChainBuildOcspRequest", Trigger + "CryptoNative_X509ChainBuildOcspRequest")]
        public static IntPtr X509ChainBuildOcspRequest(IntPtr storeCtx, int chainDepth)
        {
            if (storeCtx == IntPtr.Zero) return IntPtr.Zero;
            ERR_clear_error();
            if (!Get0CertAndIssuer(storeCtx, chainDepth, out IntPtr subject, out IntPtr issuer)) return IntPtr.Zero;
            return BuildOcspRequest(subject, issuer);
        }

        [RuntimeExport("CryptoNative_X509ChainHasStapledOcsp", Trigger + "CryptoNative_X509ChainHasStapledOcsp")]
        public static int X509ChainHasStapledOcsp(IntPtr storeCtx)
        {
            if (storeCtx == IntPtr.Zero) return -1;
            ERR_clear_error();
            if (!Get0CertAndIssuer(storeCtx, 0, out IntPtr subject, out IntPtr issuer)) return -2;
            int status = GetStapledOcspStatus(storeCtx, subject, issuer);
            return status == PAL_X509_V_OK || status == PAL_X509_V_ERR_CERT_REVOKED ? 1 : 0;
        }

        [RuntimeExport("CryptoNative_X509ChainVerifyOcsp", Trigger + "CryptoNative_X509ChainVerifyOcsp")]
        public static int X509ChainVerifyOcsp(IntPtr storeCtx, IntPtr req, IntPtr resp, byte* cachePath, int chainDepth)
        {
            if (storeCtx == IntPtr.Zero || req == IntPtr.Zero || resp == IntPtr.Zero) return -1;
            ERR_clear_error();
            if (!Get0CertAndIssuer(storeCtx, chainDepth, out IntPtr subject, out IntPtr issuer)) return -2;
            IntPtr certId = MakeCertId(subject, issuer);
            if (certId == IntPtr.Zero) return -3;
            // (The shim builds and leaks this id; it is freed here.)
            OCSP_CERTID_free(certId);
            int canCache = 0;
            int ret = CheckOcspGetExpiry(req, resp, subject, issuer, storeCtx, &canCache, null);
            if (canCache != 0 && cachePath != null)
            {
                byte* fullPath = BuildOcspCacheFilename(cachePath, subject);
                if (fullPath != null)
                {
                    bool clearErr = true;
                    IntPtr bio;
                    fixed (byte* mode = "wb\0"u8) bio = BIO_new_file(fullPath, mode);
                    if (bio != IntPtr.Zero)
                    {
                        if (ASN1_item_i2d_bio(OCSP_RESPONSE_it(), bio, resp) != 0) clearErr = false;
                        BIO_free(bio);
                    }
                    if (clearErr)
                    {
                        ERR_clear_error();
                        unlink(fullPath);
                    }
                    NativeMemory.Free(fullPath);
                }
            }
            return ret;
        }

        [RuntimeExport("CryptoNative_X509DecodeOcspToExpiration", Trigger + "CryptoNative_X509DecodeOcspToExpiration")]
        public static int X509DecodeOcspToExpiration(byte* buf, int len, IntPtr req, IntPtr subject, IntPtr* issuers, int issuersLen, long* expiration)
        {
            ERR_clear_error();
            if (buf == null || len <= 0 || issuersLen <= 0 || issuers == null) return 0;
            IntPtr resp = d2i_OCSP_RESPONSE(IntPtr.Zero, &buf, len);
            if (resp == IntPtr.Zero) return 0;
            IntPtr store = X509_STORE_new();
            IntPtr ctx = IntPtr.Zero, bag = IntPtr.Zero;
            if (store != IntPtr.Zero) bag = OPENSSL_sk_new_null();
            if (bag != IntPtr.Zero)
            {
                int i;
                for (i = 0; i < issuersLen; i++)
                    if (X509_STORE_add_cert(store, issuers[i]) == 0 || OPENSSL_sk_push(bag, issuers[i]) == 0) break;
                if (i == issuersLen) ctx = X509_STORE_CTX_new();
            }
            int ret = 0;
            if (ctx != IntPtr.Zero)
            {
                if (X509_STORE_CTX_init(ctx, store, subject, bag) != 0)
                {
                    int canCache = 0;
                    long expirationT = 0;
                    int code = CheckOcspGetExpiry(req, resp, subject, issuers[0], ctx, &canCache, &expirationT);
                    *expiration = expirationT;
                    if (code == PAL_X509_V_OK || code == PAL_X509_V_ERR_CERT_REVOKED) ret = 1;
                }
                X509_STORE_CTX_free(ctx);
            }
            if (bag != IntPtr.Zero) OPENSSL_sk_free(bag);
            if (store != IntPtr.Zero) X509_STORE_free(store);
            OCSP_RESPONSE_free(resp);
            return ret;
        }

        // ---- the root store's location (pal_x509_root.c) ---------------------------------------------------------------
        [RuntimeExport("CryptoNative_GetX509RootStorePath", Trigger + "CryptoNative_GetX509RootStorePath")]
        public static byte* GetX509RootStorePath(byte* defaultPath)
        {
            byte* dir = (byte*)getenv((byte*)X509_get_default_cert_dir_env());
            *defaultPath = 0;
            if (dir == null)
            {
                dir = (byte*)X509_get_default_cert_dir();
                *defaultPath = 1;
            }
            return dir;
        }

        [RuntimeExport("CryptoNative_GetX509RootStoreFile", Trigger + "CryptoNative_GetX509RootStoreFile")]
        public static byte* GetX509RootStoreFile(byte* defaultPath)
        {
            byte* file = (byte*)getenv((byte*)X509_get_default_cert_file_env());
            *defaultPath = 0;
            if (file == null)
            {
                file = (byte*)X509_get_default_cert_file();
                *defaultPath = 1;
            }
            return file;
        }
    }
}
#endif
