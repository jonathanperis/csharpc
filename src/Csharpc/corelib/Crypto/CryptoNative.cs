// libSystem.Security.Cryptography.Native.OpenSsl's C API, the part the framework's System.Security.Cryptography and System.Net.Security IL
// P/Invokes on Linux, served by csharpc over OpenSSL 3's libcrypto and libssl instead of the pack's shim (linking the shim moves the runtime
// boundary, an owner decision: docs/21-ownership.md, ADR 0024). Each function is exported under the shim's symbol name ([RuntimeExport];
// ModuleCodeGen.PInvoke binds the framework's `[LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_X")]` to it), so the
// framework's SafeHandle reference counting, marshalling and error mapping (Interop.Crypto.CreateOpenSslCryptographicException) run unchanged;
// only the native layer is csharpc's.
//
// The semantics are the shim's (dotnet/runtime src/native/libs/System.Security.Cryptography.Native, the .NET 11 GA source), function by
// function: the error queue is cleared exactly where the shim clears it (ERR_clear_error before each operation whose failure the framework
// reports from the queue), allocation failures the shim reports itself are pushed as ERR_R_MALLOC_FAILURE as there, and the OpenSSL version
// workarounds are the shim's (HMAC keys kept for OpenSSL 3.0.0 to 3.0.2, the zero-length XOF finalization). OpenSSL is version 3 (libcrypto.so.3
// and libssl.so.3, as the corelib's own bindings in OpenSsl.cs; the shim also accepts 1.1, which no supported distribution still ships as its
// only OpenSSL); functions added in later 3.x releases are looked up by name and treated as missing when absent, as the shim's API_EXISTS does.
// Security (docs/il-first/System.Security.Cryptography.md, "Security review"): nothing here parses data (OpenSSL does, as under .NET); every
// buffer is bounded by a length the caller passed, or by the size the framework read from OpenSSL for it (a digest's EVP_MD_get_size);
// negative lengths are rejected before they reach a size_t.
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        private const string Trigger = "libSystem.Security.Cryptography.Native.OpenSsl!";
        internal const string LibCrypto = "libcrypto.so.3";
        internal const string LibSsl = "libssl.so.3";

        // ---- OpenSSL constants (OpenSSL 3 headers) ---------------------------------------------------------------------
        private const int ERR_LIB_EVP = 6;
        private const int ERR_R_MALLOC_FAILURE = 0xC0100;
        private const int ERR_R_UNSUPPORTED = 0x8010C;
        private const ulong ERR_SYSTEM_FLAG = 0x80000000, ERR_SYSTEM_MASK = 0x7FFFFFFF, ERR_REASON_MASK = 0x7FFFFF;
        private const long OPENSSL_VERSION_3_0_RTM = 0x30000000L, OPENSSL_VERSION_3_0_2_RTM = 0x30000020L;

        // OSSL_PARAM (openssl/core.h): { const char* key; unsigned int data_type; void* data; size_t data_size; size_t return_size; }.
        [StructLayout(LayoutKind.Sequential)]
        internal struct OsslParam
        {
            public byte* Key;
            public uint DataType;
            public void* Data;
            public nuint DataSize;
            public nuint ReturnSize;
        }
        private const uint OSSL_PARAM_INTEGER = 1, OSSL_PARAM_UTF8_STRING = 4, OSSL_PARAM_OCTET_STRING = 5;
        private static readonly nuint OSSL_PARAM_UNMODIFIED = nuint.MaxValue;

        // The OSSL_PARAM_construct_* helpers, which return the struct by value: the same fields, filled here.
        internal static OsslParam OctetParam(byte* key, void* data, nuint size) =>
            new OsslParam { Key = key, DataType = OSSL_PARAM_OCTET_STRING, Data = data, DataSize = size, ReturnSize = OSSL_PARAM_UNMODIFIED };
        // A size of 0 means "the string's length", as OSSL_PARAM_construct_utf8_string computes it.
        internal static OsslParam Utf8Param(byte* key, byte* value) =>
            new OsslParam { Key = key, DataType = OSSL_PARAM_UTF8_STRING, Data = value, DataSize = Strlen(value), ReturnSize = OSSL_PARAM_UNMODIFIED };
        internal static OsslParam Int32Param(byte* key, int* value) =>
            new OsslParam { Key = key, DataType = OSSL_PARAM_INTEGER, Data = value, DataSize = 4, ReturnSize = OSSL_PARAM_UNMODIFIED };
        internal static OsslParam EndParam() => default;

        internal static nuint Strlen(byte* s)
        {
            nuint n = 0;
            while (s[n] != 0) n++;
            return n;
        }

        // ---- libcrypto: initialization, errors, random ------------------------------------------------------------------
        [DllImport(LibSsl)] private static extern int OPENSSL_init_ssl(ulong opts, IntPtr settings);
        [DllImport(LibCrypto)] private static extern int CRYPTO_get_ex_new_index(int classIndex, long argl, void* argp, void* newFunc, void* dupFunc, void* freeFunc);
        [DllImport(LibCrypto)] private static extern ulong OpenSSL_version_num();
        [DllImport(LibCrypto)] internal static extern void ERR_clear_error();
        [DllImport(LibCrypto)] private static extern ulong ERR_peek_error();
        [DllImport(LibCrypto)] private static extern ulong ERR_peek_last_error();
        [DllImport(LibCrypto)] private static extern byte* ERR_reason_error_string(ulong e);
        [DllImport(LibCrypto)] private static extern void ERR_error_string_n(ulong e, byte* buf, nuint len);
        [DllImport(LibCrypto)] private static extern void ERR_put_error(int lib, int func, int reason, byte* file, int line);
        [DllImport(LibCrypto)] private static extern int RAND_bytes(byte* buf, int num);
        [DllImport(LibCrypto)] private static extern IntPtr OSSL_PROVIDER_try_load(IntPtr libctx, byte* name, int retainFallbacks);
        [DllImport(LibCrypto)] internal static extern void* CRYPTO_zalloc(nuint num, byte* file, int line);
        [DllImport(LibCrypto)] internal static extern void* CRYPTO_malloc(nuint num, byte* file, int line);
        [DllImport(LibCrypto)] internal static extern void CRYPTO_free(void* ptr, byte* file, int line);
        [DllImport(LibCrypto)] internal static extern void CRYPTO_clear_free(void* ptr, nuint num, byte* file, int line);
        [DllImport(LibCrypto)] internal static extern void OPENSSL_cleanse(void* ptr, nuint len);

        internal static void* OpensslZalloc(nuint size) => CRYPTO_zalloc(size, null, 0);
        internal static void* OpensslMalloc(nuint size) => CRYPTO_malloc(size, null, 0);
        internal static void OpensslFree(void* p) => CRYPTO_free(p, null, 0);

        // ERR_GET_REASON (openssl/err.h, OpenSSL 3): system errors keep their errno, the others the reason bits.
        internal static int ErrGetReason(ulong e) => (int)((e & ERR_SYSTEM_FLAG) != 0 ? e & ERR_SYSTEM_MASK : e & ERR_REASON_MASK);

        // The shim's ERR_put_error(ERR_LIB_EVP, 0, ERR_R_MALLOC_FAILURE, __FILE__, __LINE__) after an allocation OpenSSL does not report itself.
        internal static void PutMallocFailure() => ERR_put_error(ERR_LIB_EVP, 0, ERR_R_MALLOC_FAILURE, null, 0);

        // Functions of later OpenSSL 3.x releases, looked up once (the shim's API_EXISTS): null when this libcrypto lacks them.
        private static IntPtr s_libCrypto;
        internal static IntPtr OptionalExport(string name)
        {
            if (s_libCrypto == IntPtr.Zero && !NativeLibrary.TryLoad(LibCrypto, out s_libCrypto)) return IntPtr.Zero;
            return NativeLibrary.TryGetExport(s_libCrypto, name, out IntPtr address) ? address : IntPtr.Zero;
        }

        // ---- initialization (openssl.c) --------------------------------------------------------------------------------
        private static readonly object s_initLock = new object();
        private static volatile int s_initState; // 0: not run; 1: done
        private static int s_initStatus = 1;
        internal static int g_x509_ocsp_index = -1, g_ssl_sess_cert_index = -1;

        [RuntimeExport("CryptoNative_EnsureOpenSslInitialized", Trigger + "CryptoNative_EnsureOpenSslInitialized")]
        public static int EnsureOpenSslInitialized()
        {
            // pthread_once in the shim: the first caller initializes, the others wait for it.
            if (s_initState == 0)
                lock (s_initLock)
                    if (s_initState == 0)
                    {
                        s_initStatus = EnsureOpenSslInitializedCore();
                        s_initState = 1;
                    }
            return s_initStatus;
        }

        private static int EnsureOpenSslInitializedCore()
        {
            const ulong common = 0x4 /* ADD_ALL_CIPHERS */ | 0x8 /* ADD_ALL_DIGESTS */ | 0x80000 /* NO_ATEXIT */
                | 0x2 /* LOAD_CRYPTO_STRINGS */ | 0x200000 /* LOAD_SSL_STRINGS */;
            // With the configuration first; a malformed openssl.cnf must not stop the program, so again without it, as the shim does.
            if (OPENSSL_init_ssl(common | 0x40 /* LOAD_CONFIG */, IntPtr.Zero) == 0 && OPENSSL_init_ssl(common, IntPtr.Zero) == 0)
                return 1;
            // The ex-data slots the shim registers: an OCSP response stapled to an X509 (CRYPTO_EX_INDEX_X509 = 3), freed with the
            // certificate and never shared by a copy, and the certificate of a TLS session (CRYPTO_EX_INDEX_SSL_SESSION = 2), an opaque
            // pointer copied as is (OpenSSL's default when the callbacks do nothing).
            g_x509_ocsp_index = CRYPTO_get_ex_new_index(3, 0, null, null, (void*)(delegate* unmanaged<IntPtr, IntPtr, void**, int, long, IntPtr, int>)&ExDataDupOcspResponse,
                (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, long, IntPtr, void>)&ExDataFreeOcspResponse);
            g_ssl_sess_cert_index = CRYPTO_get_ex_new_index(2, 0, null, null, null, null);
            return 0;
        }

        [UnmanagedCallersOnly]
        private static void ExDataFreeOcspResponse(IntPtr parent, IntPtr ptr, IntPtr ad, int idx, long argl, IntPtr argp)
        {
            if (ptr != IntPtr.Zero && idx == g_x509_ocsp_index) OCSP_RESPONSE_free(ptr);
        }

        // A duplicated X509 does not share the response (it would be freed twice).
        [UnmanagedCallersOnly]
        private static int ExDataDupOcspResponse(IntPtr to, IntPtr from, void** fromD, int idx, long argl, IntPtr argp)
        {
            if (fromD != null && idx == g_x509_ocsp_index) *fromD = null;
            return 1;
        }

        [RuntimeExport("CryptoNative_OpenSslAvailable", Trigger + "CryptoNative_OpenSslAvailable")]
        public static int OpenSslAvailable() => NativeLibrary.TryLoad(LibSsl, out _) ? 1 : 0;

        [RuntimeExport("CryptoNative_OpenSslVersionNumber", Trigger + "CryptoNative_OpenSslVersionNumber")]
        public static long OpenSslVersionNumber() => (long)OpenSSL_version_num();

        [RuntimeExport("CryptoNative_RegisterLegacyAlgorithms", Trigger + "CryptoNative_RegisterLegacyAlgorithms")]
        public static void RegisterLegacyAlgorithms()
        {
            fixed (byte* legacy = "legacy\0"u8)
                OSSL_PROVIDER_try_load(IntPtr.Zero, legacy, 1);
            // Whether it loaded or not.
            ERR_clear_error();
        }

        // ---- errors (pal_err.c) ----------------------------------------------------------------------------------------
        // The shim guards the string table against OpenSSL's atexit unload; csharpc initializes OpenSSL with NO_ATEXIT and never unloads it.
        [RuntimeExport("CryptoNative_ErrClearError", Trigger + "CryptoNative_ErrClearError")]
        public static void ErrClearError() => ERR_clear_error();

        [RuntimeExport("CryptoNative_ErrGetExceptionError", Trigger + "CryptoNative_ErrGetExceptionError")]
        public static ulong ErrGetExceptionError(int* isAllocFailure)
        {
            ulong err = ERR_peek_last_error();
            if (isAllocFailure != null) *isAllocFailure = ErrGetReason(err) == ERR_R_MALLOC_FAILURE ? 1 : 0;
            // The one wanted is taken: clear the rest.
            ERR_clear_error();
            return err;
        }

        [RuntimeExport("CryptoNative_ErrPeekError", Trigger + "CryptoNative_ErrPeekError")]
        public static ulong ErrPeekError() => ERR_peek_error();

        [RuntimeExport("CryptoNative_ErrPeekLastError", Trigger + "CryptoNative_ErrPeekLastError")]
        public static ulong ErrPeekLastError() => ERR_peek_last_error();

        [RuntimeExport("CryptoNative_ErrReasonErrorString", Trigger + "CryptoNative_ErrReasonErrorString")]
        public static byte* ErrReasonErrorString(ulong error) => ERR_reason_error_string(error);

        [RuntimeExport("CryptoNative_ErrErrorStringN", Trigger + "CryptoNative_ErrErrorStringN")]
        public static void ErrErrorStringN(ulong e, byte* buf, int len)
        {
            if (buf == null || len <= 0) return;
            ERR_error_string_n(e, buf, (nuint)len);
        }

        // ---- random (openssl.c) ----------------------------------------------------------------------------------------
        [RuntimeExport("CryptoNative_GetRandomBytes", Trigger + "CryptoNative_GetRandomBytes")]
        public static int GetRandomBytes(byte* buf, int num)
        {
            if (num < 0) return 0;
            ERR_clear_error();
            return RAND_bytes(buf, num) == 1 ? 1 : 0;
        }
    }
}
#endif
