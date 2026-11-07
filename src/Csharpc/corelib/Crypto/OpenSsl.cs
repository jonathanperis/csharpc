// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// OpenSSL 3 (libcrypto.so.3 and libssl.so.3; libcrypto-3-x64.dll and libssl-3-x64.dll on Windows) as .NET's Linux cryptography stack reaches it, for X.509 certificates,
// chain building and TLS: the calls are those of libSystem.Security.Cryptography.Native.OpenSsl's shim, made
// directly (docs/adr/0022-tls.md; Rsa.cs and AesNative.cs bind libcrypto the same way). The libraries are
// opened on first use, so programs that don't use TLS or certificates never load them.
#nullable disable
#if CSHARPC_OPENSSL
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
#if CSHARPC_TARGET_WINDOWS
// C's long and unsigned long are 32 bits on Windows (LLP64): OpenSSL's `long` parameters and results (d2i_*
// lengths, the SSL_ctrl family, ERR_get_error) are declared with these, so the calls match the DLL's ABI.
using OsslLong = System.Int32;
using OsslULong = System.UInt32;
#else
using OsslLong = System.Int64;
using OsslULong = System.UInt64;
#endif

namespace CsharpcCoreLib
{
    internal static unsafe class Ossl
    {
#if CSHARPC_TARGET_WINDOWS
        // OpenSSL 3's own DLL names (the official builds and MSYS2's): found next to the executable, then on PATH,
        // like any LoadLibrary; a program that uses no certificate or TLS type never loads them.
        internal const string Crypto = "libcrypto-3-x64.dll";
        internal const string Ssl = "libssl-3-x64.dll";
#elif CSHARPC_TARGET_MACOS
        // macOS ships no OpenSSL 3 (its libssl is LibreSSL, which system software may not load): Homebrew's openssl@3, whose
        // Apple-silicon prefix is fixed. The absolute path makes a machine without it fail with a DllNotFoundException that
        // names the library, not with a dlopen search through unrelated directories.
        internal const string Crypto = "/opt/homebrew/opt/openssl@3/lib/libcrypto.3.dylib";
        internal const string Ssl = "/opt/homebrew/opt/openssl@3/lib/libssl.3.dylib";
#else
        internal const string Crypto = "libcrypto.so.3";
        internal const string Ssl = "libssl.so.3";
#endif

        // ---- errors ----
        [DllImport(Crypto)] internal static extern OsslULong ERR_get_error();
        [DllImport(Crypto)] internal static extern OsslULong ERR_peek_error();
        [DllImport(Crypto)] internal static extern void ERR_error_string_n(OsslULong e, byte* buf, nuint len);
        [DllImport(Crypto)] internal static extern void ERR_clear_error();

        // ---- memory and file BIOs ----
        [DllImport(Crypto)] internal static extern IntPtr BIO_new_file(byte* filename, byte* mode);
        [DllImport(Crypto)] internal static extern IntPtr BIO_new_mem_buf(byte* buf, int len);
        [DllImport(Crypto)] internal static extern IntPtr BIO_new(IntPtr method);
        [DllImport(Crypto)] internal static extern IntPtr BIO_s_mem();
        [DllImport(Crypto)] internal static extern int BIO_free(IntPtr bio);
        [DllImport(Crypto)] internal static extern int BIO_read(IntPtr bio, byte* data, int len);
        [DllImport(Crypto)] internal static extern int BIO_write(IntPtr bio, byte* data, int len);
        [DllImport(Crypto)] internal static extern nuint BIO_ctrl_pending(IntPtr bio);

        // ---- certificates, keys, PKCS#12 ----
        [DllImport(Crypto)] internal static extern IntPtr d2i_X509(IntPtr* a, byte** pp, OsslLong length);
        [DllImport(Crypto)] internal static extern int i2d_X509(IntPtr x, byte** pp);
        [DllImport(Crypto)] internal static extern void X509_free(IntPtr x);
        [DllImport(Crypto)] internal static extern int X509_up_ref(IntPtr x);
        [DllImport(Crypto)] internal static extern IntPtr PEM_read_bio_X509(IntPtr bio, IntPtr* x, IntPtr cb, IntPtr u);
        [DllImport(Crypto)] internal static extern IntPtr PEM_read_bio_PrivateKey(IntPtr bio, IntPtr* x, IntPtr cb, byte* password);
        [DllImport(Crypto)] internal static extern IntPtr d2i_AutoPrivateKey(IntPtr* a, byte** pp, OsslLong length);
        [DllImport(Crypto)] internal static extern IntPtr d2i_PKCS12(IntPtr* a, byte** pp, OsslLong length);
        [DllImport(Crypto)] internal static extern int PKCS12_parse(IntPtr p12, byte* pass, IntPtr* pkey, IntPtr* cert, IntPtr* ca);
        [DllImport(Crypto)] internal static extern void PKCS12_free(IntPtr p12);
        [DllImport(Crypto)] internal static extern int EVP_PKEY_up_ref(IntPtr pkey);
        [DllImport(Crypto)] internal static extern void EVP_PKEY_free(IntPtr pkey);
        [DllImport(Crypto)] internal static extern int EVP_PKEY_get_base_id(IntPtr pkey);
        [DllImport(Crypto)] internal static extern int EVP_PKEY_get_bits(IntPtr pkey);
        [DllImport(Crypto)] internal static extern int X509_check_private_key(IntPtr x, IntPtr pkey);
        [DllImport(Crypto)] internal static extern int X509_check_host(IntPtr x, byte* name, nuint namelen, uint flags, byte** peername);
        [DllImport(Crypto)] internal static extern int X509_check_ip(IntPtr x, byte* address, nuint length, uint flags);

