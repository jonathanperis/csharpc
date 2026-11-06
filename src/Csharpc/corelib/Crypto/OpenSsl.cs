// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// OpenSSL 3 (libcrypto.so.3, libssl.so.3) as .NET's Linux cryptography stack reaches it, for X.509 certificates,
// chain building and TLS: the calls are those of libSystem.Security.Cryptography.Native.OpenSsl's shim, made
// directly (docs/adr/0022-tls.md; Rsa.cs and AesNative.cs bind libcrypto the same way). The libraries are
// opened on first use, so programs that don't use TLS or certificates never load them.
#nullable disable
#if CSHARPC_TARGET_LINUX
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CsharpcCoreLib
{
    internal static unsafe class Ossl
    {
        internal const string Crypto = "libcrypto.so.3";
        internal const string Ssl = "libssl.so.3";

        // ---- errors ----
        [DllImport(Crypto)] internal static extern ulong ERR_get_error();
        [DllImport(Crypto)] internal static extern ulong ERR_peek_error();
        [DllImport(Crypto)] internal static extern void ERR_error_string_n(ulong e, byte* buf, nuint len);
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
        [DllImport(Crypto)] internal static extern IntPtr d2i_X509(IntPtr* a, byte** pp, long length);
        [DllImport(Crypto)] internal static extern int i2d_X509(IntPtr x, byte** pp);
        [DllImport(Crypto)] internal static extern void X509_free(IntPtr x);
        [DllImport(Crypto)] internal static extern int X509_up_ref(IntPtr x);
        [DllImport(Crypto)] internal static extern IntPtr PEM_read_bio_X509(IntPtr bio, IntPtr* x, IntPtr cb, IntPtr u);
        [DllImport(Crypto)] internal static extern IntPtr PEM_read_bio_PrivateKey(IntPtr bio, IntPtr* x, IntPtr cb, byte* password);
        [DllImport(Crypto)] internal static extern IntPtr d2i_AutoPrivateKey(IntPtr* a, byte** pp, long length);
        [DllImport(Crypto)] internal static extern IntPtr d2i_PKCS12(IntPtr* a, byte** pp, long length);
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
        [DllImport(Crypto)] internal static extern void X509_STORE_CTX_set_time(IntPtr ctx, ulong flags, long t);
        [DllImport(Crypto)] internal static extern int X509_VERIFY_PARAM_set_purpose(IntPtr param, int purpose);
        [DllImport(Crypto)] internal static extern int X509_VERIFY_PARAM_set_flags(IntPtr param, ulong flags);
        [DllImport(Crypto)] internal static extern IntPtr X509_verify_cert_error_string(long n);

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
        [DllImport(Ssl)] internal static extern long SSL_CTX_ctrl(IntPtr ctx, int cmd, long larg, IntPtr parg);
        [DllImport(Ssl)] internal static extern ulong SSL_CTX_set_options(IntPtr ctx, ulong options);
        [DllImport(Ssl)] internal static extern int SSL_CTX_use_certificate(IntPtr ctx, IntPtr x);
        [DllImport(Ssl)] internal static extern int SSL_CTX_use_PrivateKey(IntPtr ctx, IntPtr pkey);
        [DllImport(Ssl)] internal static extern int SSL_CTX_check_private_key(IntPtr ctx);
        [DllImport(Ssl)] internal static extern void SSL_CTX_set_verify(IntPtr ctx, int mode, delegate* unmanaged<int, IntPtr, int> cb);
        [DllImport(Ssl)] internal static extern int SSL_CTX_set_alpn_protos(IntPtr ctx, byte* protos, uint protosLen);
        [DllImport(Ssl)] internal static extern void SSL_CTX_set_alpn_select_cb(IntPtr ctx, delegate* unmanaged<IntPtr, byte**, byte*, byte*, uint, IntPtr, int> cb, IntPtr arg);
        [DllImport(Ssl)] internal static extern int SSL_select_next_proto(byte** output, byte* outlen, byte* server, uint serverLen, byte* client, uint clientLen);
        [DllImport(Ssl)] internal static extern IntPtr SSL_new(IntPtr ctx);
        [DllImport(Ssl)] internal static extern void SSL_free(IntPtr ssl);
        [DllImport(Ssl)] internal static extern void SSL_set_bio(IntPtr ssl, IntPtr rbio, IntPtr wbio);
        [DllImport(Ssl)] internal static extern void SSL_set_connect_state(IntPtr ssl);
        [DllImport(Ssl)] internal static extern void SSL_set_accept_state(IntPtr ssl);
        [DllImport(Ssl)] internal static extern long SSL_ctrl(IntPtr ssl, int cmd, long larg, IntPtr parg);
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
            ulong e = 0, next;
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
