// The shim's TLS functions over libssl: pal_ssl.c (dotnet/runtime, .NET 11 GA), function by function, for the framework's System.Net.Security
// IL (SslStream over Interop.OpenSsl, SslStreamPal.Unix). The BIOs these drive are in CryptoNative.Bio.cs; see CryptoNative.cs for the contract.
//
// Security (docs/il-first/System.Net.Security.md, "Security review"): the SSL_CTX defaults are the shim's (TLS_method, no compression, server
// cipher preference, client renegotiation allowed and policed by the framework, the shim's default cipher string unless openssl.cnf has one,
// the protocol options and security levels computed as the shim computes them); certificate validation stays the framework's: the verify
// callback is the one the IL passes (SSL_CTX_set_cert_verify_callback), and nothing here accepts a certificate, a host name or a chain. Every
// buffer is bounded by the length the caller passed; the OpenSSL error queue is cleared exactly where the shim clears it; nothing parses
// untrusted data in C# (a stapled OCSP response goes to d2i_OCSP_RESPONSE, as in the shim).
#if CSHARPC_TARGET_LINUX
#nullable disable
using System;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    internal static unsafe partial class CryptoNative
    {
        // ---- libssl ----------------------------------------------------------------------------------------------------------
        [DllImport(LibSsl)] private static extern IntPtr TLS_method();
        [DllImport(LibSsl)] private static extern IntPtr SSL_CTX_new(IntPtr method);
        [DllImport(LibSsl)] private static extern void SSL_CTX_free(IntPtr ctx);
        [DllImport(LibSsl)] private static extern ulong SSL_CTX_set_options(IntPtr ctx, ulong options);
        [DllImport(LibSsl)] private static extern ulong SSL_set_options(IntPtr s, ulong options);
        [DllImport(LibSsl)] private static extern long SSL_CTX_ctrl(IntPtr ctx, int cmd, long larg, void* parg);
        [DllImport(LibSsl)] private static extern long SSL_CTX_callback_ctrl(IntPtr ctx, int cmd, void* fp);
        [DllImport(LibSsl)] private static extern long SSL_ctrl(IntPtr s, int cmd, long larg, void* parg);
        [DllImport(LibSsl)] private static extern int SSL_CTX_set_cipher_list(IntPtr ctx, byte* str);
        [DllImport(LibSsl)] private static extern int SSL_CTX_set_ciphersuites(IntPtr ctx, byte* str);
        [DllImport(LibSsl)] private static extern int SSL_CTX_config(IntPtr ctx, byte* name);
        [DllImport(LibSsl)] private static extern IntPtr SSL_new(IntPtr ctx);
        [DllImport(LibSsl)] private static extern void SSL_free(IntPtr ssl);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_ciphers(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_get_error(IntPtr ssl, int ret);
        [DllImport(LibSsl)] private static extern void SSL_set_connect_state(IntPtr ssl);
        [DllImport(LibSsl)] private static extern void SSL_set_accept_state(IntPtr ssl);
        [DllImport(LibSsl)] private static extern byte* SSL_get_version(IntPtr ssl);
        [DllImport(LibSsl)] private static extern nuint SSL_get_finished(IntPtr ssl, void* buf, nuint count);
        [DllImport(LibSsl)] private static extern nuint SSL_get_peer_finished(IntPtr ssl, void* buf, nuint count);
        [DllImport(LibSsl)] private static extern int SSL_session_reused(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_write(IntPtr ssl, void* buf, int num);
        [DllImport(LibSsl)] private static extern int SSL_read(IntPtr ssl, void* buf, int num);
        [DllImport(LibSsl)] private static extern int SSL_peek(IntPtr ssl, void* buf, int num);
        [DllImport(LibSsl)] private static extern int SSL_pending(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_version(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_verify_client_post_handshake(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_renegotiate_pending(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_renegotiate(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_shutdown(IntPtr ssl);
        [DllImport(LibSsl)] private static extern void SSL_set_bio(IntPtr ssl, IntPtr rbio, IntPtr wbio);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_rbio(IntPtr ssl);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_wbio(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_set_fd(IntPtr ssl, int fd);
        [DllImport(LibSsl)] private static extern int SSL_do_handshake(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_is_init_finished(IntPtr ssl);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get1_peer_certificate(IntPtr ssl);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_certificate(IntPtr ssl);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_peer_cert_chain(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_use_certificate(IntPtr ssl, IntPtr x);
        [DllImport(LibSsl)] private static extern int SSL_use_PrivateKey(IntPtr ssl, IntPtr pkey);
        [DllImport(LibSsl)] private static extern int SSL_CTX_use_certificate(IntPtr ctx, IntPtr x);
        [DllImport(LibSsl)] private static extern int SSL_CTX_use_PrivateKey(IntPtr ctx, IntPtr pkey);
        [DllImport(LibSsl)] private static extern int SSL_CTX_check_private_key(IntPtr ctx);
        [DllImport(LibSsl)] private static extern void SSL_CTX_set_quiet_shutdown(IntPtr ctx, int mode);
        [DllImport(LibSsl)] private static extern void SSL_set_quiet_shutdown(IntPtr ssl, int mode);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_client_CA_list(IntPtr ssl);
        [DllImport(LibSsl)] private static extern void SSL_set_verify(IntPtr ssl, int mode, void* callback);
        [DllImport(LibSsl)] private static extern void SSL_CTX_set_verify(IntPtr ctx, int mode, void* callback);
        [DllImport(LibSsl)] private static extern int SSL_CTX_set_session_id_context(IntPtr ctx, byte* sidCtx, uint sidCtxLen);
        [DllImport(LibSsl)] private static extern void SSL_CTX_sess_set_new_cb(IntPtr ctx, void* cb);
        [DllImport(LibSsl)] private static extern void SSL_CTX_sess_set_remove_cb(IntPtr ctx, void* cb);
        [DllImport(LibSsl)] private static extern int SSL_CTX_remove_session(IntPtr ctx, IntPtr session);
        [DllImport(LibSsl)] private static extern byte* SSL_get_servername(IntPtr ssl, int type);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_session(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_set_session(IntPtr ssl, IntPtr session);
        [DllImport(LibSsl)] private static extern void SSL_SESSION_free(IntPtr session);
        [DllImport(LibSsl)] private static extern byte* SSL_SESSION_get0_hostname(IntPtr session);
        [DllImport(LibSsl)] private static extern int SSL_SESSION_set1_hostname(IntPtr session, byte* hostname);
        [DllImport(LibSsl)] private static extern int SSL_SESSION_set_ex_data(IntPtr session, int idx, IntPtr arg);
        [DllImport(LibSsl)] private static extern IntPtr SSL_SESSION_get_ex_data(IntPtr session, int idx);
        [DllImport(LibSsl)] private static extern void SSL_CTX_set_security_level(IntPtr ctx, int level);
        [DllImport(LibSsl)] private static extern IntPtr SSL_CIPHER_find(IntPtr ssl, byte* ptr);
        [DllImport(LibSsl)] private static extern byte* SSL_CIPHER_get_name(IntPtr cipher);
        [DllImport(LibSsl)] private static extern byte* SSL_CIPHER_get_version(IntPtr cipher);
        [DllImport(LibSsl)] private static extern uint SSL_CIPHER_get_id(IntPtr cipher);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_current_cipher(IntPtr ssl);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_pending_cipher(IntPtr ssl);
        [DllImport(LibSsl)] private static extern int SSL_add_client_CA(IntPtr ssl, IntPtr x);
        [DllImport(LibSsl)] private static extern void SSL_CTX_set_alpn_select_cb(IntPtr ctx, void* cb, void* arg);
        [DllImport(LibSsl)] private static extern void SSL_set_cert_cb(IntPtr ssl, void* cb, void* arg);
        [DllImport(LibSsl)] private static extern void SSL_CTX_set_keylog_callback(IntPtr ctx, void* cb);
        [DllImport(LibSsl)] private static extern void SSL_set_post_handshake_auth(IntPtr ssl, int val);
        [DllImport(LibSsl)] private static extern int SSL_set_ex_data(IntPtr ssl, int idx, IntPtr data);
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_ex_data(IntPtr ssl, int idx);
        [DllImport(LibSsl)] private static extern int SSL_CTX_set_ex_data(IntPtr ctx, int idx, IntPtr data);
        [DllImport(LibSsl)] private static extern IntPtr SSL_CTX_get_ex_data(IntPtr ctx, int idx);
        [DllImport(LibSsl)] private static extern int SSL_set_alpn_protos(IntPtr ssl, byte* protos, uint protosLen);
        [DllImport(LibSsl)] private static extern void SSL_get0_alpn_selected(IntPtr ssl, byte** data, uint* len);
        [DllImport(LibSsl)] private static extern int SSL_get_sigalgs(IntPtr ssl, int idx, int* psign, int* phash, int* psignhash, byte* rsig, byte* rhash);
        [DllImport(LibSsl)] private static extern void SSL_CTX_set_cert_verify_callback(IntPtr ctx, void* cb, void* arg);
        [DllImport(LibSsl)] private static extern int SSL_get_ex_data_X509_STORE_CTX_idx();
        [DllImport(LibSsl)] private static extern IntPtr SSL_get_SSL_CTX(IntPtr ssl);
        [DllImport(LibCrypto)] private static extern void X509_STORE_CTX_set_error(IntPtr ctx, int s);

        private const int SSL_ERROR_NONE = 0;
        private const ulong SSL_OP_NO_SSLv2 = 0, SSL_OP_ALLOW_CLIENT_RENEGOTIATION = 1UL << 8, SSL_OP_NO_TICKET = 1UL << 14,
            SSL_OP_NO_SESSION_RESUMPTION_ON_RENEGOTIATION = 1UL << 16, SSL_OP_NO_COMPRESSION = 1UL << 17, SSL_OP_CIPHER_SERVER_PREFERENCE = 1UL << 22,
            SSL_OP_NO_SSLv3 = 1UL << 25, SSL_OP_NO_TLSv1 = 1UL << 26, SSL_OP_NO_TLSv1_2 = 1UL << 27, SSL_OP_NO_TLSv1_1 = 1UL << 28,
            SSL_OP_NO_TLSv1_3 = 1UL << 29;
        private const int PAL_SSL_SSL2 = 12, PAL_SSL_SSL3 = 48, PAL_SSL_TLS = 192, PAL_SSL_TLS11 = 768, PAL_SSL_TLS12 = 3072, PAL_SSL_TLS13 = 12288;
        private const int SSL_CTRL_EXTRA_CHAIN_CERT = 14, SSL_CTRL_MODE = 33, SSL_CTRL_SET_SESS_CACHE_SIZE = 42, SSL_CTRL_SET_SESS_CACHE_MODE = 44,
            SSL_CTRL_SET_TLSEXT_HOSTNAME = 55, SSL_CTRL_SET_TLSEXT_STATUS_REQ_CB = 63, SSL_CTRL_SET_TLSEXT_STATUS_REQ_TYPE = 65,
            SSL_CTRL_GET_TLSEXT_STATUS_REQ_OCSP_RESP = 70, SSL_CTRL_SET_TLSEXT_STATUS_REQ_OCSP_RESP = 71, SSL_CTRL_CHAIN_CERT = 89,
            SSL_CTRL_SET_SIGALGS_LIST = 98, SSL_CTRL_SET_CLIENT_SIGALGS_LIST = 102, SSL_CTRL_SET_MIN_PROTO_VERSION = 123,
            SSL_CTRL_SET_MAX_PROTO_VERSION = 124;
        private const int TLSEXT_STATUSTYPE_ocsp = 1, TLSEXT_NAMETYPE_host_name = 0, SSL_TLSEXT_ERR_OK = 0, SSL_TLSEXT_ERR_NOACK = 3;
        private const int SSL_MODE_ACCEPT_MOVING_WRITE_BUFFER = 2, SSL_VERIFY_NONE = 0, SSL_VERIFY_PEER = 1, SSL_VERIFY_FAIL_IF_NO_PEER_CERT = 2;
        private const int SSL_SESS_CACHE_OFF = 0, SSL_SESS_CACHE_BOTH = 3, SSL_MAX_SID_CTX_LENGTH = 32, TLS1_3_VERSION = 0x0304;

        // DOTNET_DEFAULT_CIPHERSTRING, as the shim spells it (with its trailing separator).
        private static ReadOnlySpan<byte> DefaultCipherString => "ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-SHA384:ECDHE-ECDSA-AES128-SHA256:ECDHE-RSA-AES256-SHA384:ECDHE-RSA-AES128-SHA256:\0"u8;

        private static int g_config_specified_ciphersuites;
        // g_emptyAlpn: the ALPN select callback's argument is always this empty string, whatever the framework passes.
        private static byte* s_emptyAlpn;

        // ---- initialization ----------------------------------------------------------------------------------------------------
        // Whether openssl.cnf names a default CipherString (then the shim's own default list is not applied).
        private static void DetectCiphersuiteConfiguration()
        {
            IntPtr ctx = SSL_CTX_new(TLS_method());
            if (ctx == IntPtr.Zero)
            {
                // A misconfigured OpenSSL sometimes fails the first time only.
                ctx = SSL_CTX_new(TLS_method());
                if (ctx == IntPtr.Zero)
                {
                    ERR_clear_error();
                    return;
                }
            }
            IntPtr ssl = SSL_new(ctx);
            if (ssl == IntPtr.Zero)
            {
                ERR_clear_error();
                SSL_CTX_free(ctx);
                return;
            }
            int defaultCount = OPENSSL_sk_num(SSL_get_ciphers(ssl));
            SSL_free(ssl);
            int rv;
            fixed (byte* all = "ALL\0"u8) rv = SSL_CTX_set_cipher_list(ctx, all);
            if (rv == 0)
            {
                ERR_clear_error();
                SSL_CTX_free(ctx);
                return;
            }
            ssl = SSL_new(ctx);
            if (ssl == IntPtr.Zero)
            {
                ERR_clear_error();
                SSL_CTX_free(ctx);
                return;
            }
            int allCount = OPENSSL_sk_num(SSL_get_ciphers(ssl));
            SSL_free(ssl);
            if (allCount == defaultCount)
            {
                fixed (byte* rsa = "RSA\0"u8) rv = SSL_CTX_set_cipher_list(ctx, rsa);
                if (rv == 0)
                {
                    ERR_clear_error();
                    SSL_CTX_free(ctx);
                    return;
                }
                ssl = SSL_new(ctx);
                if (ssl == IntPtr.Zero)
                {
                    ERR_clear_error();
                    SSL_CTX_free(ctx);
                    return;
                }
                allCount = OPENSSL_sk_num(SSL_get_ciphers(ssl));
                SSL_free(ssl);
                if (allCount == defaultCount)
                {
                    SSL_CTX_free(ctx);
                    return;
                }
            }
            int configured;
            fixed (byte* name = "system_default\0"u8) configured = SSL_CTX_config(ctx, name);
            if (configured == 0)
                ERR_clear_error();
            else
            {
                ssl = SSL_new(ctx);
                if (ssl != IntPtr.Zero)
                {
                    int after = OPENSSL_sk_num(SSL_get_ciphers(ssl));
                    SSL_free(ssl);
                    g_config_specified_ciphersuites = allCount != after ? 1 : 0;
                }
            }
            SSL_CTX_free(ctx);
        }

        [RuntimeExport("CryptoNative_EnsureLibSslInitialized", Trigger + "CryptoNative_EnsureLibSslInitialized")]
        public static void EnsureLibSslInitialized()
        {
            EnsureOpenSslInitialized();
            DetectCiphersuiteConfiguration();
        }

        [RuntimeExport("CryptoNative_SslV2_3Method", Trigger + "CryptoNative_SslV2_3Method")]
        public static IntPtr SslV2_3Method() => TLS_method();

        [RuntimeExport("CryptoNative_SslCtxCreate", Trigger + "CryptoNative_SslCtxCreate")]
        public static IntPtr SslCtxCreate(IntPtr method)
        {
            ERR_clear_error();
            if (method == IntPtr.Zero) return IntPtr.Zero;
            IntPtr ctx = SSL_CTX_new(method);
            if (ctx != IntPtr.Zero)
            {
                // Server preference, as the other .NET platforms; compression off.
                SSL_CTX_set_options(ctx, SSL_OP_NO_COMPRESSION | SSL_OP_CIPHER_SERVER_PREFERENCE);
                // OpenSSL 3 forbids client-initiated renegotiation by default; the framework enforces AllowRenegotiation itself.
                if (OpenSSL_version_num() >= OPENSSL_VERSION_3_0_RTM)
                    SSL_CTX_set_options(ctx, SSL_OP_ALLOW_CLIENT_RENEGOTIATION);
                if (g_config_specified_ciphersuites == 0)
                {
                    int ok;
                    fixed (byte* list = DefaultCipherString) ok = SSL_CTX_set_cipher_list(ctx, list);
                    if (ok == 0)
                    {
                        SSL_CTX_free(ctx);
                        return IntPtr.Zero;
                    }
                }
            }
            return ctx;
        }

        private static void ResetCtxProtocolRestrictions(IntPtr ctx)
        {
            SSL_CTX_ctrl(ctx, SSL_CTRL_SET_MIN_PROTO_VERSION, 0, null);
            SSL_CTX_ctrl(ctx, SSL_CTRL_SET_MAX_PROTO_VERSION, 0, null);
        }

        [RuntimeExport("CryptoNative_SslCtxSetProtocolOptions", Trigger + "CryptoNative_SslCtxSetProtocolOptions")]
        public static void SslCtxSetProtocolOptions(IntPtr ctx, int protocols)
        {
            // 0: the system default, OpenSSL's choice.
            if (protocols == 0) return;
            ulong protocolOptions = 0;
            if ((protocols & PAL_SSL_SSL2) != PAL_SSL_SSL2) protocolOptions |= SSL_OP_NO_SSLv2;
            if ((protocols & PAL_SSL_SSL3) != PAL_SSL_SSL3) protocolOptions |= SSL_OP_NO_SSLv3;
            if ((protocols & PAL_SSL_TLS) != PAL_SSL_TLS) protocolOptions |= SSL_OP_NO_TLSv1;
            if ((protocols & PAL_SSL_TLS11) != PAL_SSL_TLS11) protocolOptions |= SSL_OP_NO_TLSv1_1;
            if ((protocols & PAL_SSL_TLS12) != PAL_SSL_TLS12) protocolOptions |= SSL_OP_NO_TLSv1_2;
            if ((protocols & PAL_SSL_TLS13) != PAL_SSL_TLS13) protocolOptions |= SSL_OP_NO_TLSv1_3;
            // Protocols set by hand: OpenSSL's own min/max restrictions are reset to the widest range.
            ResetCtxProtocolRestrictions(ctx);
            SSL_CTX_set_options(ctx, protocolOptions);
        }

        [RuntimeExport("CryptoNative_SslCreate", Trigger + "CryptoNative_SslCreate")]
        public static IntPtr SslCreate(IntPtr ctx)
        {
            ERR_clear_error();
            IntPtr ret = SSL_new(ctx);
            // Opportunistically ask the server for a stapled OCSP response.
            if (ret != IntPtr.Zero && SSL_ctrl(ret, SSL_CTRL_SET_TLSEXT_STATUS_REQ_TYPE, TLSEXT_STATUSTYPE_ocsp, null) != 1)
                ERR_clear_error();
            return ret;
        }

        // The queue is left for the framework's exception.
        [RuntimeExport("CryptoNative_SslGetError", Trigger + "CryptoNative_SslGetError")]
        public static int SslGetError(IntPtr ssl, int ret) => SSL_get_error(ssl, ret);

        [RuntimeExport("CryptoNative_SslDestroy", Trigger + "CryptoNative_SslDestroy")]
        public static void SslDestroy(IntPtr ssl)
        {
            if (ssl != IntPtr.Zero) SSL_free(ssl);
        }

        [RuntimeExport("CryptoNative_SslCtxDestroy", Trigger + "CryptoNative_SslCtxDestroy")]
        public static void SslCtxDestroy(IntPtr ctx)
        {
            if (ctx != IntPtr.Zero) SSL_CTX_free(ctx);
        }

        [RuntimeExport("CryptoNative_SslSetConnectState", Trigger + "CryptoNative_SslSetConnectState")]
        public static void SslSetConnectState(IntPtr ssl) => SSL_set_connect_state(ssl);

        [RuntimeExport("CryptoNative_SslSetAcceptState", Trigger + "CryptoNative_SslSetAcceptState")]
        public static void SslSetAcceptState(IntPtr ssl) => SSL_set_accept_state(ssl);

        [RuntimeExport("CryptoNative_SslGetVersion", Trigger + "CryptoNative_SslGetVersion")]
        public static byte* SslGetVersion(IntPtr ssl) => SSL_get_version(ssl);

        [RuntimeExport("CryptoNative_SslGetFinished", Trigger + "CryptoNative_SslGetFinished")]
        public static int SslGetFinished(IntPtr ssl, void* buf, int count) => count < 0 ? 0 : (int)SSL_get_finished(ssl, buf, (nuint)count);

        [RuntimeExport("CryptoNative_SslGetPeerFinished", Trigger + "CryptoNative_SslGetPeerFinished")]
        public static int SslGetPeerFinished(IntPtr ssl, void* buf, int count) => count < 0 ? 0 : (int)SSL_get_peer_finished(ssl, buf, (nuint)count);

        [RuntimeExport("CryptoNative_SslSessionReused", Trigger + "CryptoNative_SslSessionReused")]
        public static int SslSessionReused(IntPtr ssl) => SSL_session_reused(ssl) == 1 ? 1 : 0;

        // errno is captured before SSL_get_error and restored after it: on SSL_ERROR_SYSCALL it is the caller's diagnostic (SetLastError).
        [RuntimeExport("CryptoNative_SslWrite", Trigger + "CryptoNative_SslWrite")]
        public static int SslWrite(IntPtr ssl, void* buf, int num, int* error)
        {
            ERR_clear_error();
            int result = SSL_write(ssl, buf, num);
            int savedErrno = Errno;
            *error = result > 0 ? SSL_ERROR_NONE : SslGetError(ssl, result);
            Marshal.SetLastSystemError(savedErrno);
            return result;
        }

        [RuntimeExport("CryptoNative_SslRead", Trigger + "CryptoNative_SslRead")]
        public static int SslRead(IntPtr ssl, void* buf, int num, int* error)
        {
            ERR_clear_error();
            int result = SSL_read(ssl, buf, num);
            int savedErrno = Errno;
            *error = result > 0 ? SSL_ERROR_NONE : SslGetError(ssl, result);
            Marshal.SetLastSystemError(savedErrno);
            return result;
        }

        [RuntimeExport("CryptoNative_SslRenegotiate", Trigger + "CryptoNative_SslRenegotiate")]
        public static int SslRenegotiate(IntPtr ssl, int* error)
        {
            ERR_clear_error();
            // TLS 1.3: post-handshake authentication, which needs SSL_VERIFY_PEER.
            if (SSL_version(ssl) == TLS1_3_VERSION)
            {
                SslSetVerifyPeer(ssl, 0);
                int ret13 = SSL_verify_client_post_handshake(ssl);
                if (ret13 != 1) *error = SslGetError(ssl, ret13);
                return ret13;
            }
            // The context is gone afterwards: no ticket or session resumption.
            SSL_set_options(ssl, SSL_OP_NO_TICKET | SSL_OP_NO_SESSION_RESUMPTION_ON_RENEGOTIATION);
            if (SSL_renegotiate_pending(ssl) == 0)
            {
                SslSetVerifyPeer(ssl, 0);
                int ret = SSL_renegotiate(ssl);
                if (ret != 1) *error = SslGetError(ssl, ret);
                return ret;
            }
            *error = SSL_ERROR_NONE;
            return 0;
        }

        [RuntimeExport("CryptoNative_IsSslRenegotiatePending", Trigger + "CryptoNative_IsSslRenegotiatePending")]
        public static int IsSslRenegotiatePending(IntPtr ssl)
        {
            ERR_clear_error();
            return SSL_renegotiate_pending(ssl) != 0 ? 1 : 0;
        }

        [RuntimeExport("CryptoNative_SslShutdown", Trigger + "CryptoNative_SslShutdown")]
        public static int SslShutdown(IntPtr ssl)
        {
            ERR_clear_error();
            return SSL_shutdown(ssl);
        }

        [RuntimeExport("CryptoNative_SslSetBio", Trigger + "CryptoNative_SslSetBio")]
        public static void SslSetBio(IntPtr ssl, IntPtr rbio, IntPtr wbio) => SSL_set_bio(ssl, rbio, wbio);

        [RuntimeExport("CryptoNative_SslSetFd", Trigger + "CryptoNative_SslSetFd")]
        public static int SslSetFd(IntPtr ssl, nint fd)
        {
            ERR_clear_error();
            return SSL_set_fd(ssl, (int)fd);
        }

        [RuntimeExport("CryptoNative_SslSetAcceptMovingWriteBuffer", Trigger + "CryptoNative_SslSetAcceptMovingWriteBuffer")]
        public static void SslSetAcceptMovingWriteBuffer(IntPtr ssl) => SSL_ctrl(ssl, SSL_CTRL_MODE, SSL_MODE_ACCEPT_MOVING_WRITE_BUFFER, null);

        [RuntimeExport("CryptoNative_SslDoHandshake", Trigger + "CryptoNative_SslDoHandshake")]
        public static int SslDoHandshake(IntPtr ssl, int* errorCode)
        {
            ERR_clear_error();
            // The peek starts renegotiation and post-handshake client-certificate processing; errno is the handshake's, not the peek's.
            int peekErrno = Errno;
            SSL_peek(ssl, null, 0);
            Marshal.SetLastSystemError(peekErrno);
            int ret = SSL_do_handshake(ssl);
            int savedErrno = Errno;
            *errorCode = ret <= 0 ? SSL_get_error(ssl, ret) : 0;
            Marshal.SetLastSystemError(savedErrno);
            return ret;
        }

        [RuntimeExport("CryptoNative_SslHandshake", Trigger + "CryptoNative_SslHandshake")]
        public static int SslHandshake(IntPtr ssl, byte* inputPtr, int inputLen, int* consumed, byte* outputPtr, int outputCap, int* outputWritten,
            int* outputPending, int* errorCode)
        {
            if (outputWritten != null) *outputWritten = 0;
            if (outputPending != null) *outputPending = 0;
            if (consumed != null) *consumed = 0;
            ERR_clear_error();
            IntPtr inputBio = SSL_get_rbio(ssl);
            IntPtr outputBio = SSL_get_wbio(ssl);
            if (inputBio != IntPtr.Zero) BioSetReadWindow(inputBio, inputPtr, inputLen);
            if (outputBio != IntPtr.Zero) BioSetWriteWindow(outputBio, outputPtr, outputCap);
            int peekErrno = Errno;
            SSL_peek(ssl, null, 0);
            Marshal.SetLastSystemError(peekErrno);
            int result = SSL_do_handshake(ssl);
            *errorCode = result == 1 ? SSL_ERROR_NONE : SslGetError(ssl, result);
            if (outputBio != IntPtr.Zero)
            {
                BioGetWriteResult(outputBio, outputWritten, outputPending);
                BioSetWriteWindow(outputBio, null, 0);
            }
            if (inputBio != IntPtr.Zero)
            {
                int leftover = 0;
                BioClearReadWindow(inputBio, &leftover);
                if (consumed != null) *consumed = inputLen - leftover;
            }
            return result;
        }

        [RuntimeExport("CryptoNative_SslEncrypt", Trigger + "CryptoNative_SslEncrypt")]
        public static int SslEncrypt(IntPtr ssl, byte* plaintextPtr, int plaintextLen, byte* outputPtr, int outputCap, int* outputWritten,
            int* outputPending, int* errorCode)
        {
            if (outputWritten != null) *outputWritten = 0;
            if (outputPending != null) *outputPending = 0;
            ERR_clear_error();
            IntPtr outputBio = SSL_get_wbio(ssl);
            if (outputBio != IntPtr.Zero) BioSetWriteWindow(outputBio, outputPtr, outputCap);
            int result = SSL_write(ssl, plaintextPtr, plaintextLen);
            *errorCode = result > 0 ? SSL_ERROR_NONE : SslGetError(ssl, result);
            if (outputBio != IntPtr.Zero)
            {
                BioGetWriteResult(outputBio, outputWritten, outputPending);
                BioSetWriteWindow(outputBio, null, 0);
            }
            return result;
        }

        [RuntimeExport("CryptoNative_SslDecrypt", Trigger + "CryptoNative_SslDecrypt")]
        public static int SslDecrypt(IntPtr ssl, byte* inputPtr, int inputLen, int* consumed, byte* outputPtr, int outputCap, int* leftoverOffset,
            int* leftoverLength, int* errorCode)
        {
            if (leftoverOffset != null) *leftoverOffset = 0;
            if (leftoverLength != null) *leftoverLength = 0;
            if (consumed != null) *consumed = 0;
            ERR_clear_error();
            IntPtr inputBio = SSL_get_rbio(ssl);
            if (inputBio != IntPtr.Zero) BioSetReadWindow(inputBio, inputPtr, inputLen);
            int result = 0, leftover = 0;
            if (outputCap > 0) result = SSL_read(ssl, outputPtr, outputCap);
            // With the destination empty or full, plaintext OpenSSL would buffer is read into the input buffer for the next call (not after
            // a failed first read, whose state is reported as is).
            if (result > 0 ? SSL_pending(ssl) > 0 : outputCap == 0) leftover = SSL_read(ssl, inputPtr, inputLen);
            int outcome = result > 0 ? result : leftover;
            *errorCode = outcome > 0 ? SSL_ERROR_NONE : SslGetError(ssl, outcome);
            if (leftover > 0) *leftoverLength = leftover;
            if (inputBio != IntPtr.Zero)
            {
                int unread = 0;
                BioClearReadWindow(inputBio, &unread);
                if (consumed != null) *consumed = inputLen - unread;
            }
            return result;
        }

        [RuntimeExport("CryptoNative_IsSslStateOK", Trigger + "CryptoNative_IsSslStateOK")]
        public static int IsSslStateOK(IntPtr ssl) => SSL_is_init_finished(ssl);

        [RuntimeExport("CryptoNative_SslGetPeerCertificate", Trigger + "CryptoNative_SslGetPeerCertificate")]
        public static IntPtr SslGetPeerCertificate(IntPtr ssl)
        {
            IntPtr cert = SSL_get1_peer_certificate(ssl);
            SslUpdateOcspStaple(ssl, cert);
            return cert;
        }

        [RuntimeExport("CryptoNative_SslUpdateOcspStaple", Trigger + "CryptoNative_SslUpdateOcspStaple")]
        public static void SslUpdateOcspStaple(IntPtr ssl, IntPtr cert)
        {
            if (ssl == IntPtr.Zero || cert == IntPtr.Zero) return;
            byte* data = null;
            long len = SSL_ctrl(ssl, SSL_CTRL_GET_TLSEXT_STATUS_REQ_OCSP_RESP, 0, &data);
            if (len > 0 && X509_get_ex_data(cert, g_x509_ocsp_index) == IntPtr.Zero)
            {
                // DER decoding is OpenSSL's, bounded by the length OpenSSL reports.
                IntPtr ocspResp = d2i_OCSP_RESPONSE(IntPtr.Zero, &data, len);
                if (ocspResp == IntPtr.Zero) ERR_clear_error();
                else X509_set_ex_data(cert, g_x509_ocsp_index, ocspResp);
            }
        }

        [RuntimeExport("CryptoNative_SslGetCertificate", Trigger + "CryptoNative_SslGetCertificate")]
        public static IntPtr SslGetCertificate(IntPtr ssl) => SSL_get_certificate(ssl);

        [RuntimeExport("CryptoNative_SslGetPeerCertChain", Trigger + "CryptoNative_SslGetPeerCertChain")]
        public static IntPtr SslGetPeerCertChain(IntPtr ssl) => SSL_get_peer_cert_chain(ssl);

        [RuntimeExport("CryptoNative_SslUseCertificate", Trigger + "CryptoNative_SslUseCertificate")]
        public static int SslUseCertificate(IntPtr ssl, IntPtr x)
        {
            ERR_clear_error();
            return SSL_use_certificate(ssl, x);
        }

        [RuntimeExport("CryptoNative_SslUsePrivateKey", Trigger + "CryptoNative_SslUsePrivateKey")]
        public static int SslUsePrivateKey(IntPtr ssl, IntPtr pkey)
        {
            ERR_clear_error();
            return SSL_use_PrivateKey(ssl, pkey);
        }

        [RuntimeExport("CryptoNative_SslCtxUseCertificate", Trigger + "CryptoNative_SslCtxUseCertificate")]
        public static int SslCtxUseCertificate(IntPtr ctx, IntPtr x)
        {
            ERR_clear_error();
            return SSL_CTX_use_certificate(ctx, x);
        }

        [RuntimeExport("CryptoNative_SslCtxUsePrivateKey", Trigger + "CryptoNative_SslCtxUsePrivateKey")]
        public static int SslCtxUsePrivateKey(IntPtr ctx, IntPtr pkey)
        {
            ERR_clear_error();
            return SSL_CTX_use_PrivateKey(ctx, pkey);
        }

        [RuntimeExport("CryptoNative_SslCtxCheckPrivateKey", Trigger + "CryptoNative_SslCtxCheckPrivateKey")]
        public static int SslCtxCheckPrivateKey(IntPtr ctx)
        {
            ERR_clear_error();
            return SSL_CTX_check_private_key(ctx);
        }

        [RuntimeExport("CryptoNative_SslCtxSetQuietShutdown", Trigger + "CryptoNative_SslCtxSetQuietShutdown")]
        public static void SslCtxSetQuietShutdown(IntPtr ctx) => SSL_CTX_set_quiet_shutdown(ctx, 1);

        [RuntimeExport("CryptoNative_SslSetQuietShutdown", Trigger + "CryptoNative_SslSetQuietShutdown")]
        public static void SslSetQuietShutdown(IntPtr ssl, int mode) => SSL_set_quiet_shutdown(ssl, mode);

        [RuntimeExport("CryptoNative_SslGetClientCAList", Trigger + "CryptoNative_SslGetClientCAList")]
        public static IntPtr SslGetClientCAList(IntPtr ssl) => SSL_get_client_CA_list(ssl);

        // No verify callback here: the certificate is judged by the framework's callback (SslCtxSetCertVerifyCallback).
        [RuntimeExport("CryptoNative_SslSetVerifyPeer", Trigger + "CryptoNative_SslSetVerifyPeer")]
        public static void SslSetVerifyPeer(IntPtr ssl, int failIfNoPeerCert)
        {
            int mode = SSL_VERIFY_PEER;
            if (failIfNoPeerCert != 0) mode |= SSL_VERIFY_FAIL_IF_NO_PEER_CERT;
            SSL_set_verify(ssl, mode, null);
        }

        [RuntimeExport("CryptoNative_SslCtxSetCaching", Trigger + "CryptoNative_SslCtxSetCaching")]
        public static int SslCtxSetCaching(IntPtr ctx, int mode, int cacheSize, int contextIdLength, byte* contextId, void* newSessionCb, void* removeSessionCb)
        {
            // A context is never shared between client and server.
            SSL_CTX_ctrl(ctx, SSL_CTRL_SET_SESS_CACHE_MODE, mode != 0 ? SSL_SESS_CACHE_BOTH : SSL_SESS_CACHE_OFF, null);
            if (mode == 0) SSL_CTX_set_options(ctx, SSL_OP_NO_TICKET);
            else if (cacheSize >= 0) SSL_CTX_ctrl(ctx, SSL_CTRL_SET_SESS_CACHE_SIZE, cacheSize, null);
            if (contextIdLength > 0 && contextId != null)
                SSL_CTX_set_session_id_context(ctx, contextId, contextIdLength <= SSL_MAX_SID_CTX_LENGTH ? (uint)contextIdLength : SSL_MAX_SID_CTX_LENGTH);
            if (newSessionCb != null) SSL_CTX_sess_set_new_cb(ctx, newSessionCb);
            if (removeSessionCb != null) SSL_CTX_sess_set_remove_cb(ctx, removeSessionCb);
            return 1;
        }

        [RuntimeExport("CryptoNative_SslCtxRemoveSession", Trigger + "CryptoNative_SslCtxRemoveSession")]
        public static int SslCtxRemoveSession(IntPtr ctx, IntPtr session) => SSL_CTX_remove_session(ctx, session);

        [RuntimeExport("CryptoNative_SslGetServerName", Trigger + "CryptoNative_SslGetServerName")]
        public static byte* SslGetServerName(IntPtr ssl) => SSL_get_servername(ssl, TLSEXT_NAMETYPE_host_name);

        [RuntimeExport("CryptoNative_SslGetSession", Trigger + "CryptoNative_SslGetSession")]
        public static IntPtr SslGetSession(IntPtr ssl) => SSL_get_session(ssl);

        [RuntimeExport("CryptoNative_SslSetSession", Trigger + "CryptoNative_SslSetSession")]
        public static int SslSetSession(IntPtr ssl, IntPtr session) => SSL_set_session(ssl, session);

        [RuntimeExport("CryptoNative_SslSessionFree", Trigger + "CryptoNative_SslSessionFree")]
        public static void SslSessionFree(IntPtr session) => SSL_SESSION_free(session);

        [RuntimeExport("CryptoNative_SslSessionGetHostname", Trigger + "CryptoNative_SslSessionGetHostname")]
        public static byte* SslSessionGetHostname(IntPtr session) => SSL_SESSION_get0_hostname(session);

        [RuntimeExport("CryptoNative_SslSessionSetHostname", Trigger + "CryptoNative_SslSessionSetHostname")]
        public static int SslSessionSetHostname(IntPtr session, byte* hostname)
        {
            SSL_SESSION_set1_hostname(session, hostname);
            return 0;
        }

        [RuntimeExport("CryptoNative_SslSessionSetData", Trigger + "CryptoNative_SslSessionSetData")]
        public static void SslSessionSetData(IntPtr session, IntPtr val) => SSL_SESSION_set_ex_data(session, g_ssl_sess_cert_index, val);

        [RuntimeExport("CryptoNative_SslSessionGetData", Trigger + "CryptoNative_SslSessionGetData")]
        public static IntPtr SslSessionGetData(IntPtr session) => SSL_SESSION_get_ex_data(session, g_ssl_sess_cert_index);

        [RuntimeExport("CryptoNative_SslCtxSetEncryptionPolicy", Trigger + "CryptoNative_SslCtxSetEncryptionPolicy")]
        public static int SslCtxSetEncryptionPolicy(IntPtr ctx, int policy)
        {
            switch (policy)
            {
                case 1: // AllowNoEncryption
                case 2: // NoEncryption
                    // No minimum security policy, as OpenSSL 1.0.
                    SSL_CTX_set_security_level(ctx, 0);
                    ResetCtxProtocolRestrictions(ctx);
                    return 1;
                case 0: // RequireEncryption
                    return 1;
            }
            return 0;
        }

        [UnmanagedCallersOnly]
        private static int DefaultOcspCallback(IntPtr ssl, void* args)
        {
            int ret = SSL_TLSEXT_ERR_NOACK;
            if (ssl != IntPtr.Zero)
            {
                byte* resp = null;
                long len = SSL_ctrl(ssl, SSL_CTRL_GET_TLSEXT_STATUS_REQ_OCSP_RESP, 0, &resp);
                // The stapled data was already provided.
                if (len > 0 && resp != null) ret = SSL_TLSEXT_ERR_OK;
            }
            return ret;
        }

        [RuntimeExport("CryptoNative_SslCtxSetDefaultOcspCallback", Trigger + "CryptoNative_SslCtxSetDefaultOcspCallback")]
        public static void SslCtxSetDefaultOcspCallback(IntPtr ctx) =>
            SSL_CTX_callback_ctrl(ctx, SSL_CTRL_SET_TLSEXT_STATUS_REQ_CB, (void*)(delegate* unmanaged<IntPtr, void*, int>)&DefaultOcspCallback);

        [RuntimeExport("CryptoNative_SslCtxSetCiphers", Trigger + "CryptoNative_SslCtxSetCiphers")]
        public static int SslCtxSetCiphers(IntPtr ctx, byte* cipherList, byte* cipherSuites)
        {
            ERR_clear_error();
            int ret = 1;
            // TLS 1.2 and lower.
            if (cipherList != null)
            {
                ret &= SSL_CTX_set_cipher_list(ctx, cipherList);
                if (ret == 0) return ret;
            }
            // TLS 1.3.
            if (Tls13Supported() != 0 && cipherSuites != null) ret &= SSL_CTX_set_ciphersuites(ctx, cipherSuites);
            return ret;
        }

        [RuntimeExport("CryptoNative_GetOpenSslCipherSuiteName", Trigger + "CryptoNative_GetOpenSslCipherSuiteName")]
        public static byte* GetOpenSslCipherSuiteName(IntPtr ssl, int cipherSuite, int* isTls12OrLower)
        {
            *isTls12OrLower = 0;
            byte* cs = stackalloc byte[2];
            cs[0] = (byte)((cipherSuite >> 8) & 0xFF);
            cs[1] = (byte)(cipherSuite & 0xFF);
            IntPtr cipher = SSL_CIPHER_find(ssl, cs);
            if (cipher == IntPtr.Zero) return null;
            byte* ret = SSL_CIPHER_get_name(cipher);
            if (ret == null) return null;
            byte* version = SSL_CIPHER_get_version(cipher);
            if (version == null) return ret;
            // DTLS follows the TLS rules.
            if (version[0] == (byte)'D') version++;
            if (version[0] == 'T' && version[1] == 'L' && version[2] == 'S' && version[3] == 'v' && version[4] == '1')
            {
                // TLSv1, TLSv1.0, TLSv1.1, TLSv1.2; anything else is newer.
                byte* tlsver = version + 5;
                *isTls12OrLower = tlsver[0] == 0 || (tlsver[0] == '.' && tlsver[1] >= '0' && tlsver[1] <= '2' && tlsver[2] == 0) ? 1 : 0;
            }
            else
                // Unknown: assumed new (OpenSSL ignores it at worst).
                *isTls12OrLower = version[0] == 'S' && version[1] == 'S' && version[2] == 'L' && version[3] == 'v' ? 1 : 0;
            return ret;
        }

        [RuntimeExport("CryptoNative_Tls13Supported", Trigger + "CryptoNative_Tls13Supported")]
        public static int Tls13Supported() => 1; // SSL_CTX_set_ciphersuites exists in every OpenSSL 3.

        [RuntimeExport("CryptoNative_SslCtxAddExtraChainCert", Trigger + "CryptoNative_SslCtxAddExtraChainCert")]
        public static int SslCtxAddExtraChainCert(IntPtr ctx, IntPtr x509)
        {
            ERR_clear_error();
            if (x509 == IntPtr.Zero || ctx == IntPtr.Zero) return 0;
            return SSL_CTX_ctrl(ctx, SSL_CTRL_EXTRA_CHAIN_CERT, 0, (void*)x509) == 1 ? 1 : 0;
        }

        [RuntimeExport("CryptoNative_SslAddExtraChainCert", Trigger + "CryptoNative_SslAddExtraChainCert")]
        public static int SslAddExtraChainCert(IntPtr ssl, IntPtr x509)
        {
            ERR_clear_error();
            if (x509 == IntPtr.Zero || ssl == IntPtr.Zero) return 0;
            return SSL_ctrl(ssl, SSL_CTRL_CHAIN_CERT, 1, (void*)x509) == 1 ? 1 : 0;
        }

        [RuntimeExport("CryptoNative_SslAddClientCAs", Trigger + "CryptoNative_SslAddClientCAs")]
        public static int SslAddClientCAs(IntPtr ssl, IntPtr* x509s, uint count)
        {
            if (x509s == null || ssl == IntPtr.Zero) return 0;
            for (uint i = 0; i < count; i++)
            {
                int res = SSL_add_client_CA(ssl, x509s[i]);
                if (res != 1) return res;
            }
            return 1;
        }

        [RuntimeExport("CryptoNative_SslCtxSetAlpnSelectCb", Trigger + "CryptoNative_SslCtxSetAlpnSelectCb")]
        public static void SslCtxSetAlpnSelectCb(IntPtr ctx, void* cb, void* arg)
        {
            if (s_emptyAlpn == null) s_emptyAlpn = (byte*)NativeMemory.AllocZeroed(1);
            SSL_CTX_set_alpn_select_cb(ctx, cb, s_emptyAlpn);
        }

        // A negative result pauses the handshake with SSL_ERROR_WANT_X509_LOOKUP, for the framework to pick a client certificate.
        [UnmanagedCallersOnly]
        private static int ClientCertificateCallback(IntPtr ssl, void* state) => -1;

        [RuntimeExport("CryptoNative_SslSetClientCertCallback", Trigger + "CryptoNative_SslSetClientCertCallback")]
        public static void SslSetClientCertCallback(IntPtr ssl, int set) =>
            SSL_set_cert_cb(ssl, set != 0 ? (void*)(delegate* unmanaged<IntPtr, void*, int>)&ClientCertificateCallback : null, null);

        [RuntimeExport("CryptoNative_SslCtxSetKeylogCallback", Trigger + "CryptoNative_SslCtxSetKeylogCallback")]
        public static void SslCtxSetKeylogCallback(IntPtr ctx, void* cb) => SSL_CTX_set_keylog_callback(ctx, cb);

        [RuntimeExport("CryptoNative_SslSetPostHandshakeAuth", Trigger + "CryptoNative_SslSetPostHandshakeAuth")]
        public static void SslSetPostHandshakeAuth(IntPtr ssl, int val) => SSL_set_post_handshake_auth(ssl, val);

        [RuntimeExport("CryptoNative_SslSetData", Trigger + "CryptoNative_SslSetData")]
        public static int SslSetData(IntPtr ssl, IntPtr ptr)
        {
            ERR_clear_error();
            return SSL_set_ex_data(ssl, 0, ptr);
        }

        [RuntimeExport("CryptoNative_SslGetData", Trigger + "CryptoNative_SslGetData")]
        public static IntPtr SslGetData(IntPtr ssl) => SSL_get_ex_data(ssl, 0);

        [RuntimeExport("CryptoNative_SslCtxSetData", Trigger + "CryptoNative_SslCtxSetData")]
        public static int SslCtxSetData(IntPtr ctx, IntPtr ptr) => SSL_CTX_set_ex_data(ctx, 0, ptr);

        [RuntimeExport("CryptoNative_SslCtxGetData", Trigger + "CryptoNative_SslCtxGetData")]
        public static IntPtr SslCtxGetData(IntPtr ctx) => SSL_CTX_get_ex_data(ctx, 0);

        [RuntimeExport("CryptoNative_SslSetAlpnProtos", Trigger + "CryptoNative_SslSetAlpnProtos")]
        public static int SslSetAlpnProtos(IntPtr ssl, byte* protos, uint protosLen)
        {
            ERR_clear_error();
            return SSL_set_alpn_protos(ssl, protos, protosLen);
        }

        [RuntimeExport("CryptoNative_SslGet0AlpnSelected", Trigger + "CryptoNative_SslGet0AlpnSelected")]
        public static void SslGet0AlpnSelected(IntPtr ssl, byte** protocol, uint* len) => SSL_get0_alpn_selected(ssl, protocol, len);

        [RuntimeExport("CryptoNative_SslSetTlsExtHostName", Trigger + "CryptoNative_SslSetTlsExtHostName")]
        public static int SslSetTlsExtHostName(IntPtr ssl, byte* name)
        {
            ERR_clear_error();
            return (int)SSL_ctrl(ssl, SSL_CTRL_SET_TLSEXT_HOSTNAME, TLSEXT_NAMETYPE_host_name, name);
        }

        [RuntimeExport("CryptoNative_SslSetSigalgs", Trigger + "CryptoNative_SslSetSigalgs")]
        public static int SslSetSigalgs(IntPtr ssl, byte* str)
        {
            ERR_clear_error();
            return (int)SSL_ctrl(ssl, SSL_CTRL_SET_SIGALGS_LIST, 0, str);
        }

        [RuntimeExport("CryptoNative_SslSetClientSigalgs", Trigger + "CryptoNative_SslSetClientSigalgs")]
        public static int SslSetClientSigalgs(IntPtr ssl, byte* str)
        {
            if (ssl == IntPtr.Zero || str == null) return 0;
            return (int)SSL_ctrl(ssl, SSL_CTRL_SET_CLIENT_SIGALGS_LIST, 0, str);
        }

        [RuntimeExport("CryptoNative_SslGetCurrentCipherId", Trigger + "CryptoNative_SslGetCurrentCipherId")]
        public static int SslGetCurrentCipherId(IntPtr ssl, int* cipherId)
        {
            IntPtr cipher = SSL_get_current_cipher(ssl);
            // Inside the verify callback TLS 1.2 has no current cipher yet (set at ChangeCipherSpec): the pending one.
            if (cipher == IntPtr.Zero) cipher = SSL_get_pending_cipher(ssl);
            if (cipher == IntPtr.Zero)
            {
                *cipherId = -1;
                return 0;
            }
            // The low two bytes of OpenSSL's id are the IANA value.
            *cipherId = (int)(SSL_CIPHER_get_id(cipher) & 0xFFFF);
            return 1;
        }

        // The signature algorithms a default server context offers, read from a client hello sent to it over memory BIOs.
        [RuntimeExport("CryptoNative_GetDefaultSignatureAlgorithms", Trigger + "CryptoNative_GetDefaultSignatureAlgorithms")]
        public static int GetDefaultSignatureAlgorithms(ushort* buffer, int* count)
        {
            int ret = 0;
            IntPtr clientCtx = SslCtxCreate(TLS_method());
            IntPtr serverCtx = SslCtxCreate(TLS_method());
            IntPtr bio1 = BIO_new(BIO_s_mem());
            IntPtr bio2 = BIO_new(BIO_s_mem());
            IntPtr client = IntPtr.Zero, server = IntPtr.Zero;
            if (clientCtx != IntPtr.Zero && serverCtx != IntPtr.Zero && bio1 != IntPtr.Zero && bio2 != IntPtr.Zero)
            {
                SSL_CTX_set_verify(clientCtx, SSL_VERIFY_NONE, null);
                SSL_CTX_set_verify(serverCtx, SSL_VERIFY_NONE, null);
                server = SslCreate(serverCtx);
                SSL_set_accept_state(server);
                client = SslCreate(clientCtx);
                SSL_set_connect_state(client);
                // Crossed; SSL_set_bio takes ownership and each BIO is shared, hence the extra references.
                SSL_set_bio(client, bio1, bio2);
                SSL_set_bio(server, bio2, bio1);
                BIO_up_ref(bio1);
                BIO_up_ref(bio2);
                bio1 = IntPtr.Zero;
                bio2 = IntPtr.Zero;
                SSL_do_handshake(client);
                SSL_do_handshake(server);
                int c = SSL_get_sigalgs(server, 0, null, null, null, null, null);
                if (c > 0)
                {
                    for (int i = 0; i < c; i++)
                    {
                        // Bounded by the caller's count.
                        if (i >= *count)
                        {
                            ret = -1;
                            break;
                        }
                        byte sig, hash;
                        SSL_get_sigalgs(server, i, null, null, null, &sig, &hash);
                        buffer[i] = (ushort)(hash << 8 | sig);
                    }
                    *count = c;
                    ret = 0;
                }
            }
            if (bio1 != IntPtr.Zero) BIO_free(bio1);
            if (bio2 != IntPtr.Zero) BIO_free(bio2);
            if (client != IntPtr.Zero) SSL_free(client);
            if (server != IntPtr.Zero) SSL_free(server);
            // The shim leaks the two contexts here (SSL_free drops only the SSLs' references); freed in csharpc, which changes nothing visible.
            if (clientCtx != IntPtr.Zero) SSL_CTX_free(clientCtx);
            if (serverCtx != IntPtr.Zero) SSL_CTX_free(serverCtx);
            ERR_clear_error();
            return ret;
        }

        [RuntimeExport("CryptoNative_SslStapleOcsp", Trigger + "CryptoNative_SslStapleOcsp")]
        public static void SslStapleOcsp(IntPtr ssl, byte* buf, int len)
        {
            if (ssl == IntPtr.Zero || buf == null || len <= 0) return;
            // OpenSSL frees the response with OPENSSL_free when the SSL goes: a fresh copy from its allocator.
            void* copy = OpensslMalloc((nuint)len);
            if (copy == null) return;
            Buffer.MemoryCopy(buf, copy, len, len);
            if (SSL_ctrl(ssl, SSL_CTRL_SET_TLSEXT_STATUS_REQ_OCSP_RESP, len, copy) != 1) OpensslFree(copy);
        }

        [RuntimeExport("CryptoNative_SslCtxSetCertVerifyCallback", Trigger + "CryptoNative_SslCtxSetCertVerifyCallback")]
        public static void SslCtxSetCertVerifyCallback(IntPtr ctx, void* callback)
        {
            if (ctx != IntPtr.Zero) SSL_CTX_set_cert_verify_callback(ctx, callback, null);
        }

        [RuntimeExport("CryptoNative_X509StoreCtxGetSslPtr", Trigger + "CryptoNative_X509StoreCtxGetSslPtr")]
        public static IntPtr X509StoreCtxGetSslPtr(IntPtr storeCtx) => X509_STORE_CTX_get_ex_data(storeCtx, SSL_get_ex_data_X509_STORE_CTX_idx());

        [RuntimeExport("CryptoNative_X509StoreCtxSetError", Trigger + "CryptoNative_X509StoreCtxSetError")]
        public static void X509StoreCtxSetError(IntPtr storeCtx, int error) => X509_STORE_CTX_set_error(storeCtx, error);

        [RuntimeExport("CryptoNative_SslGetSslCtx", Trigger + "CryptoNative_SslGetSslCtx")]
        public static IntPtr SslGetSslCtx(IntPtr ssl) => SSL_get_SSL_CTX(ssl);
    }
}
#endif