        // ---- stacks ----
        [DllImport(Crypto)] internal static extern IntPtr OPENSSL_sk_new_null();
        [DllImport(Crypto)] internal static extern int OPENSSL_sk_push(IntPtr st, IntPtr data);
        [DllImport(Crypto)] internal static extern int OPENSSL_sk_num(IntPtr st);
        [DllImport(Crypto)] internal static extern IntPtr OPENSSL_sk_value(IntPtr st, int i);
        [DllImport(Crypto)] internal static extern void OPENSSL_sk_free(IntPtr st);

        // ---- chain building ----
        [DllImport(Crypto)] internal static extern IntPtr X509_STORE_new();
        [DllImport(Crypto)] internal static extern void X509_STORE_free(IntPtr store);
        [DllImport(Crypto)] internal static extern int X509_STORE_set_default_paths(IntPtr store);
#if CSHARPC_TARGET_MACOS
        [DllImport(Crypto)] internal static extern int X509_STORE_load_file(IntPtr store, byte* file);
#endif
        [DllImport(Crypto)] internal static extern int X509_STORE_add_cert(IntPtr store, IntPtr x);
        [DllImport(Crypto)] internal static extern IntPtr X509_STORE_CTX_new();
        [DllImport(Crypto)] internal static extern void X509_STORE_CTX_free(IntPtr ctx);
        [DllImport(Crypto)] internal static extern int X509_STORE_CTX_init(IntPtr ctx, IntPtr store, IntPtr x509, IntPtr chain);
        [DllImport(Crypto)] internal static extern void X509_STORE_CTX_set_verify_cb(IntPtr ctx, delegate* unmanaged<int, IntPtr, int> cb);
        [DllImport(Crypto)] internal static extern int X509_verify_cert(IntPtr ctx);
        [DllImport(Crypto)] internal static extern IntPtr X509_STORE_CTX_get1_chain(IntPtr ctx);
        [DllImport(Crypto)] internal static extern int X509_STORE_CTX_get_error(IntPtr ctx);
        [DllImport(Crypto)] internal static extern int X509_STORE_CTX_get_error_depth(IntPtr ctx);
        [DllImport(Crypto)] internal static extern void X509_STORE_CTX_set_error(IntPtr ctx, int error);
        [DllImport(Crypto)] internal static extern IntPtr X509_STORE_CTX_get0_param(IntPtr ctx);
        [DllImport(Crypto)] internal static extern void X509_STORE_CTX_set_time(IntPtr ctx, OsslULong flags, long t);
        [DllImport(Crypto)] internal static extern int X509_VERIFY_PARAM_set_purpose(IntPtr param, int purpose);
        [DllImport(Crypto)] internal static extern int X509_VERIFY_PARAM_set_flags(IntPtr param, OsslULong flags);
        [DllImport(Crypto)] internal static extern IntPtr X509_verify_cert_error_string(OsslLong n);

        [DllImport(Crypto)] internal static extern IntPtr X509_STORE_CTX_get_ex_data(IntPtr ctx, int idx);
        [DllImport(Crypto)] internal static extern IntPtr X509_STORE_CTX_get0_cert(IntPtr ctx);
        [DllImport(Crypto)] internal static extern IntPtr X509_STORE_CTX_get0_untrusted(IntPtr ctx);

