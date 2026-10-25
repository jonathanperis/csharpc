// TLS and X.509 certificates are not implemented by csharpc yet. The types Kestrel's HTTPS support
// mentions exist so that HTTP-only programs compile: creating an SslStream, a certificate, a chain or a
// store throws NotSupportedException. SslApplicationProtocol (ALPN names) and SslServerAuthenticationOptions
// (a property bag) are real, after dotnet/runtime's sources (release/11.0, MIT).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    internal static class Tls
    {
        internal static NotSupportedException NotSupported() =>
            new NotSupportedException("TLS and X.509 certificates (System.Net.Security, System.Security.Cryptography.X509Certificates) are not supported by csharpc yet.");
    }

    [Shadows("System.Net.Security.SslApplicationProtocol")]
    public readonly struct SslApplicationProtocolImpl : IEquatable<SslApplicationProtocolImpl>
    {
        private static readonly byte[] s_http3Utf8 = new byte[] { (byte)'h', (byte)'3' };
        private static readonly byte[] s_http2Utf8 = new byte[] { (byte)'h', (byte)'2' };
        private static readonly byte[] s_http11Utf8 = new byte[] { (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)'/', (byte)'1', (byte)'.', (byte)'1' };

        public static readonly SslApplicationProtocolImpl Http3 = new SslApplicationProtocolImpl(s_http3Utf8, copy: false);
        public static readonly SslApplicationProtocolImpl Http2 = new SslApplicationProtocolImpl(s_http2Utf8, copy: false);
        public static readonly SslApplicationProtocolImpl Http11 = new SslApplicationProtocolImpl(s_http11Utf8, copy: false);

        private readonly byte[] _readOnlyProtocol;

        private SslApplicationProtocolImpl(byte[] protocol, bool copy)
        {
            // RFC 7301: 1 to 255 bytes.
            if (protocol.Length == 0 || protocol.Length > 255)
                throw new ArgumentException("The application protocol value is invalid.", "protocol");
            _readOnlyProtocol = copy ? protocol.AsSpan().ToArray() : protocol;
        }

        public SslApplicationProtocolImpl(byte[] protocol) : this(protocol ?? throw new ArgumentNullException("protocol"), copy: true) { }

        public SslApplicationProtocolImpl(string protocol) : this(StrictUtf8(protocol ?? throw new ArgumentNullException("protocol")), copy: false) { }

        // .NET encodes with exception fallbacks: a lone surrogate throws.
        private static byte[] StrictUtf8(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { i++; continue; }
                if (char.IsSurrogate(s[i]))
                    throw new EncoderFallbackException("Unable to translate Unicode character \\u" + ((int)s[i]).ToString("X4") + " at index " + i + " to specified code page.");
            }
            return Encoding.UTF8.GetBytes(s);
        }

        public ReadOnlyMemory<byte> Protocol => _readOnlyProtocol;

        public bool Equals(SslApplicationProtocolImpl other) => ((ReadOnlySpan<byte>)_readOnlyProtocol).SequenceEqual(other._readOnlyProtocol);
        public override bool Equals(object obj) => obj is SslApplicationProtocolImpl protocol && Equals(protocol);

        public override int GetHashCode()
        {
            byte[] arr = _readOnlyProtocol;
            if (arr == null) return 0;
            int hash = 0;
            for (int i = 0; i < arr.Length; i++) hash = ((hash << 5) + hash) ^ arr[i];
            return hash;
        }

        public override string ToString()
        {
            byte[] arr = _readOnlyProtocol;
            if (arr == null) return string.Empty;
            if (ReferenceEquals(arr, s_http3Utf8)) return "h3";
            if (ReferenceEquals(arr, s_http2Utf8)) return "h2";
            if (ReferenceEquals(arr, s_http11Utf8)) return "http/1.1";
            string text = Encoding.UTF8.GetString(arr);
            if (((ReadOnlySpan<byte>)Encoding.UTF8.GetBytes(text)).SequenceEqual(arr)) return text;
            // Not valid UTF-8 (.NET's decoder throws): the bytes as "0xhh" values.
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append("0x").Append(arr[i].ToString("x2"));
            }
            return sb.ToString();
        }

        public static bool operator ==(SslApplicationProtocolImpl left, SslApplicationProtocolImpl right) => left.Equals(right);
        public static bool operator !=(SslApplicationProtocolImpl left, SslApplicationProtocolImpl right) => !left.Equals(right);
    }

    [Shadows("System.Net.Security.SslServerAuthenticationOptions")]
    public class SslServerAuthenticationOptionsImpl
    {
        private X509RevocationMode _checkCertificateRevocation = X509RevocationMode.NoCheck;
        private SslProtocols _enabledSslProtocols = SslProtocols.None;
        private EncryptionPolicy _encryptionPolicy = EncryptionPolicy.RequireEncryption;

        public bool AllowRenegotiation { get; set; }
        public bool AllowTlsResume { get; set; } = true;
        public bool AllowRsaPssPadding { get; set; } = true;
        public bool AllowRsaPkcs1Padding { get; set; } = true;
        public bool ClientCertificateRequired { get; set; }
        public List<SslApplicationProtocol> ApplicationProtocols { get; set; }
        public RemoteCertificateValidationCallback RemoteCertificateValidationCallback { get; set; }
        public ServerCertificateSelectionCallback ServerCertificateSelectionCallback { get; set; }
        public X509Certificate ServerCertificate { get; set; }
        public SslStreamCertificateContext ServerCertificateContext { get; set; }
        public CipherSuitesPolicy CipherSuitesPolicy { get; set; }
        public X509ChainPolicy CertificateChainPolicy { get; set; }

        public SslProtocols EnabledSslProtocols
        {
            get => _enabledSslProtocols;
            set => _enabledSslProtocols = value;
        }

        public X509RevocationMode CertificateRevocationCheckMode
        {
            get => _checkCertificateRevocation;
            set
            {
                if (value != X509RevocationMode.NoCheck && value != X509RevocationMode.Offline && value != X509RevocationMode.Online)
                    throw new ArgumentException("The specified value is not valid in the 'X509RevocationMode' enumeration.", "value");
                _checkCertificateRevocation = value;
            }
        }

        public EncryptionPolicy EncryptionPolicy
        {
            get => _encryptionPolicy;
            set
            {
                if (value != EncryptionPolicy.RequireEncryption && value != EncryptionPolicy.AllowNoEncryption && value != EncryptionPolicy.NoEncryption)
                    throw new ArgumentException("The specified value is not valid in the 'EncryptionPolicy' enumeration.", "value");
                _encryptionPolicy = value;
            }
        }
    }

    [Shadows("System.Net.Security.SslStream")]
    public class SslStreamImpl : Stream
    {
        public SslStreamImpl(Stream innerStream) => throw Tls.NotSupported();
        public SslStreamImpl(Stream innerStream, bool leaveInnerStreamOpen) => throw Tls.NotSupported();
        public SslStreamImpl(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback userCertificateValidationCallback)
            => throw Tls.NotSupported();
        public SslStreamImpl(Stream innerStream, bool leaveInnerStreamOpen, RemoteCertificateValidationCallback userCertificateValidationCallback,
            LocalCertificateSelectionCallback userCertificateSelectionCallback) => throw Tls.NotSupported();

        public Task AuthenticateAsServerAsync(SslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken = default) => throw Tls.NotSupported();
        public Task AuthenticateAsServerAsync(ServerOptionsSelectionCallback optionsCallback, object state, CancellationToken cancellationToken = default) => throw Tls.NotSupported();
        public Task NegotiateClientCertificateAsync(CancellationToken cancellationToken = default) => throw Tls.NotSupported();
        public SslProtocols SslProtocol => throw Tls.NotSupported();
        public SslApplicationProtocol NegotiatedApplicationProtocol => throw Tls.NotSupported();
        public TlsCipherSuite NegotiatedCipherSuite => throw Tls.NotSupported();
        public X509Certificate RemoteCertificate => throw Tls.NotSupported();
        public CipherAlgorithmType CipherAlgorithm => throw Tls.NotSupported();
        public int CipherStrength => throw Tls.NotSupported();
        public HashAlgorithmType HashAlgorithm => throw Tls.NotSupported();
        public int HashStrength => throw Tls.NotSupported();
        public ExchangeAlgorithmType KeyExchangeAlgorithm => throw Tls.NotSupported();
        public int KeyExchangeStrength => throw Tls.NotSupported();
        public System.Net.TransportContext TransportContext => throw Tls.NotSupported();

        public override bool CanRead => false;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw Tls.NotSupported();
        public override int Read(byte[] buffer, int offset, int count) => throw Tls.NotSupported();
        public override void Write(byte[] buffer, int offset, int count) => throw Tls.NotSupported();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Certificate")]
    public class X509CertificateImpl : IDisposable
    {
        public X509CertificateImpl() => throw Tls.NotSupported();
        public X509CertificateImpl(byte[] data) => throw Tls.NotSupported();
        public X509CertificateImpl(string fileName) => throw Tls.NotSupported();

        public string Subject => throw Tls.NotSupported();
        public string Issuer => throw Tls.NotSupported();
        public virtual byte[] Export(X509ContentType contentType) => throw Tls.NotSupported();
        public virtual byte[] Export(X509ContentType contentType, string password) => throw Tls.NotSupported();
        public virtual byte[] GetCertHash() => throw Tls.NotSupported();
        public virtual string GetCertHashString() => throw Tls.NotSupported();
        public virtual bool Equals(X509CertificateImpl other) => ReferenceEquals(this, other);
        public void Dispose() { }
        protected virtual void Dispose(bool disposing) { }
    }

    [Shadows("System.Security.Cryptography.Oid")]
    public sealed class OidImpl
    {
        public OidImpl() { }
        public OidImpl(string oid) { Value = oid; }
        public OidImpl(string value, string friendlyName) { Value = value; FriendlyName = friendlyName; }
        public OidImpl(OidImpl oid)
        {
            ArgumentNullException.ThrowIfNull(oid);
            Value = oid.Value;
            FriendlyName = oid.FriendlyName;
        }
        public string Value { get; set; }
        public string FriendlyName { get; set; }
    }

    [Shadows("System.Security.Cryptography.OidCollection")]
    public sealed class OidCollectionImpl : System.Collections.ICollection
    {
        private readonly List<Oid> _list = new List<Oid>();
        public int Add(Oid oid) { _list.Add(oid); return _list.Count - 1; }
        public Oid this[int index] => _list[index];
        public Oid this[string oid]
        {
            get
            {
                foreach (var o in _list) if (o.Value == oid || o.FriendlyName == oid) return o;
                return null;
            }
        }
        public int Count => _list.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public OidEnumerator GetEnumerator() => (OidEnumerator)(object)new OidEnumeratorImpl(this);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => (System.Collections.IEnumerator)(object)new OidEnumeratorImpl(this);
        public void CopyTo(Oid[] array, int index) => _list.CopyTo(array, index);
        void System.Collections.ICollection.CopyTo(Array array, int index) { for (int i = 0; i < _list.Count; i++) array.SetValue(_list[i], index + i); }
    }

    [Shadows("System.Security.Cryptography.OidEnumerator")]
    public sealed class OidEnumeratorImpl : System.Collections.IEnumerator
    {
        private readonly OidCollectionImpl _oids;
        private int _current = -1;
        internal OidEnumeratorImpl(OidCollectionImpl oids) { _oids = oids; }
        public Oid Current => _oids[_current];
        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext()
        {
            if (_current >= _oids.Count - 1) return false;
            _current++;
            return true;
        }
        public void Reset() => _current = -1;
    }

    [Shadows("System.Security.Cryptography.AsnEncodedData")]
    public class AsnEncodedDataImpl
    {
        protected AsnEncodedDataImpl() => throw Tls.NotSupported();
        public AsnEncodedDataImpl(byte[] rawData) => throw Tls.NotSupported();
        public Oid Oid { get => throw Tls.NotSupported(); set => throw Tls.NotSupported(); }
        public byte[] RawData { get => throw Tls.NotSupported(); set => throw Tls.NotSupported(); }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Extension")]
    public class X509ExtensionImpl : AsnEncodedDataImpl
    {
        protected X509ExtensionImpl() : base() { }
        public X509ExtensionImpl(string oid, byte[] rawData, bool critical) : base() { }
        public X509ExtensionImpl(Oid oid, byte[] rawData, bool critical) : base() { }
        public bool Critical { get => throw Tls.NotSupported(); set => throw Tls.NotSupported(); }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension")]
    public sealed class X509EnhancedKeyUsageExtensionImpl : X509ExtensionImpl
    {
        public X509EnhancedKeyUsageExtensionImpl() : base() { }
        public X509EnhancedKeyUsageExtensionImpl(OidCollection enhancedKeyUsages, bool critical) : base() { }
        public OidCollection EnhancedKeyUsages => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509SubjectAlternativeNameExtension")]
    public sealed class X509SubjectAlternativeNameExtensionImpl : X509ExtensionImpl
    {
        public X509SubjectAlternativeNameExtensionImpl() : base() { }
        public IEnumerable<string> EnumerateDnsNames() => throw Tls.NotSupported();
        public IEnumerable<System.Net.IPAddress> EnumerateIPAddresses() => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainPolicy")]
    public sealed class X509ChainPolicyImpl
    {
        public X509ChainPolicyImpl() => throw Tls.NotSupported();
        public X509RevocationMode RevocationMode { get => throw Tls.NotSupported(); set => throw Tls.NotSupported(); }
        public X509RevocationFlag RevocationFlag { get => throw Tls.NotSupported(); set => throw Tls.NotSupported(); }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainElementCollection")]
    public sealed class X509ChainElementCollectionImpl
    {
        private X509ChainElementCollectionImpl() { }
        public int Count => throw Tls.NotSupported();
        public X509ChainElement this[int index] => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainElement")]
    public class X509ChainElementImpl
    {
        private X509ChainElementImpl() { }
        public X509Certificate2 Certificate => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.RSACryptoServiceProvider")]
    public sealed class RSACryptoServiceProviderImpl
    {
        public RSACryptoServiceProviderImpl() => throw Tls.NotSupported();
        public CspKeyContainerInfo CspKeyContainerInfo => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.CngKey")]
    public sealed class CngKeyImpl : IDisposable
    {
        private CngKeyImpl() => throw Tls.NotSupported();
        public CngExportPolicies ExportPolicy => throw Tls.NotSupported();
        public void Dispose() { }
    }

    /// <summary>PEM text (RFC 7468) after .NET's PemEncoding.Write: base64 in 64-character lines between BEGIN/END labels.</summary>
    [ShadowMembers("System.Security.Cryptography.PemEncoding")]
    internal static class PemEncodingMembers
    {
        private static bool IsLabelChar(char c) => (uint)(c - 0x21) <= 0x5d && c != '-';

        private static bool IsValidLabel(ReadOnlySpan<char> label)
        {
            if (label.IsEmpty) return true;
            if (!IsLabelChar(label[0])) return false;
            bool previousSpaceOrHyphen = false;
            for (int i = 1; i < label.Length; i++)
            {
                char c = label[i];
                if (IsLabelChar(c)) { previousSpaceOrHyphen = false; continue; }
                bool spaceOrHyphen = c == ' ' || c == '-';
                if (!spaceOrHyphen || previousSpaceOrHyphen) return false;
                previousSpaceOrHyphen = true;
            }
            return !previousSpaceOrHyphen;
        }

        public static string WriteString(ReadOnlySpan<char> label, ReadOnlySpan<byte> data) => new string(Write(label, data));

        public static char[] Write(ReadOnlySpan<char> label, ReadOnlySpan<byte> data)
        {
            if (!IsValidLabel(label)) throw new ArgumentException("The specified label is not valid.", "label");
            var sb = new StringBuilder();
            sb.Append("-----BEGIN ").Append(label).Append("-----\n");
            const int BytesPerLine = 48;
            while (data.Length > 0)
            {
                int n = Math.Min(BytesPerLine, data.Length);
                sb.Append(Convert.ToBase64String(data.Slice(0, n))).Append('\n');
                data = data.Slice(n);
            }
            sb.Append("-----END ").Append(label).Append("-----");
            return sb.ToString().ToCharArray();
        }
    }

    [Shadows("System.Security.Cryptography.RSACng")]
    public sealed class RSACngImpl
    {
        public RSACngImpl() => throw Tls.NotSupported();
        public CngKey Key => throw Tls.NotSupported();
    }

    [ShadowMembers("System.Security.Cryptography.AsymmetricAlgorithm")]
    internal static class AsymmetricAlgorithmMembers
    {
        // No AsymmetricAlgorithm can exist (every factory throws): disposing one has nothing to do.
        [ShadowInstance]
        public static void Dispose(AsymmetricAlgorithm algorithm) { }
        [ShadowInstance]
        public static byte[] ExportEncryptedPkcs8PrivateKey(AsymmetricAlgorithm algorithm, ReadOnlySpan<char> password, PbeParameters pbeParameters) => throw Tls.NotSupported();
        [ShadowInstance]
        public static byte[] ExportEncryptedPkcs8PrivateKey(AsymmetricAlgorithm algorithm, ReadOnlySpan<byte> passwordBytes, PbeParameters pbeParameters) => throw Tls.NotSupported();
        [ShadowInstance]
        public static byte[] ExportPkcs8PrivateKey(AsymmetricAlgorithm algorithm) => throw Tls.NotSupported();
        [ShadowInstance]
        public static void ImportFromPem(AsymmetricAlgorithm algorithm, ReadOnlySpan<char> input) => throw Tls.NotSupported();
        [ShadowInstance]
        public static void ImportFromEncryptedPem(AsymmetricAlgorithm algorithm, ReadOnlySpan<char> input, ReadOnlySpan<char> password) => throw Tls.NotSupported();
    }

    [ShadowMembers("System.Security.Cryptography.RSA")]
    internal static class RSAFactories
    {
        public static RSA Create() => throw Tls.NotSupported();
        public static RSA Create(int keySizeInBits) => throw Tls.NotSupported();
        public static RSA Create(RSAParameters parameters) => throw Tls.NotSupported();
    }

    [ShadowMembers("System.Security.Cryptography.ECDsa")]
    internal static class ECDsaFactories
    {
        public static ECDsa Create() => throw Tls.NotSupported();
        public static ECDsa Create(ECCurve curve) => throw Tls.NotSupported();
    }

    [ShadowMembers("System.Security.Cryptography.DSA")]
    internal static class DSAFactories
    {
        public static DSA Create() => throw Tls.NotSupported();
    }

    [ShadowMembers("System.Security.Cryptography.X509Certificates.X509CertificateLoader")]
    internal static class X509CertificateLoaderMembers
    {
        public static X509Certificate2 LoadCertificate(byte[] data) => throw Tls.NotSupported();
        public static X509Certificate2 LoadCertificate(ReadOnlySpan<byte> data) => throw Tls.NotSupported();
        public static X509Certificate2 LoadCertificateFromFile(string path) => throw Tls.NotSupported();
        public static X509Certificate2 LoadPkcs12(byte[] data, string password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet,
            Pkcs12LoaderLimits loaderLimits = null) => throw Tls.NotSupported();
        public static X509Certificate2 LoadPkcs12FromFile(string path, string password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet,
            Pkcs12LoaderLimits loaderLimits = null) => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.CspKeyContainerInfo")]
    public sealed class CspKeyContainerInfoImpl
    {
        private CspKeyContainerInfoImpl() { }
        public bool Exportable => throw Tls.NotSupported();
    }

    /// <summary>Password-based encryption parameters (a plain value holder, as in .NET).</summary>
    [Shadows("System.Security.Cryptography.PbeParameters")]
    public sealed class PbeParametersImpl
    {
        public PbeParametersImpl(PbeEncryptionAlgorithm encryptionAlgorithm, HashAlgorithmName hashAlgorithm, int iterationCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(iterationCount);
            EncryptionAlgorithm = encryptionAlgorithm;
            HashAlgorithm = hashAlgorithm;
            IterationCount = iterationCount;
        }
        public PbeEncryptionAlgorithm EncryptionAlgorithm { get; }
        public HashAlgorithmName HashAlgorithm { get; }
        public int IterationCount { get; }
    }

    [Shadows("System.Net.Security.SslStreamCertificateContext")]
    public sealed class SslStreamCertificateContextImpl
    {
        private SslStreamCertificateContextImpl() { }
        public static SslStreamCertificateContextImpl Create(X509Certificate2 target, X509Certificate2Collection additionalCertificates, bool offline = false,
            SslCertificateTrust trust = null) => throw Tls.NotSupported();
        public X509Certificate2 TargetCertificate => throw Tls.NotSupported();
    }

    [ShadowMembers("System.Security.Cryptography.X509Certificates.RSACertificateExtensions")]
    internal static class RSACertificateExtensionsMembers
    {
        public static RSA GetRSAPrivateKey(X509Certificate2 certificate) => throw Tls.NotSupported();
        public static RSA GetRSAPublicKey(X509Certificate2 certificate) => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Certificate2")]
    public class X509Certificate2Impl : X509CertificateImpl
    {
        public X509Certificate2Impl(X509Certificate certificate) : base() { }
        public X509Certificate2Impl(byte[] rawData) : base() { }
        public X509Certificate2Impl(ReadOnlySpan<byte> rawData) : base() { }
        public X509Certificate2Impl(string fileName) : base() { }
        public X509Certificate2Impl(string fileName, string password) : base() { }
        public X509Certificate2Impl(string fileName, string password, X509KeyStorageFlags keyStorageFlags) : base() { }
        public X509Certificate2Impl(byte[] rawData, string password, X509KeyStorageFlags keyStorageFlags) : base() { }

        public string Thumbprint => throw Tls.NotSupported();
        public X509ExtensionCollection Extensions => throw Tls.NotSupported();
        public bool HasPrivateKey => throw Tls.NotSupported();
        public ReadOnlyMemory<byte> RawDataMemory => throw Tls.NotSupported();
        public byte[] RawData => throw Tls.NotSupported();
        public string FriendlyName { get => throw Tls.NotSupported(); set => throw Tls.NotSupported(); }
        public DateTime NotAfter => throw Tls.NotSupported();
        public DateTime NotBefore => throw Tls.NotSupported();
        public string SerialNumber => throw Tls.NotSupported();
        public PublicKey PublicKey => throw Tls.NotSupported();
        public string GetNameInfo(X509NameType nameType, bool forIssuer) => throw Tls.NotSupported();
        public static X509ContentType GetCertContentType(byte[] rawData) => throw Tls.NotSupported();
        public static X509ContentType GetCertContentType(string fileName) => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Certificate2Collection")]
    public class X509Certificate2CollectionImpl : IEnumerable<X509Certificate2>
    {
        public X509Certificate2CollectionImpl() => throw Tls.NotSupported();
        public X509Certificate2CollectionImpl(X509Certificate2 certificate) => throw Tls.NotSupported();
        public X509Certificate2 this[int index] => throw Tls.NotSupported();
        public int Count => throw Tls.NotSupported();
        public X509Certificate2CollectionImpl Find(X509FindType findType, object findValue, bool validOnly) => throw Tls.NotSupported();
        public void ImportFromPemFile(string certPemFilePath) => throw Tls.NotSupported();
        public IEnumerator<X509Certificate2> GetEnumerator() => throw Tls.NotSupported();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw Tls.NotSupported();
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Chain")]
    public class X509ChainImpl : IDisposable
    {
        public X509ChainImpl() => throw Tls.NotSupported();
        public bool Build(X509Certificate2 certificate) => throw Tls.NotSupported();
        public X509ChainPolicy ChainPolicy => throw Tls.NotSupported();
        public X509ChainElementCollection ChainElements => throw Tls.NotSupported();
        public void Dispose() { }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Store")]
    public sealed class X509StoreImpl : IDisposable
    {
        public X509StoreImpl() => throw Tls.NotSupported();
        public X509StoreImpl(StoreName storeName, StoreLocation storeLocation) => throw Tls.NotSupported();
        public X509StoreImpl(string storeName, StoreLocation storeLocation) => throw Tls.NotSupported();
        public X509StoreImpl(StoreName storeName, StoreLocation storeLocation, OpenFlags flags) => throw Tls.NotSupported();
        public X509Certificate2Collection Certificates => throw Tls.NotSupported();
        public StoreLocation Location => throw Tls.NotSupported();
        public string Name => throw Tls.NotSupported();
        public void Open(OpenFlags flags) => throw Tls.NotSupported();
        public void Add(X509Certificate2 certificate) => throw Tls.NotSupported();
        public void Remove(X509Certificate2 certificate) => throw Tls.NotSupported();
        public void Close() { }
        public void Dispose() { }
    }
}
