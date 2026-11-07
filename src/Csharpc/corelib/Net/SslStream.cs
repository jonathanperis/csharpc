// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Net.Security/src/System/Net/Security/
// {AuthenticatedStream,SslStream,SslStream.IO,SslStream.Protocol}.cs and the libssl calls of Interop.OpenSsl; see
// THIRD-PARTY-NOTICES.md.
// SslStream on Linux: TLS 1.2 / 1.3 client and server over libssl 3 with memory BIOs (docs/adr/0022-tls.md). The
// handshake and the data path move TLS records between the BIO pair and the inner stream, synchronously or
// asynchronously; the certificate validation runs inside libssl's certificate-verify callback, as .NET's does, so a
// rejected certificate aborts the handshake with the alert the peer sees. The validation logic, the policy errors, the
// exception types and messages are .NET's. Not implemented (explicit NotSupportedException or compile-time
// diagnostic): server certificate selection callbacks and SslClientHelloInfo callbacks, local certificate selection
// callbacks, cipher suite policies, client certificate renegotiation (NegotiateClientCertificateAsync), the
// CipherAlgorithm/HashAlgorithm/KeyExchangeAlgorithm properties.
#nullable disable
#if CSHARPC_OPENSSL
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Net.Security.AuthenticatedStream")]
    public abstract class AuthenticatedStreamImpl : Stream
    {
        private readonly Stream _innerStream;
        private readonly bool _leaveStreamOpen;

        protected AuthenticatedStreamImpl(Stream innerStream, bool leaveInnerStreamOpen)
        {
            if (innerStream == null || innerStream == Stream.Null) throw new ArgumentNullException("innerStream");
            if (!innerStream.CanRead || !innerStream.CanWrite) throw new ArgumentException("The stream is not readable and writable.", "innerStream");
            _innerStream = innerStream;
            _leaveStreamOpen = leaveInnerStreamOpen;
        }

        public bool LeaveInnerStreamOpen => _leaveStreamOpen;

        protected Stream InnerStream => _innerStream;

        public abstract bool IsAuthenticated { get; }
        public abstract bool IsMutuallyAuthenticated { get; }
        public abstract bool IsEncrypted { get; }
        public abstract bool IsSigned { get; }
        public abstract bool IsServer { get; }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    if (_leaveStreamOpen) _innerStream.Flush();
                    else _innerStream.Close();
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (_leaveStreamOpen) await _innerStream.FlushAsync().ConfigureAwait(false);
                else await _innerStream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>System.Net.Security.SslException (internal in .NET): the native handshake failure under an AuthenticationException.</summary>
    internal sealed class SslException : Exception
    {
        internal SslException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>One TLS connection in libssl: the context, the SSL object and the BIO pair the records pass through.</summary>
    internal sealed unsafe class TlsSession
    {
        private const int SSL_ERROR_SSL = 1, SSL_ERROR_WANT_READ = 2, SSL_ERROR_WANT_WRITE = 3, SSL_ERROR_SYSCALL = 5, SSL_ERROR_ZERO_RETURN = 6;
        private const int SSL_VERIFY_PEER = 1;

        private static readonly Dictionary<IntPtr, TlsSession> s_sessions = new Dictionary<IntPtr, TlsSession>();

        internal IntPtr Ctx, Ssl, ReadBio, WriteBio;
        internal readonly bool IsServer;
        internal readonly SslStreamImpl Owner;
        internal Exception ValidationException;
        private IntPtr _alpnNative;
        private bool _freed;
        internal readonly object Lock = new object();

        internal TlsSession(SslStreamImpl owner, bool isServer)
        {
            Owner = owner;
            IsServer = isServer;
        }

        [UnmanagedCallersOnly]
        private static int CertVerifyCallback(IntPtr storeCtx, IntPtr arg)
        {
            TlsSession session = null;
            try
            {
                IntPtr ssl = Ossl.X509_STORE_CTX_get_ex_data(storeCtx, Ossl.SSL_get_ex_data_X509_STORE_CTX_idx());
                lock (s_sessions) s_sessions.TryGetValue(ssl, out session);
                if (session == null) return 0;
                IntPtr leaf = Ossl.X509_STORE_CTX_get0_cert(storeCtx);
                byte[] der = leaf == IntPtr.Zero ? null : Ossl.ToDer(leaf);
                var extra = new List<byte[]>();
                IntPtr untrusted = Ossl.X509_STORE_CTX_get0_untrusted(storeCtx);
                if (untrusted != IntPtr.Zero)
                {
                    int n = Ossl.OPENSSL_sk_num(untrusted);
                    for (int i = 0; i < n; i++)
                    {
                        byte[] e = Ossl.ToDer(Ossl.OPENSSL_sk_value(untrusted, i));
                        if (der == null || !((ReadOnlySpan<byte>)e).SequenceEqual(der)) extra.Add(e);
                    }
                }
                Exception failure = session.Owner.ValidateRemote(der, extra);
                if (failure == null) return 1;
                session.ValidationException = failure;
                Ossl.X509_STORE_CTX_set_error(storeCtx, 50); // X509_V_ERR_APPLICATION_VERIFICATION
                return 0;
            }
            catch (Exception e)
            {
                if (session != null) session.ValidationException = e;
                return 0;
            }
        }

        [UnmanagedCallersOnly]
        private static int VerifyAlways(int preverify, IntPtr storeCtx) => 1;

        [UnmanagedCallersOnly]
        private static int AlpnSelectCallback(IntPtr ssl, byte** output, byte* outlen, byte* input, uint inlen, IntPtr arg)
        {
            // arg: native copy of the server's list as a uint length and then the wire-format protocols.
            uint serverLength = *(uint*)arg;
            byte* server = (byte*)arg + sizeof(uint);
            return Ossl.SSL_select_next_proto(output, outlen, server, serverLength, input, inlen) == 1 ? 0 /* SSL_TLSEXT_ERR_OK */ : 3 /* SSL_TLSEXT_ERR_NOACK */;
        }

        internal void Setup(SslStreamImpl.AuthState a)
        {
            Ossl.EnsureInitialized();
            Ctx = Ossl.SSL_CTX_new(IsServer ? Ossl.TLS_server_method() : Ossl.TLS_client_method());
            if (Ctx == IntPtr.Zero) throw Ossl.Error();
            SetProtocols(a.EnabledProtocols);
            if (!a.AllowTlsResume) Ossl.SSL_CTX_set_options(Ctx, 0x4000 /* SSL_OP_NO_TICKET */);
            if (!a.AllowTlsResume) Ossl.SSL_CTX_ctrl(Ctx, 44 /* SSL_CTRL_SET_SESS_CACHE_MODE */, 0, IntPtr.Zero);

            X509Certificate2Impl local = a.LocalCertificate;
            if (local != null)
            {
                if (Ossl.SSL_CTX_use_certificate(Ctx, local.Handle) != 1) throw Ossl.Error();
                if (Ossl.SSL_CTX_use_PrivateKey(Ctx, local._key) != 1) throw Ossl.Error();
                if (Ossl.SSL_CTX_check_private_key(Ctx) != 1) throw Ossl.Error();
                if (local._others != null)
                    foreach (byte[] der in local._others)
                    {
                        IntPtr x = Ossl.FromDer(der);
                        if (x != IntPtr.Zero && Ossl.SSL_CTX_ctrl(Ctx, 14 /* SSL_CTRL_EXTRA_CHAIN_CERT */, 0, x) != 1) Ossl.X509_free(x);
                    }
            }

            // Certificates are validated by the managed callback, never by libssl's own store walk.
            Ossl.SSL_CTX_set_cert_verify_callback(Ctx, &CertVerifyCallback, IntPtr.Zero);
            if (IsServer)
            {
                byte* sid = stackalloc byte[4];
                sid[0] = (byte)'c'; sid[1] = (byte)'s'; sid[2] = (byte)'r'; sid[3] = (byte)'t';
                Ossl.SSL_CTX_set_session_id_context(Ctx, sid, 4);
                Ossl.SSL_CTX_set_verify(Ctx, a.ClientCertificateRequired ? SSL_VERIFY_PEER : 0, &VerifyAlways);
            }
            else Ossl.SSL_CTX_set_verify(Ctx, SSL_VERIFY_PEER, &VerifyAlways);

            if (a.ApplicationProtocols != null && a.ApplicationProtocols.Count > 0)
            {
                var wire = new List<byte>();
                foreach (SslApplicationProtocol p in a.ApplicationProtocols)
                {
                    ReadOnlyMemory<byte> bytes = p.Protocol;
                    wire.Add((byte)bytes.Length);
                    wire.AddRange(bytes.ToArray());
                }
                byte[] list = wire.ToArray();
                if (IsServer)
                {
                    _alpnNative = Marshal.AllocHGlobal(list.Length + sizeof(uint));
                    *(uint*)_alpnNative = (uint)list.Length;
                    list.AsSpan().CopyTo(new Span<byte>((byte*)_alpnNative + sizeof(uint), list.Length));
                    Ossl.SSL_CTX_set_alpn_select_cb(Ctx, &AlpnSelectCallback, _alpnNative);
                }
                else
                {
                    fixed (byte* l = list)
                        if (Ossl.SSL_CTX_set_alpn_protos(Ctx, l, (uint)list.Length) != 0) throw Ossl.Error();
                }
            }

            Ssl = Ossl.SSL_new(Ctx);
            if (Ssl == IntPtr.Zero) throw Ossl.Error();
            ReadBio = Ossl.BIO_new(Ossl.BIO_s_mem());
            WriteBio = Ossl.BIO_new(Ossl.BIO_s_mem());
            if (ReadBio == IntPtr.Zero || WriteBio == IntPtr.Zero) throw Ossl.Error();
            Ossl.SSL_set_bio(Ssl, ReadBio, WriteBio); // the SSL owns the BIOs now
            lock (s_sessions) s_sessions[Ssl] = this;
            if (IsServer) Ossl.SSL_set_accept_state(Ssl);
            else
            {
                Ossl.SSL_set_connect_state(Ssl);
                string host = a.TargetHost;
                if (!string.IsNullOrEmpty(host) && !IPAddress.TryParse(host, out _))
                {
                    byte[] name = Ossl.Utf8Z(host.TrimEnd('.'));
                    fixed (byte* n = name) Ossl.SSL_ctrl(Ssl, 55 /* SSL_CTRL_SET_TLSEXT_HOSTNAME */, 0 /* host_name */, (IntPtr)n);
                }
            }
        }

        private void SetProtocols(SslProtocols protocols)
        {
            if (protocols == SslProtocols.None) return;
#pragma warning disable SYSLIB0039
            int lowest = 0, highest = 0;
            if ((protocols & SslProtocols.Tls) != 0) { lowest = lowest == 0 ? 0x301 : lowest; highest = 0x301; }
            if ((protocols & SslProtocols.Tls11) != 0) { lowest = lowest == 0 ? 0x302 : lowest; highest = 0x302; }
            if ((protocols & SslProtocols.Tls12) != 0) { lowest = lowest == 0 ? 0x303 : lowest; highest = 0x303; }
            if ((protocols & SslProtocols.Tls13) != 0) { lowest = lowest == 0 ? 0x304 : lowest; highest = 0x304; }
#pragma warning restore SYSLIB0039
            if (highest == 0) return; // only SSL 2/3: nothing libssl can speak
            Ossl.SSL_CTX_ctrl(Ctx, 123 /* SSL_CTRL_SET_MIN_PROTO_VERSION */, lowest, IntPtr.Zero);
            Ossl.SSL_CTX_ctrl(Ctx, 124 /* SSL_CTRL_SET_MAX_PROTO_VERSION */, highest, IntPtr.Zero);
        }

        /// <summary>One step of the handshake: 1 finished, 0 more input needed; anything else throws.</summary>
        internal int Handshake(out byte[] output)
        {
            lock (Lock)
            {
                Ossl.ERR_clear_error();
                int rc = Ossl.SSL_do_handshake(Ssl);
                int status = 0;
                if (rc != 1)
                {
                    int error = Ossl.SSL_get_error(Ssl, rc);
                    if (error == SSL_ERROR_WANT_READ || error == SSL_ERROR_WANT_WRITE) status = 0;
                    else
                    {
                        // An alert goes out even though the handshake failed: the caller sends it before it throws.
                        throw new HandshakeFailure(error, Ossl.TakeErrorString(), Drain());
                    }
                }
                else status = 1;
                output = Drain();
                return status;
            }
        }

        internal sealed class HandshakeFailure : Exception
        {
            internal readonly int Error;
            internal readonly string OpenSslText;
            internal readonly byte[] Alert;
            internal HandshakeFailure(int error, string text, byte[] alert = null) : base(text) { Error = error; OpenSslText = text; Alert = alert; }
        }

        private byte[] Drain()
        {
            int pending = (int)Ossl.BIO_ctrl_pending(WriteBio);
            if (pending <= 0) return null;
            byte[] data = new byte[pending];
            fixed (byte* p = data)
            {
                int n = Ossl.BIO_read(WriteBio, p, pending);
                if (n <= 0) return null;
                if (n != pending) Array.Resize(ref data, n);
            }
            return data;
        }

        internal void Feed(byte[] data, int offset, int count)
        {
            lock (Lock)
            {
                fixed (byte* p = data)
                    if (Ossl.BIO_write(ReadBio, p + offset, count) != count) throw Ossl.Error();
            }
        }

        /// <summary>Decrypts into <paramref name="destination"/>: bytes read, 0 for the peer's close_notify, -1 when more input is needed.</summary>
        internal int Read(Span<byte> destination, out byte[] output)
        {
            lock (Lock)
            {
                Ossl.ERR_clear_error();
                int n;
                fixed (byte* p = destination) n = Ossl.SSL_read(Ssl, p, destination.Length);
                if (n > 0) { output = Drain(); return n; }
                int error = Ossl.SSL_get_error(Ssl, n);
                output = Drain();
                if (error == SSL_ERROR_ZERO_RETURN) return 0;
                if (error == SSL_ERROR_WANT_READ) return -1;
                string text = Ossl.TakeErrorString();
                throw new HandshakeFailure(error, text);
            }
        }

        internal byte[] Write(ReadOnlySpan<byte> data)
        {
            lock (Lock)
            {
                Ossl.ERR_clear_error();
                int written = 0;
                while (written < data.Length)
                {
                    int n;
                    fixed (byte* p = data.Slice(written)) n = Ossl.SSL_write(Ssl, p, Math.Min(data.Length - written, 1 << 30));
                    if (n <= 0)
                    {
                        string text = Ossl.TakeErrorString();
                        throw new HandshakeFailure(Ossl.SSL_get_error(Ssl, n), text);
                    }
                    written += n;
                }
                return Drain();
            }
        }

        internal byte[] Shutdown()
        {
            lock (Lock)
            {
                if (Ssl == IntPtr.Zero) return null;
                Ossl.ERR_clear_error();
                Ossl.SSL_shutdown(Ssl);
                Ossl.ERR_clear_error();
                return Drain();
            }
        }

        internal int ProtocolVersion => Ossl.SSL_version(Ssl);

        internal uint CipherId
        {
            get
            {
                IntPtr cipher = Ossl.SSL_get_current_cipher(Ssl);
                return cipher == IntPtr.Zero ? 0 : Ossl.SSL_CIPHER_get_id(cipher);
            }
        }

        internal byte[] NegotiatedAlpn()
        {
            byte* data;
            uint length;
            Ossl.SSL_get0_alpn_selected(Ssl, &data, &length);
            if (data == null || length == 0) return null;
            return new ReadOnlySpan<byte>(data, (int)length).ToArray();
        }

        internal string ServerName()
        {
            IntPtr name = Ossl.SSL_get_servername(Ssl, 0);
            return name == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(name);
        }

        internal byte[] PeerCertificate()
        {
            IntPtr x = Ossl.SSL_get1_peer_certificate(Ssl);
            if (x == IntPtr.Zero) return null;
            byte[] der = Ossl.ToDer(x);
            Ossl.X509_free(x);
            return der;
        }

        internal void Free()
        {
            lock (Lock)
            {
                if (_freed) return;
                _freed = true;
                if (Ssl != IntPtr.Zero)
                {
                    lock (s_sessions) s_sessions.Remove(Ssl);
                    Ossl.SSL_free(Ssl); // frees the BIOs
                }
                if (Ctx != IntPtr.Zero) Ossl.SSL_CTX_free(Ctx);
                if (_alpnNative != IntPtr.Zero) Marshal.FreeHGlobal(_alpnNative);
                Ssl = Ctx = IntPtr.Zero;
            }
        }
    }

    [Shadows("System.Net.Security.SslStream")]
    public class SslStreamImpl : AuthenticatedStreamImpl
    {
        /// <summary>What an authentication needs from SslClientAuthenticationOptions / SslServerAuthenticationOptions.</summary>
        internal sealed class AuthState
        {
            internal bool IsServer;
            internal string TargetHost = "";
            internal SslProtocols EnabledProtocols;
            internal bool AllowTlsResume = true;
            internal bool ClientCertificateRequired;
            internal X509Certificate2Impl LocalCertificate;
            internal X509RevocationMode RevocationMode;
            internal X509ChainPolicy ChainPolicy;
            internal List<SslApplicationProtocol> ApplicationProtocols;
            internal RemoteCertificateValidationCallback ValidationCallback;
            internal bool CheckCertName => !IsServer;
        }

        private static readonly Oid s_serverAuthOid = new Oid("1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.1");
        private static readonly Oid s_clientAuthOid = new Oid("1.3.6.1.5.5.7.3.2", "1.3.6.1.5.5.7.3.2");

        private readonly RemoteCertificateValidationCallback _userCertificateValidationCallback;
        private readonly LocalCertificateSelectionCallback _userCertificateSelectionCallback;
        private readonly EncryptionPolicy _encryptionPolicy;
        private AuthState _state = new AuthState();
        private TlsSession _session;
        private bool _handshakeCompleted;
        private bool _authenticating;
        private bool _shutdown;
        private bool _disposed;
        private Exception _exception;
        private X509Certificate2 _remoteCertificate;
        private bool _remoteCertificateSet;
        private SslProtocols _protocol;
        private TlsCipherSuite _cipherSuite;
        private byte[] _alpn;
        private string _serverName;
        private readonly byte[] _readBuffer = new byte[16 * 1024 + 256];

        public SslStreamImpl(Stream innerStream) : this(innerStream, false, null, null) { }

        public SslStreamImpl(Stream innerStream, bool leaveInnerStreamOpen)
            : this(innerStream, leaveInnerStreamOpen, null, null, EncryptionPolicy.RequireEncryption) { }

        public SslStreamImpl(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback userCertificateValidationCallback)
            : this(innerStream, leaveInnerStreamOpen, userCertificateValidationCallback, null, EncryptionPolicy.RequireEncryption) { }

        public SslStreamImpl(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback userCertificateValidationCallback,
            LocalCertificateSelectionCallback userCertificateSelectionCallback)
            : this(innerStream, leaveInnerStreamOpen, userCertificateValidationCallback, userCertificateSelectionCallback, EncryptionPolicy.RequireEncryption) { }

        public SslStreamImpl(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback userCertificateValidationCallback,
            LocalCertificateSelectionCallback userCertificateSelectionCallback, EncryptionPolicy encryptionPolicy)
            : base(innerStream, leaveInnerStreamOpen)
        {
#pragma warning disable SYSLIB0040
            if (encryptionPolicy != EncryptionPolicy.RequireEncryption && encryptionPolicy != EncryptionPolicy.AllowNoEncryption && encryptionPolicy != EncryptionPolicy.NoEncryption)
                throw new ArgumentException("The specified value is not valid in the 'EncryptionPolicy' enumeration.", "encryptionPolicy");
#pragma warning restore SYSLIB0040
            _userCertificateValidationCallback = userCertificateValidationCallback;
            _userCertificateSelectionCallback = userCertificateSelectionCallback;
            _encryptionPolicy = encryptionPolicy;
        }

        // ---- authentication ----

        public virtual void AuthenticateAsClient(string targetHost) => AuthenticateAsClient(targetHost, null, SslProtocols.None, false);

        public virtual void AuthenticateAsClient(string targetHost, X509CertificateCollection clientCertificates, bool checkCertificateRevocation)
            => AuthenticateAsClient(targetHost, clientCertificates, SslProtocols.None, checkCertificateRevocation);

        public virtual void AuthenticateAsClient(string targetHost, X509CertificateCollection clientCertificates, SslProtocols enabledSslProtocols, bool checkCertificateRevocation)
        {
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                ClientCertificates = clientCertificates,
                EnabledSslProtocols = enabledSslProtocols,
                CertificateRevocationCheckMode = checkCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
                EncryptionPolicy = _encryptionPolicy,
            };
            AuthenticateAsClient(options);
        }

        public void AuthenticateAsClient(SslClientAuthenticationOptions sslClientAuthenticationOptions)
        {
            ArgumentNullException.ThrowIfNull(sslClientAuthenticationOptions);
            ThrowIfExceptional();
            Authenticate(ClientState(sslClientAuthenticationOptions), true, default).GetAwaiter().GetResult();
        }

        public virtual Task AuthenticateAsClientAsync(string targetHost) => AuthenticateAsClientAsync(targetHost, null, false);

        public virtual Task AuthenticateAsClientAsync(string targetHost, X509CertificateCollection clientCertificates, bool checkCertificateRevocation)
            => AuthenticateAsClientAsync(targetHost, clientCertificates, SslProtocols.None, checkCertificateRevocation);

        public virtual Task AuthenticateAsClientAsync(string targetHost, X509CertificateCollection clientCertificates, SslProtocols enabledSslProtocols, bool checkCertificateRevocation)
        {
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                ClientCertificates = clientCertificates,
                EnabledSslProtocols = enabledSslProtocols,
                CertificateRevocationCheckMode = checkCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
                EncryptionPolicy = _encryptionPolicy,
            };
            return AuthenticateAsClientAsync(options);
        }

        public Task AuthenticateAsClientAsync(SslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sslClientAuthenticationOptions);
            ThrowIfExceptional();
            return Authenticate(ClientState(sslClientAuthenticationOptions), false, cancellationToken);
        }

        public virtual void AuthenticateAsServer(X509Certificate serverCertificate) => AuthenticateAsServer(serverCertificate, false, SslProtocols.None, false);

        public virtual void AuthenticateAsServer(X509Certificate serverCertificate, bool clientCertificateRequired, bool checkCertificateRevocation)
            => AuthenticateAsServer(serverCertificate, clientCertificateRequired, SslProtocols.None, checkCertificateRevocation);

        public virtual void AuthenticateAsServer(X509Certificate serverCertificate, bool clientCertificateRequired, SslProtocols enabledSslProtocols, bool checkCertificateRevocation)
        {
            var options = new SslServerAuthenticationOptions
            {
                ServerCertificate = serverCertificate,
                ClientCertificateRequired = clientCertificateRequired,
                EnabledSslProtocols = enabledSslProtocols,
                CertificateRevocationCheckMode = checkCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
                EncryptionPolicy = _encryptionPolicy,
            };
            AuthenticateAsServer(options);
        }

        public void AuthenticateAsServer(SslServerAuthenticationOptions sslServerAuthenticationOptions)
        {
            ArgumentNullException.ThrowIfNull(sslServerAuthenticationOptions);
            Authenticate(ServerState(sslServerAuthenticationOptions), true, default).GetAwaiter().GetResult();
        }

        public virtual Task AuthenticateAsServerAsync(X509Certificate serverCertificate) => AuthenticateAsServerAsync(serverCertificate, false, false);

        public virtual Task AuthenticateAsServerAsync(X509Certificate serverCertificate, bool clientCertificateRequired, bool checkCertificateRevocation)
            => AuthenticateAsServerAsync(serverCertificate, clientCertificateRequired, SslProtocols.None, checkCertificateRevocation);

        public virtual Task AuthenticateAsServerAsync(X509Certificate serverCertificate, bool clientCertificateRequired, SslProtocols enabledSslProtocols, bool checkCertificateRevocation)
        {
            var options = new SslServerAuthenticationOptions
            {
                ServerCertificate = serverCertificate,
                ClientCertificateRequired = clientCertificateRequired,
                EnabledSslProtocols = enabledSslProtocols,
                CertificateRevocationCheckMode = checkCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
                EncryptionPolicy = _encryptionPolicy,
            };
            return AuthenticateAsServerAsync(options);
        }

        public Task AuthenticateAsServerAsync(SslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sslServerAuthenticationOptions);
            return Authenticate(ServerState(sslServerAuthenticationOptions), false, cancellationToken);
        }

        public Task AuthenticateAsServerAsync(ServerOptionsSelectionCallback optionsCallback, object state, CancellationToken cancellationToken = default)
            => throw Tls.NotSupported("SslStream.AuthenticateAsServerAsync with a ServerOptionsSelectionCallback (SNI-based option selection)");

        public virtual Task NegotiateClientCertificateAsync(CancellationToken cancellationToken = default)
            => throw Tls.NotSupported("SslStream.NegotiateClientCertificateAsync");

        private void CheckConstructorCallbacks(RemoteCertificateValidationCallback validation, LocalCertificateSelectionCallback selection)
        {
            if (validation != null && _userCertificateValidationCallback != null)
                throw new InvalidOperationException("The 'RemoteCertificateValidationCallback' option was already set in the SslStream constructor.");
            if (selection != null && _userCertificateSelectionCallback != null)
                throw new InvalidOperationException("The 'LocalCertificateSelectionCallback' option was already set in the SslStream constructor.");
        }

        private AuthState ClientState(SslClientAuthenticationOptions o)
        {
            CheckConstructorCallbacks(o.RemoteCertificateValidationCallback, o.LocalCertificateSelectionCallback);
            var s = new AuthState
            {
                IsServer = false,
                TargetHost = o.TargetHost ?? "",
                EnabledProtocols = o.EnabledSslProtocols,
                AllowTlsResume = o.AllowTlsResume,
                RevocationMode = o.CertificateRevocationCheckMode,
                ChainPolicy = o.CertificateChainPolicy,
                ApplicationProtocols = o.ApplicationProtocols,
                ValidationCallback = o.RemoteCertificateValidationCallback ?? _userCertificateValidationCallback,
            };
            if (o.LocalCertificateSelectionCallback != null || _userCertificateSelectionCallback != null)
                throw Tls.NotSupported("SslStream with a LocalCertificateSelectionCallback");
            if (o.CipherSuitesPolicy != null) throw Tls.NotSupported("SslClientAuthenticationOptions.CipherSuitesPolicy");
            if (o.ClientCertificateContext != null) throw Tls.NotSupported("SslClientAuthenticationOptions.ClientCertificateContext");
            if (o.ClientCertificates != null)
                foreach (X509Certificate c in o.ClientCertificates)
                    if (c is X509Certificate2 c2 && ((X509Certificate2Impl)(object)c2).HasPrivateKey) { s.LocalCertificate = (X509Certificate2Impl)(object)c2; break; }
            return s;
        }

        private AuthState ServerState(SslServerAuthenticationOptions o)
        {
            CheckConstructorCallbacks(o.RemoteCertificateValidationCallback, null);
            var s = new AuthState
            {
                IsServer = true,
                EnabledProtocols = o.EnabledSslProtocols,
                AllowTlsResume = o.AllowTlsResume,
                ClientCertificateRequired = o.ClientCertificateRequired,
                RevocationMode = o.CertificateRevocationCheckMode,
                ChainPolicy = o.CertificateChainPolicy,
                ApplicationProtocols = o.ApplicationProtocols,
                ValidationCallback = o.RemoteCertificateValidationCallback ?? _userCertificateValidationCallback,
            };
            if (o.ServerCertificateSelectionCallback != null) throw Tls.NotSupported("SslServerAuthenticationOptions.ServerCertificateSelectionCallback");
            if (o.ServerCertificateContext != null) throw Tls.NotSupported("SslServerAuthenticationOptions.ServerCertificateContext");
            if (o.CipherSuitesPolicy != null) throw Tls.NotSupported("SslServerAuthenticationOptions.CipherSuitesPolicy");
            X509Certificate cert = o.ServerCertificate;
            if (cert == null) throw new NotSupportedException("The server mode SSL must use a certificate with the associated private key.");
            if (cert is X509Certificate2 c2 && ((X509Certificate2Impl)(object)c2).HasPrivateKey) s.LocalCertificate = (X509Certificate2Impl)(object)c2;
            else throw new AuthenticationException("The server mode SSL must use a certificate with the associated private key.");
            return s;
        }

        private async Task Authenticate(AuthState state, bool sync, CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            if (_authenticating || _handshakeCompleted) throw new InvalidOperationException("Authentication is already in progress or has completed on this stream.");
            _authenticating = true;
            _state = state;
            var session = new TlsSession(this, state.IsServer);
            _session = session;
            try
            {
                session.Setup(state);
                await Handshake(session, sync, ct).ConfigureAwait(false);
                if (_remoteCertificate == null)
                {
                    // The verify callback didn't run (no peer certificate, as with a server asking for none, or a resumed
                    // session): validate now, which is where the user's callback sees RemoteCertificateNotAvailable.
                    Exception failure = ValidateRemote(session.PeerCertificate(), new List<byte[]>());
                    if (failure != null) throw failure;
                }
                _protocol = ProtocolOf(session.ProtocolVersion);
                _cipherSuite = (TlsCipherSuite)(ushort)(session.CipherId & 0xFFFF);
                _alpn = session.NegotiatedAlpn();
                _serverName = state.IsServer ? session.ServerName() : null;
                _handshakeCompleted = true;
            }
            catch (Exception e)
            {
                Exception final = e is TlsSession.HandshakeFailure f ? WrapFailure(f, session) : e;
                if (final is not OperationCanceledException) _exception = final;
                session.Free();
                _session = null;
                ExceptionDispatchInfo.Capture(final).Throw();
            }
            finally
            {
                _authenticating = false;
            }
        }

        private static Exception WrapFailure(TlsSession.HandshakeFailure f, TlsSession session)
        {
            Exception validation = session.ValidationException;
            // Our own explicit diagnostics (a revocation mode, say) surface as they are.
            if (validation is NotSupportedException) return validation;
            if (validation != null) return new AuthenticationException("Authentication failed, see inner exception.", validation);
            return new AuthenticationException("Authentication failed, see inner exception.",
                new SslException("SSL Handshake failed with OpenSSL error - " + ErrorName(f.Error) + ".", f.OpenSslText == null ? null : new CryptographicException(f.OpenSslText)));
        }

        private static string ErrorName(int error) => error switch
        {
            1 => "SSL_ERROR_SSL",
            5 => "SSL_ERROR_SYSCALL",
            _ => "SSL_ERROR_" + error,
        };

        private static SslProtocols ProtocolOf(int version) => version switch
        {
            0x0304 => SslProtocols.Tls13,
            0x0303 => SslProtocols.Tls12,
#pragma warning disable SYSLIB0039
            0x0302 => SslProtocols.Tls11,
            0x0301 => SslProtocols.Tls,
#pragma warning restore SYSLIB0039
            _ => SslProtocols.None,
        };

        private async Task Handshake(TlsSession session, bool sync, CancellationToken ct)
        {
            while (true)
            {
                int status;
                byte[] output;
                try { status = session.Handshake(out output); }
                catch (TlsSession.HandshakeFailure f)
                {
                    if (f.Alert != null)
                    {
                        try { await InnerWrite(f.Alert, sync, ct).ConfigureAwait(false); if (sync) InnerStream.Flush(); else await InnerStream.FlushAsync(ct).ConfigureAwait(false); }
                        catch (Exception) { }
                    }
                    throw;
                }
                if (output != null)
                {
                    await InnerWrite(output, sync, ct).ConfigureAwait(false);
                    if (sync) InnerStream.Flush(); else await InnerStream.FlushAsync(ct).ConfigureAwait(false);
                }
                if (status == 1) return;
                await ReceiveHandshakeFrame(session, sync, ct).ConfigureAwait(false);
            }
        }

        private async Task ReceiveHandshakeFrame(TlsSession session, bool sync, CancellationToken ct)
        {
            byte[] header = new byte[5];
            await ReadExactly(header, 0, 5, sync, ct, true).ConfigureAwait(false);
            // TlsFrameHelper.TryGetFrameHeader: a record type, a 3.x version and a plausible length, or this exception.
            int type = header[0], major = header[1], minor = header[2], length = (header[3] << 8) | header[4];
            if (type < 20 || type > 24 || major != 3 || minor > 4 || length > 16384 + 2048 + 256)
                throw new AuthenticationException("Cannot determine the frame size or a corrupted frame was received.");
            byte[] frame = new byte[5 + length];
            Buffer.BlockCopy(header, 0, frame, 0, 5);
            await ReadExactly(frame, 5, length, sync, ct, true).ConfigureAwait(false);
            session.Feed(frame, 0, frame.Length);
        }

        private async Task ReadExactly(byte[] buffer, int offset, int count, bool sync, CancellationToken ct, bool unexpectedEofIsError)
        {
            while (count > 0)
            {
                int n = sync ? InnerStream.Read(buffer, offset, count) : await InnerStream.ReadAsync(new Memory<byte>(buffer, offset, count), ct).ConfigureAwait(false);
                if (n == 0) throw new IOException("Received an unexpected EOF or 0 bytes from the transport stream.");
                offset += n;
                count -= n;
            }
        }

        private async Task InnerWrite(byte[] data, bool sync, CancellationToken ct)
        {
            if (sync) InnerStream.Write(data, 0, data.Length);
            else await InnerStream.WriteAsync(new ReadOnlyMemory<byte>(data), ct).ConfigureAwait(false);
        }

        // ---- certificate validation (SslStream.Protocol.cs, VerifyRemoteCertificateCore) ----

        /// <summary>Validates the peer's certificate; null when accepted, else the exception that rejects the handshake.</summary>
        internal Exception ValidateRemote(byte[] der, List<byte[]> extra)
        {
            AuthState o = _state;
            SslPolicyErrors errors = SslPolicyErrors.None;
            X509ChainStatusFlags chainStatus = X509ChainStatusFlags.NoError;
            X509Chain chain = null;
            X509Certificate2 certificate = der == null ? null : X509Certificate2Impl.Wrap(der, IntPtr.Zero);
            if (certificate == null) errors |= SslPolicyErrors.RemoteCertificateNotAvailable;
            else
            {
                chain = new X509Chain();
                if (o.ChainPolicy != null) chain.ChainPolicy = o.ChainPolicy;
                else
                {
                    chain.ChainPolicy.RevocationMode = o.RevocationMode;
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                }
                if (chain.ChainPolicy.ApplicationPolicy.Count == 0) chain.ChainPolicy.ApplicationPolicy.Add(o.IsServer ? s_clientAuthOid : s_serverAuthOid);
                foreach (byte[] e in extra) chain.ChainPolicy.ExtraStore.Add(X509Certificate2Impl.Wrap(e, IntPtr.Zero));
                if (!chain.Build(certificate))
                {
                    errors |= SslPolicyErrors.RemoteCertificateChainErrors;
                    foreach (X509ChainStatus s in chain.ChainStatus) chainStatus |= s.Status;
                }
                if (o.CheckCertName && !string.IsNullOrEmpty(o.TargetHost) && !HostNameMatches(certificate, o.TargetHost))
                    errors |= SslPolicyErrors.RemoteCertificateNameMismatch;
            }
            _remoteCertificate = certificate;
            _remoteCertificateSet = true;
            bool success;
            if (o.ValidationCallback != null) success = o.ValidationCallback((SslStream)(object)this, certificate, chain, errors);
            else
            {
                bool required = !o.IsServer || o.ClientCertificateRequired;
                if (!required) errors &= ~SslPolicyErrors.RemoteCertificateNotAvailable;
                success = errors == SslPolicyErrors.None;
            }
            if (success) return null;
            if (o.ValidationCallback != null) return new AuthenticationException("The remote certificate was rejected by the provided RemoteCertificateValidationCallback.");
            if (errors == SslPolicyErrors.RemoteCertificateChainErrors && chainStatus != X509ChainStatusFlags.NoError)
                return new AuthenticationException("The remote certificate is invalid because of errors in the certificate chain: " + chainStatus);
            return new AuthenticationException("The remote certificate is invalid according to the validation procedure: " + errors);
        }

        private static unsafe bool HostNameMatches(X509Certificate2 certificate, string host)
        {
            Ossl.EnsureInitialized();
            IntPtr x = ((X509Certificate2Impl)(object)certificate).Handle;
            host = host.TrimEnd('.');
            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                byte[] bytes = ip.GetAddressBytes();
                fixed (byte* b = bytes) return Ossl.X509_check_ip(x, b, (nuint)bytes.Length, 0) == 1;
            }
            byte[] name = Encoding.UTF8.GetBytes(host);
            fixed (byte* n = name) return Ossl.X509_check_host(x, n, (nuint)name.Length, 0, null) == 1;
        }

        // ---- properties ----

        private bool IsValidContext => _session != null && !_disposed;

        public override bool IsAuthenticated => IsValidContext && _exception == null && _handshakeCompleted;

        public override bool IsMutuallyAuthenticated => IsAuthenticated && _state.LocalCertificate != null && _remoteCertificate != null;

        public override bool IsEncrypted => IsAuthenticated;

        public override bool IsSigned => IsAuthenticated;

        public override bool IsServer => _state.IsServer;

        private void ThrowIfExceptional()
        {
            if (_exception != null) ExceptionDispatchInfo.Capture(_exception).Throw();
        }

        private void ThrowIfExceptionalOrNotAuthenticated()
        {
            ThrowIfExceptional();
            if (!IsAuthenticated) throw new InvalidOperationException("This operation is only allowed using a successfully authenticated context.");
        }

        public virtual SslProtocols SslProtocol { get { ThrowIfExceptionalOrNotAuthenticated(); return _protocol; } }

        public SslApplicationProtocol NegotiatedApplicationProtocol
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return _alpn != null ? new SslApplicationProtocol(_alpn) : default;
            }
        }

        public virtual TlsCipherSuite NegotiatedCipherSuite { get { ThrowIfExceptionalOrNotAuthenticated(); return _cipherSuite; } }

        public virtual bool CheckCertRevocationStatus => _state.RevocationMode != X509RevocationMode.NoCheck;

        public virtual X509Certificate LocalCertificate
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return _state.LocalCertificate == null ? null : (X509Certificate)(object)_state.LocalCertificate;
            }
        }

        public virtual X509Certificate RemoteCertificate
        {
            get
            {
                ThrowIfExceptionalOrNotAuthenticated();
                return _remoteCertificate;
            }
        }

        public string TargetHostName => _state.IsServer ? (_serverName ?? "") : _state.TargetHost;

        /// <summary>The channel binding tokens (extended protection for NTLM / Negotiate) aren't produced: asking for one is the diagnostic.</summary>
        private sealed class SslStreamContext : TransportContext
        {
            public override System.Security.Authentication.ExtendedProtection.ChannelBinding GetChannelBinding(System.Security.Authentication.ExtendedProtection.ChannelBindingKind kind)
                => throw Tls.NotSupported("SslStream channel bindings (extended protection)");
        }

        public TransportContext TransportContext => new SslStreamContext();

        public virtual CipherAlgorithmType CipherAlgorithm => throw Tls.NotSupported("SslStream.CipherAlgorithm");
        public virtual int CipherStrength => throw Tls.NotSupported("SslStream.CipherStrength");
        public virtual HashAlgorithmType HashAlgorithm => throw Tls.NotSupported("SslStream.HashAlgorithm");
        public virtual int HashStrength => throw Tls.NotSupported("SslStream.HashStrength");
        public virtual ExchangeAlgorithmType KeyExchangeAlgorithm => throw Tls.NotSupported("SslStream.KeyExchangeAlgorithm");
        public virtual int KeyExchangeStrength => throw Tls.NotSupported("SslStream.KeyExchangeStrength");

        // ---- the stream ----

        public override bool CanSeek => false;
        public override bool CanRead => IsAuthenticated && InnerStream.CanRead;
        public override bool CanWrite => IsAuthenticated && InnerStream.CanWrite && !_shutdown;
        public override bool CanTimeout => InnerStream.CanTimeout;

        public override int ReadTimeout
        {
            get => InnerStream.ReadTimeout;
            set => InnerStream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => InnerStream.WriteTimeout;
            set => InnerStream.WriteTimeout = value;
        }

        public override long Length => InnerStream.Length;

        public override long Position
        {
            get => InnerStream.Position;
            set => throw new NotSupportedException("This stream does not support seek operations.");
        }

        public override void SetLength(long value) => InnerStream.SetLength(value);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("This stream does not support seek operations.");

        public override void Flush() => InnerStream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => InnerStream.FlushAsync(cancellationToken);

        private async ValueTask<int> ReadCore(Memory<byte> buffer, bool sync, CancellationToken ct)
        {
            ThrowIfExceptionalOrNotAuthenticated();
            if (buffer.Length == 0) return 0;
            TlsSession session = _session;
            while (true)
            {
                int n;
                byte[] output;
                try { n = session.Read(buffer.Span, out output); }
                catch (TlsSession.HandshakeFailure f) { throw new IOException("The decryption operation failed, see inner exception.", new CryptographicException(f.OpenSslText)); }
                if (output != null) await InnerWrite(output, sync, ct).ConfigureAwait(false);
                if (n >= 0) return n;
                int got = sync ? InnerStream.Read(_readBuffer, 0, _readBuffer.Length) : await InnerStream.ReadAsync(new Memory<byte>(_readBuffer), ct).ConfigureAwait(false);
                if (got == 0) return 0; // the transport ended: end of stream
                session.Feed(_readBuffer, 0, got);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadCore(new Memory<byte>(buffer, offset, count), true, default).AsTask().GetAwaiter().GetResult();
        }

        public override int Read(Span<byte> buffer)
        {
            byte[] temp = new byte[buffer.Length];
            int n = ReadCore(new Memory<byte>(temp), true, default).AsTask().GetAwaiter().GetResult();
            temp.AsSpan(0, n).CopyTo(buffer);
            return n;
        }

        public override int ReadByte()
        {
            byte[] one = new byte[1];
            return ReadCore(new Memory<byte>(one), true, default).AsTask().GetAwaiter().GetResult() == 0 ? -1 : one[0];
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadCore(new Memory<byte>(buffer, offset, count), false, cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ReadCore(buffer, false, cancellationToken);

        private async ValueTask WriteCore(ReadOnlyMemory<byte> buffer, bool sync, CancellationToken ct)
        {
            ThrowIfExceptionalOrNotAuthenticated();
            if (_shutdown) throw new InvalidOperationException("The write operation is not allowed after the stream was shut down.");
            byte[] output;
            try { output = _session.Write(buffer.Span); }
            catch (TlsSession.HandshakeFailure f) { throw new IOException("The encryption operation failed, see inner exception.", new CryptographicException(f.OpenSslText)); }
            if (output != null) await InnerWrite(output, sync, ct).ConfigureAwait(false);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            WriteCore(new ReadOnlyMemory<byte>(buffer, offset, count), true, default).AsTask().GetAwaiter().GetResult();
        }

        public void Write(byte[] buffer) => Write(buffer, 0, buffer.Length);

        public override void Write(ReadOnlySpan<byte> buffer) => WriteCore(buffer.ToArray(), true, default).AsTask().GetAwaiter().GetResult();

        public override void WriteByte(byte value) => Write(new byte[] { value }, 0, 1);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return WriteCore(new ReadOnlyMemory<byte>(buffer, offset, count), false, cancellationToken).AsTask();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => WriteCore(buffer, false, cancellationToken);

        public virtual async Task ShutdownAsync()
        {
            ThrowIfExceptionalOrNotAuthenticated();
            if (_shutdown) return;
            _shutdown = true;
            byte[] output = _session.Shutdown();
            if (output != null) await InnerWrite(output, false, default).ConfigureAwait(false);
            await InnerStream.FlushAsync().ConfigureAwait(false);
        }

        private static void ValidateBufferArguments(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if ((uint)count > buffer.Length - offset) throw new ArgumentOutOfRangeException("count");
        }

        private void CloseSession(bool notify)
        {
            TlsSession session = _session;
            if (session == null) return;
            if (notify && _handshakeCompleted && _exception == null && !_shutdown)
            {
                _shutdown = true;
                try
                {
                    byte[] output = session.Shutdown();
                    if (output != null) InnerStream.Write(output, 0, output.Length);
                }
                catch (Exception) { }
            }
            session.Free();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing) CloseSession(true);
            }
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                CloseSession(true);
            }
            return base.DisposeAsync();
        }
    }
}
#endif