        // ---- libssl ----
        [DllImport(Ssl)] internal static extern int SSL_get_ex_data_X509_STORE_CTX_idx();
        [DllImport(Ssl)] internal static extern void SSL_CTX_set_cert_verify_callback(IntPtr ctx, delegate* unmanaged<IntPtr, IntPtr, int> cb, IntPtr arg);
        [DllImport(Ssl)] internal static extern int SSL_CTX_set_session_id_context(IntPtr ctx, byte* sidCtx, uint len);
        [DllImport(Ssl)] internal static extern int OPENSSL_init_ssl(ulong opts, IntPtr settings);
        [DllImport(Ssl)] internal static extern IntPtr TLS_client_method();
        [DllImport(Ssl)] internal static extern IntPtr TLS_server_method();
        [DllImport(Ssl)] internal static extern IntPtr SSL_CTX_new(IntPtr method);
        [DllImport(Ssl)] internal static extern void SSL_CTX_free(IntPtr ctx);
        [DllImport(Ssl)] internal static extern OsslLong SSL_CTX_ctrl(IntPtr ctx, int cmd, OsslLong larg, IntPtr parg);
        [DllImport(Ssl)] internal static extern ulong SSL_CTX_set_options(IntPtr ctx, ulong options);
        [DllImport(Ssl)] internal static extern int SSL_CTX_use_certificate(IntPtr ctx, IntPtr x);
        [DllImport(Ssl)] internal static extern int SSL_CTX_use_PrivateKey(IntPtr ctx, IntPtr pkey);
        [DllImport(Ssl)] internal static extern int SSL_CTX_check_private_key(IntPtr ctx);
        [DllImport(Ssl)] internal static extern void SSL_CTX_set_verify(IntPtr ctx, int mode, delegate* unmanaged<int, IntPtr, int> cb);
        [DllImport(Ssl)] internal static extern int SSL_CTX_set_alpn_protos(IntPtr ctx, byte* protos, uint protosLen);
        [DllImport(Ssl)] internal static extern void SSL_CTX_set_alpn_select_cb(IntPtr ctx, delegate* unmanaged<IntPtr, byte**, byte*, byte*, uint, IntPtr, int> cb, IntPtr arg);
        [DllImport(Ssl)] internal static extern int SSL_select_next_proto(byte** output, byte* outlen, byte* server, uint serverLen, byte* client, uint clientLen);
        [DllImport(Ssl)] internal static extern int SSL_CTX_set_num_tickets(IntPtr ctx, nuint tickets);
        [DllImport(Ssl)] internal static extern IntPtr SSL_new(IntPtr ctx);
        [DllImport(Ssl)] internal static extern void SSL_free(IntPtr ssl);
        [DllImport(Ssl)] internal static extern void SSL_set_bio(IntPtr ssl, IntPtr rbio, IntPtr wbio);
        [DllImport(Ssl)] internal static extern void SSL_set_connect_state(IntPtr ssl);
        [DllImport(Ssl)] internal static extern void SSL_set_accept_state(IntPtr ssl);
        [DllImport(Ssl)] internal static extern OsslLong SSL_ctrl(IntPtr ssl, int cmd, OsslLong larg, IntPtr parg);
        [DllImport(Ssl)] internal static extern void SSL_set_verify(IntPtr ssl, int mode, delegate* unmanaged<int, IntPtr, int> cb);
        [DllImport(Ssl)] internal static extern int SSL_do_handshake(IntPtr ssl);
        [DllImport(Ssl)] internal static extern int SSL_read(IntPtr ssl, byte* buf, int num);
        [DllImport(Ssl)] internal static extern int SSL_write(IntPtr ssl, byte* buf, int num);
        [DllImport(Ssl)] internal static extern int SSL_shutdown(IntPtr ssl);
        [DllImport(Ssl)] internal static extern int SSL_get_error(IntPtr ssl, int ret);
        [DllImport(Ssl)] internal static extern int SSL_pending(IntPtr ssl);
        [DllImport(Ssl)] internal static extern int SSL_version(IntPtr ssl);
        [DllImport(Ssl)] internal static extern IntPtr SSL_get_current_cipher(IntPtr ssl);
        [DllImport(Ssl)] internal static extern uint SSL_CIPHER_get_id(IntPtr cipher);
        [DllImport(Ssl)] internal static extern IntPtr SSL_get1_peer_certificate(IntPtr ssl);
        [DllImport(Ssl)] internal static extern IntPtr SSL_get_peer_cert_chain(IntPtr ssl);
        [DllImport(Ssl)] internal static extern void SSL_get0_alpn_selected(IntPtr ssl, byte** data, uint* len);
        [DllImport(Ssl)] internal static extern int SSL_get_shutdown(IntPtr ssl);
        [DllImport(Ssl)] internal static extern IntPtr SSL_get_servername(IntPtr ssl, int type);

#if CSHARPC_TARGET_WINDOWS
        // The Windows ROOT certificate stores (current user, then local machine) as DER, read once: OpenSSL has no system store
        // on Windows, so these are the trust anchors of X509ChainTrustMode.System. Roots Windows fetches on demand (the
        // AuthRoot program) are not in ROOT until used, which is the difference to CertGetCertificateChain.
        [DllImport("crypt32")] private static extern IntPtr CertOpenStore(IntPtr provider, uint encoding, IntPtr cryptProv, uint flags, char* parameter);
        [DllImport("crypt32")] private static extern byte* CertEnumCertificatesInStore(IntPtr store, byte* previous);
        [DllImport("crypt32")] private static extern int CertCloseStore(IntPtr store, uint flags);

        private static byte[][] s_systemRoots;

        private static byte[][] SystemRoots()
        {
            var roots = s_systemRoots;
            if (roots != null) return roots;
            var list = new System.Collections.Generic.List<byte[]>();
            // CERT_SYSTEM_STORE_CURRENT_USER, CERT_SYSTEM_STORE_LOCAL_MACHINE; | CERT_STORE_OPEN_EXISTING_FLAG | CERT_STORE_READONLY_FLAG.
            foreach (uint location in new uint[] { 0x10000, 0x20000 })
            {
                IntPtr handle;
                fixed (char* name = "ROOT") handle = CertOpenStore((IntPtr)10 /* CERT_STORE_PROV_SYSTEM_W */, 0, IntPtr.Zero, location | 0x4000 | 0x8000, name);
                if (handle == IntPtr.Zero) continue;
                // CERT_CONTEXT: dwCertEncodingType @0, pbCertEncoded @8, cbCertEncoded @16.
                for (byte* context = CertEnumCertificatesInStore(handle, null); context != null; context = CertEnumCertificatesInStore(handle, context))
                    list.Add(new ReadOnlySpan<byte>(*(byte**)(context + 8), (int)*(uint*)(context + 16)).ToArray());
                CertCloseStore(handle, 0);
            }
            return s_systemRoots = list.ToArray();
        }
#endif

        private static readonly object s_initLock = new object();
        private static bool s_initialized;

        /// <summary>Loads libssl and initializes it (once); a missing library is the DllNotFoundException of any P/Invoke.</summary>
        internal static void EnsureInitialized()
        {
            if (s_initialized) return;
            lock (s_initLock)
            {
                if (s_initialized) return;
                OPENSSL_init_ssl(0, IntPtr.Zero);
                s_initialized = true;
            }
        }

        /// <summary>The first queued OpenSSL error as ERR_error_string prints it ("error:10000080:BIO routines::no such file"), and clears the queue.</summary>
        internal static string TakeErrorString()
        {
            // .NET reports the last queued error (ERR_peek_last_error): BIO_new_file queues the system error, then its own.
            OsslULong e = 0, next;
            while ((next = ERR_get_error()) != 0) e = next;
            if (e == 0) return null;
            byte* buffer = stackalloc byte[256];
            ERR_error_string_n(e, buffer, 256);
            return Marshal.PtrToStringUTF8((IntPtr)buffer);
        }

        internal static CryptographicException Error()
        {
            string message = TakeErrorString();
            return message == null ? new CryptographicException() : new CryptographicException(message);
        }

        /// <summary>
        /// OpenSSL's default trust (SSL_CERT_FILE / SSL_CERT_DIR, the library's own directory); the Windows ROOT stores on Windows. On macOS the keychain's roots
        /// are not reachable from OpenSSL; the bundle macOS keeps for its command-line tools (/etc/ssl/cert.pem) stands in,
        /// next to whatever Homebrew's openssl@3 has installed (docs/adr/0022-tls.md, "macOS").
        /// </summary>
        internal static void SetDefaultTrust(IntPtr store)
        {
#if CSHARPC_TARGET_WINDOWS
            // No system store in OpenSSL on Windows: the ROOT certificate stores stand in (SystemRoots).
            foreach (byte[] der in SystemRoots())
            {
                IntPtr x = FromDer(der);
                if (x == IntPtr.Zero) continue;
                X509_STORE_add_cert(store, x);
                X509_free(x);
            }
#else
            X509_STORE_set_default_paths(store);
#endif
#if CSHARPC_TARGET_MACOS
            fixed (byte* bundle = Utf8Z("/etc/ssl/cert.pem"))
                X509_STORE_load_file(store, bundle);
#endif
            ERR_clear_error();
        }

        /// <summary>The DER encoding of an X509*.</summary>
        internal static byte[] ToDer(IntPtr x509)
        {
            int length = i2d_X509(x509, null);
            if (length <= 0) throw Error();
            byte[] der = new byte[length];
            fixed (byte* p = der)
            {
                byte* cursor = p;
                if (i2d_X509(x509, &cursor) != length) throw Error();
            }
            return der;
        }

        /// <summary>A new X509* (the caller frees it) from DER, or zero.</summary>
        internal static IntPtr FromDer(byte[] der)
        {
            fixed (byte* p = der)
            {
                byte* cursor = p;
                IntPtr x = d2i_X509(null, &cursor, der.Length);
                if (x == IntPtr.Zero) ERR_clear_error();
                return x;
            }
        }

        internal static byte[] Utf8Z(string s)
        {
            if (s == null) return null;
            return System.Text.Encoding.UTF8.GetBytes(s + "\0");
        }
    }
}
#endif
