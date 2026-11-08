// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/
// System/Security/Cryptography/X509Certificates/{X509Certificate,X509Certificate2,X509CertificateLoader,
// X509Certificate2Collection,X509CertificateCollection}.cs and OpenSslX509CertificateReader.cs; see
// THIRD-PARTY-NOTICES.md.
// X.509 certificates as .NET has them on Linux: the DER is read here (subject, issuer, serial number, validity, key
// algorithms: strings and hashes are .NET's), the private key of a PFX / PEM stays an OpenSSL EVP_PKEY, which TLS
// (SslStream) and GetRSAPrivateKey use. Loading takes DER, PEM, PKCS#12 (X509CertificateLoader and the obsolete
// constructors). Extensions, stores, PKCS#7, exporting a PFX and the non-RSA key getters are explicit
// NotSupportedExceptions (docs/adr/0022-tls.md).
#nullable disable
#if CSHARPC_OPENSSL
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CsharpcCoreLib
{
    /// <summary>The fields of a certificate's DER that the API reports.</summary>
    internal sealed class X509Parsed
    {
        internal int Version;
        internal byte[] Serial;          // big-endian, without leading zeros
        internal string SignatureOid;
        internal byte[] Issuer, Subject; // DER of the Name
        internal DateTime NotBefore, NotAfter; // UTC
        internal string KeyOid;
        internal byte[] KeyParameters;   // DER of the AlgorithmIdentifier's parameters, or null
        internal byte[] KeyValue;        // the SubjectPublicKey bits
        internal byte[] SubjectAltNames; // the extension's OCTET STRING content (GeneralNames), or null

        internal static X509Parsed Parse(byte[] der)
        {
            var c = new X509Parsed();
            Der.Read(der, 0, der.Length, out int tag, out int start, out _, out int end);
            if (tag != 0x30 || end != der.Length) throw Der.Corrupt();
            Der.Read(der, start, end, out int ttag, out int tstart, out _, out int tend);
            if (ttag != 0x30) throw Der.Corrupt();
            int pos = tstart;
            c.Version = 1;
            Der.Read(der, pos, tend, out int t, out int s, out int l, out int n);
            if (t == 0xA0)
            {
                Der.Read(der, s, n, out int vt, out int vs, out int vl, out _);
                if (vt != 0x02 || vl != 1) throw Der.Corrupt();
                c.Version = der[vs] + 1;
                pos = n;
                Der.Read(der, pos, tend, out t, out s, out l, out n);
            }
            if (t != 0x02) throw Der.Corrupt();
            int zeros = 0;
            while (zeros < l - 1 && der[s + zeros] == 0) zeros++;
            c.Serial = Der.Slice(der, s + zeros, l - zeros);
            pos = n;
            Der.Read(der, pos, tend, out t, out s, out l, out n); // signature algorithm
            if (t != 0x30) throw Der.Corrupt();
            Der.Read(der, s, n, out int ot, out int os, out int ol, out _);
            if (ot != 0x06) throw Der.Corrupt();
            c.SignatureOid = Der.ReadOid(der, os, ol);
            pos = n;
            c.Issuer = Der.Element(der, pos, tend, out pos);
            Der.Read(der, pos, tend, out t, out s, out l, out n); // validity
            if (t != 0x30) throw Der.Corrupt();
            Der.Read(der, s, n, out int bt, out int bs, out int bl, out int bn);
            Der.Read(der, bn, n, out int at, out int astart, out int al, out _);
            c.NotBefore = Der.ReadTime(der, bt, bs, bl);
            c.NotAfter = Der.ReadTime(der, at, astart, al);
            pos = n;
            c.Subject = Der.Element(der, pos, tend, out pos);
            Der.Read(der, pos, tend, out t, out s, out l, out n); // SubjectPublicKeyInfo
            if (t != 0x30) throw Der.Corrupt();
            Der.Read(der, s, n, out int algTag, out int algStart, out int algLen, out int algNext);
            if (algTag != 0x30) throw Der.Corrupt();
            Der.Read(der, algStart, algNext, out ot, out os, out ol, out int oidNext);
            if (ot != 0x06) throw Der.Corrupt();
            c.KeyOid = Der.ReadOid(der, os, ol);
            if (oidNext < algNext) c.KeyParameters = Der.Element(der, oidNext, algNext, out _);
            Der.Read(der, algNext, n, out int kt, out int ks, out int kl, out _);
            if (kt != 0x03 || kl < 1) throw Der.Corrupt();
            c.KeyValue = Der.Slice(der, ks + 1, kl - 1);
            pos = n;
            // Extensions: [3] EXPLICIT SEQUENCE OF Extension; only the subject alternative names are read.
            while (pos < tend)
            {
                Der.Read(der, pos, tend, out t, out s, out l, out n);
                if (t == 0xA3)
                {
                    Der.Read(der, s, n, out int et, out int es, out int el, out int en);
                    if (et != 0x30) throw Der.Corrupt();
                    for (int e = es; e < en;)
                    {
                        Der.Read(der, e, en, out int xt, out int xs, out int xl, out int xn);
                        Der.Read(der, xs, xn, out int it, out int istart, out int il, out int inext);
                        if (it == 0x06 && Der.ReadOid(der, istart, il) == "2.5.29.17")
                        {
                            Der.Read(der, inext, xn, out int ct, out int cs, out int cl, out int cn);
                            if (ct == 0x01) Der.Read(der, cn, xn, out ct, out cs, out cl, out cn); // critical
                            if (ct == 0x04) c.SubjectAltNames = Der.Slice(der, cs, cl);
                        }
                        e = xn;
                    }
                }
                pos = n;
            }
            return c;
        }

        /// <summary>The dNSName entries of the subject alternative names, in order.</summary>
        internal List<string> DnsNames()
        {
            var names = new List<string>();
            if (SubjectAltNames == null) return names;
            Der.Read(SubjectAltNames, 0, SubjectAltNames.Length, out int tag, out int start, out _, out int end);
            for (int pos = start; pos < end;)
            {
                Der.Read(SubjectAltNames, pos, end, out int t, out int s, out int l, out int n);
                if (t == 0x82) names.Add(Encoding.ASCII.GetString(SubjectAltNames, s, l));
                pos = n;
            }
            return names;
        }

        /// <summary>The iPAddress entries of the subject alternative names, in order.</summary>
        internal List<byte[]> IPAddresses()
        {
            var list = new List<byte[]>();
            if (SubjectAltNames == null) return list;
            Der.Read(SubjectAltNames, 0, SubjectAltNames.Length, out int tag, out int start, out _, out int end);
            for (int pos = start; pos < end;)
            {
                Der.Read(SubjectAltNames, pos, end, out int t, out int s, out int l, out int n);
                if (t == 0x87) list.Add(Der.Slice(SubjectAltNames, s, l));
                pos = n;
            }
            return list;
        }
    }

    internal static unsafe class X509Reader
    {
        /// <summary>The certificate DER, the EVP_PKEY of its private key (or zero) and the rest of a PKCS#12's certificates (DER).</summary>
        internal static byte[] Load(byte[] data, string password, bool passwordProvided, out IntPtr key, out List<byte[]> others)
        {
            key = IntPtr.Zero;
            others = null;
            Ossl.EnsureInitialized();
            // DER certificate
            if (data.Length > 0 && data[0] == 0x30)
            {
                IntPtr x = Ossl.FromDer(data);
                if (x != IntPtr.Zero)
                {
                    byte[] der = Ossl.ToDer(x);
                    Ossl.X509_free(x);
                    return der;
                }
                // PKCS#12
                IntPtr p12;
                fixed (byte* p = data)
                {
                    byte* cursor = p;
                    p12 = Ossl.d2i_PKCS12(null, &cursor, data.Length);
                }
                if (p12 != IntPtr.Zero)
                {
                    try { return FromPkcs12(p12, password, out key, out others); }
                    finally { Ossl.PKCS12_free(p12); }
                }
                Ossl.ERR_clear_error();
                throw Unrecognized();
            }
            // PEM: the first CERTIFICATE (and a PRIVATE KEY if one follows)
            byte[] pem = FromPem(data, null, out key);
            if (pem != null) return pem;
            throw Unrecognized();
        }

        // Data that is no certificate, PEM or PKCS#12: .NET's message depends on its backend (Apple's import API on macOS).
        private static CryptographicException Unrecognized()
        {
#if CSHARPC_TARGET_MACOS
            return new CryptographicException("Unknown format in import.");
#elif CSHARPC_TARGET_WINDOWS
            return new CryptographicException("Cannot find the requested object."); // CRYPT_E_NOT_FOUND from CryptoAPI
#else
            return Der.Corrupt();
#endif
        }

        /// <summary>
        /// The DER of every certificate bag of a PFX, in the order the file holds them (what .NET's collection loaders return),
        /// or null if the structure can't be walked (the caller falls back to PKCS12_parse's order). The password has been
        /// accepted by <see cref="Load"/> already.
        /// </summary>
        internal static List<byte[]> Pkcs12CertificatesInFileOrder(byte[] data, string password)
        {
            Ossl.EnsureInitialized();
            IntPtr p12;
            fixed (byte* p = data)
            {
                byte* cursor = p;
                p12 = Ossl.d2i_PKCS12(null, &cursor, data.Length);
            }
            if (p12 == IntPtr.Zero) { Ossl.ERR_clear_error(); return null; }
            var result = new List<byte[]>();
            bool walked = false;
            byte[] pass = Ossl.Utf8Z(password);
            IntPtr safes = Ossl.PKCS12_unpack_authsafes(p12);
            try
            {
                if (safes == IntPtr.Zero) return null;
                walked = true;
                for (int i = 0, n = Ossl.OPENSSL_sk_num(safes); i < n; i++)
                {
                    IntPtr p7 = Ossl.OPENSSL_sk_value(safes, i);
                    IntPtr bags;
                    fixed (byte* pw = pass) bags = Ossl.PKCS12_unpack_p7encdata(p7, pw, -1);
                    if (bags == IntPtr.Zero && string.IsNullOrEmpty(password)) bags = Ossl.PKCS12_unpack_p7encdata(p7, null, 0);
                    if (bags == IntPtr.Zero) bags = Ossl.PKCS12_unpack_p7data(p7);
                    Ossl.ERR_clear_error(); // the attempt that didn't fit the safe's type
                    if (bags == IntPtr.Zero) { walked = false; continue; }
                    for (int j = 0, m = Ossl.OPENSSL_sk_num(bags); j < m; j++)
                    {
                        IntPtr bag = Ossl.OPENSSL_sk_value(bags, j);
                        if (Ossl.PKCS12_SAFEBAG_get_nid(bag) != 152 /* NID_certBag */) continue;
                        IntPtr x = Ossl.PKCS12_SAFEBAG_get1_cert(bag);
                        if (x == IntPtr.Zero) { Ossl.ERR_clear_error(); walked = false; continue; }
                        result.Add(Ossl.ToDer(x));
                        Ossl.X509_free(x);
                    }
                    for (int j = 0, m = Ossl.OPENSSL_sk_num(bags); j < m; j++) Ossl.PKCS12_SAFEBAG_free(Ossl.OPENSSL_sk_value(bags, j));
                    Ossl.OPENSSL_sk_free(bags);
                }
            }
            finally
            {
                if (safes != IntPtr.Zero)
                {
                    for (int i = 0, n = Ossl.OPENSSL_sk_num(safes); i < n; i++) Ossl.PKCS7_free(Ossl.OPENSSL_sk_value(safes, i));
                    Ossl.OPENSSL_sk_free(safes);
                }
                Ossl.PKCS12_free(p12);
            }
            return walked ? result : null;
        }

        private static byte[] FromPkcs12(IntPtr p12, string password, out IntPtr key, out List<byte[]> others)
        {
            // .NET tries the password as given and, for an absent or empty one, both the null and the empty password.
            IntPtr pkey = IntPtr.Zero, cert = IntPtr.Zero, ca = IntPtr.Zero;
            int ok;
            byte[] pass = Ossl.Utf8Z(password);
            fixed (byte* pw = pass) ok = Ossl.PKCS12_parse(p12, pw, &pkey, &cert, &ca);
            if (ok != 1 && string.IsNullOrEmpty(password))
            {
                Ossl.ERR_clear_error();
                pkey = cert = ca = IntPtr.Zero;
                ok = Ossl.PKCS12_parse(p12, null, &pkey, &cert, &ca);
            }
            if (ok != 1)
            {
                Ossl.ERR_clear_error();
                throw new CryptographicException("The certificate data cannot be read with the provided password, the password may be incorrect.");
            }
            try
            {
                if (cert == IntPtr.Zero) throw new CryptographicException("Cannot find the requested object.");
                byte[] der = Ossl.ToDer(cert);
                others = null;
                if (ca != IntPtr.Zero)
                {
                    others = new List<byte[]>();
                    int n = Ossl.OPENSSL_sk_num(ca);
                    for (int i = 0; i < n; i++) others.Add(Ossl.ToDer(Ossl.OPENSSL_sk_value(ca, i)));
                }
                key = pkey;
                pkey = IntPtr.Zero;
                return der;
            }
            finally
            {
                Ossl.X509_free(cert);
                if (pkey != IntPtr.Zero) Ossl.EVP_PKEY_free(pkey);
                if (ca != IntPtr.Zero)
                {
                    int n = Ossl.OPENSSL_sk_num(ca);
                    for (int i = 0; i < n; i++) Ossl.X509_free(Ossl.OPENSSL_sk_value(ca, i));
                    Ossl.OPENSSL_sk_free(ca);
                }
            }
        }

        /// <summary>
        /// The first certificate of PEM text (DER) and, when <paramref name="keyPem"/> is given, the private key it holds (required: an
        /// unreadable one throws). With <paramref name="password"/> (NUL-terminated UTF-8) the key must be a PKCS#8 "ENCRYPTED PRIVATE KEY"
        /// and is decrypted with it, as CreateFromEncryptedPem does; without, an encrypted key is unreadable (OpenSSL is given an empty
        /// password so that it never prompts on the terminal).
        /// </summary>
        internal static byte[] FromPem(byte[] pemText, byte[] keyPem, out IntPtr key, byte[] password = null)
        {
            key = IntPtr.Zero;
            IntPtr bio;
            fixed (byte* p = pemText) bio = Ossl.BIO_new_mem_buf(p, pemText.Length);
            if (bio == IntPtr.Zero) throw Ossl.Error();
            byte[] der = null;
            try
            {
                IntPtr x = Ossl.PEM_read_bio_X509(bio, null, IntPtr.Zero, IntPtr.Zero);
                if (x == IntPtr.Zero) { Ossl.ERR_clear_error(); return null; }
                der = Ossl.ToDer(x);
                Ossl.X509_free(x);
            }
            finally { Ossl.BIO_free(bio); }
            if (keyPem == null) return der;
            bool encryptedLabel = password != null && Encoding.ASCII.GetString(keyPem).Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.Ordinal);
            if (password != null && !encryptedLabel)
                throw new CryptographicException("The key contents do not contain a PEM, the content is malformed, or the key does not match the certificate.");
            fixed (byte* p = keyPem) bio = Ossl.BIO_new_mem_buf(p, keyPem.Length);
            try
            {
                IntPtr k;
                fixed (byte* pw = password ?? new byte[1]) k = Ossl.PEM_read_bio_PrivateKey(bio, null, IntPtr.Zero, pw);
                if (k == IntPtr.Zero)
                {
                    Ossl.TakeErrorString();
                    throw new CryptographicException(encryptedLabel
                        ? "The EncryptedPrivateKeyInfo structure was decoded but was not successfully interpreted, the password may be incorrect."
                        : "The key contents do not contain a PEM, the content is malformed, or the key does not match the certificate.");
                }
                key = k;
            }
            finally { Ossl.BIO_free(bio); }
            return der;
        }

        internal static byte[] ReadFile(string path)
        {
#if CSHARPC_TARGET_MACOS
            // .NET on macOS reads the file itself: a missing one is the file system's exception.
            return File.ReadAllBytes(path);
#elif CSHARPC_TARGET_WINDOWS
            // .NET on Windows opens the file with CryptoAPI: a missing one is a CryptographicException with the Win32 text.
            if (!File.Exists(path))
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                throw new CryptographicException(dir != null && !Directory.Exists(dir)
                    ? "The system cannot find the path specified." : "The system cannot find the file specified.");
            }
            return File.ReadAllBytes(path);
#else
            // BIO_new_file reports a missing file as OpenSSL's error text, which .NET surfaces unchanged.
            Ossl.EnsureInitialized();
            byte[] name = Ossl.Utf8Z(path);
            IntPtr bio;
            fixed (byte* n = name) fixed (byte* mode = "rb\0"u8) bio = Ossl.BIO_new_file(n, mode);
            if (bio == IntPtr.Zero) throw Ossl.Error();
            Ossl.BIO_free(bio);
            return File.ReadAllBytes(path);
#endif
        }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Certificate")]
    public class X509CertificateImpl : IDisposable
    {
        internal byte[] _der;
        internal X509Parsed _parsed;
        internal IntPtr _x509;
        internal IntPtr _key;
        internal List<byte[]> _others;
        private byte[] _thumbprint;

        public X509CertificateImpl() { }

        public X509CertificateImpl(byte[] data)
        {
            if (data != null && data.Length != 0) Load(data, null, false);
        }

        internal X509CertificateImpl(ReadOnlySpan<byte> data)
        {
            if (!data.IsEmpty) Load(data.ToArray(), null, false);
        }

        public X509CertificateImpl(byte[] rawData, string password) : this(rawData, password, X509KeyStorageFlags.DefaultKeySet) { }

        public X509CertificateImpl(byte[] rawData, string password, X509KeyStorageFlags keyStorageFlags)
        {
            if (rawData == null || rawData.Length == 0) throw new ArgumentException("Array cannot be empty or null.", "rawData");
            Load(rawData, password, true);
        }

        public X509CertificateImpl(string fileName) : this(fileName, (string)null, X509KeyStorageFlags.DefaultKeySet) { }

        public X509CertificateImpl(string fileName, string password) : this(fileName, password, X509KeyStorageFlags.DefaultKeySet) { }

        public X509CertificateImpl(string fileName, string password, X509KeyStorageFlags keyStorageFlags)
        {
            if (fileName == null) throw new ArgumentNullException("fileName");
            Load(X509Reader.ReadFile(fileName), password, true);
        }

        public X509CertificateImpl(X509Certificate cert)
        {
            if (cert == null) throw new ArgumentNullException("cert");
            var other = (X509CertificateImpl)(object)cert;
            if (other._der != null) CopyFrom(other);
        }

        internal void CopyFrom(X509CertificateImpl other)
        {
            _der = other._der;
            _parsed = other._parsed;
            _others = other._others;
            if (other._key != IntPtr.Zero)
            {
                Ossl.EVP_PKEY_up_ref(other._key);
                _key = other._key;
            }
        }

        internal void Load(byte[] data, string password, bool passwordProvided)
        {
            byte[] der = X509Reader.Load(data, password, passwordProvided, out IntPtr key, out List<byte[]> others);
            _parsed = X509Parsed.Parse(der);
            _der = der;
            _key = key;
            _others = others;
        }

        internal void SetKey(IntPtr key) { _key = key; }

        internal void ThrowIfInvalid()
        {
            if (_der == null) throw new CryptographicException("m_safeCertContext is an invalid handle.");
        }

        /// <summary>The X509* (created on first use; owned by the certificate).</summary>
        public IntPtr Handle
        {
            get
            {
                if (_der == null) return IntPtr.Zero;
                if (_x509 == IntPtr.Zero) { Ossl.EnsureInitialized(); _x509 = Ossl.FromDer(_der); }
                return _x509;
            }
        }

        public virtual void Reset()
        {
            if (_x509 != IntPtr.Zero) Ossl.X509_free(_x509);
            if (_key != IntPtr.Zero) Ossl.EVP_PKEY_free(_key);
            _x509 = IntPtr.Zero;
            _key = IntPtr.Zero;
            _der = null;
            _parsed = null;
            _thumbprint = null;
        }

        public string Issuer
        {
            get
            {
                ThrowIfInvalid();
                return X500Decoder.Decode(_parsed.Issuer, true, X500DistinguishedNameFlags.Reversed);
            }
        }

        public string Subject
        {
            get
            {
                ThrowIfInvalid();
                return X500Decoder.Decode(_parsed.Subject, true, X500DistinguishedNameFlags.Reversed);
            }
        }

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) Reset();
        }

        public override bool Equals(object obj) => obj is X509Certificate other && Equals(other);

        public virtual bool Equals(X509Certificate other)
        {
            if (other == null) return false;
            var o = (X509CertificateImpl)(object)other;
            if (ReferenceEquals(this, o)) return true;
            if (_der == null || o._der == null) return false;
            return ((ReadOnlySpan<byte>)_der).SequenceEqual(o._der);
        }

        public virtual byte[] Export(X509ContentType contentType) => Export(contentType, (string)null);

        public virtual byte[] Export(X509ContentType contentType, string password)
        {
            if (!(contentType == X509ContentType.Cert || contentType == X509ContentType.SerializedCert || contentType == X509ContentType.Pkcs12))
                throw new CryptographicException("The X509ContentType is not valid for this operation.");
            ThrowIfInvalid();
            if (contentType == X509ContentType.Cert) return (byte[])_der.Clone();
            throw new NotSupportedException("Exporting a certificate as " + contentType + " is not supported by csharpc yet.");
        }

        public virtual string GetRawCertDataString() { ThrowIfInvalid(); return Convert.ToHexString(_der); }

        private byte[] RawHash()
        {
            if (_thumbprint == null) _thumbprint = SHA1.HashData(_der);
            return _thumbprint;
        }

        public virtual byte[] GetCertHash() { ThrowIfInvalid(); return (byte[])RawHash().Clone(); }

        public virtual byte[] GetCertHash(HashAlgorithmName hashAlgorithm) { ThrowIfInvalid(); return HashData(hashAlgorithm, _der); }

        public virtual string GetCertHashString() { ThrowIfInvalid(); return Convert.ToHexString(RawHash()); }

        public virtual string GetCertHashString(HashAlgorithmName hashAlgorithm) { ThrowIfInvalid(); return Convert.ToHexString(HashData(hashAlgorithm, _der)); }

        internal static byte[] HashData(HashAlgorithmName name, byte[] data)
        {
            switch (name.Name)
            {
                case "SHA1": return SHA1.HashData(data);
                case "SHA256": return SHA256.HashData(data);
                case "SHA384": return SHA384.HashData(data);
                case "SHA512": return SHA512.HashData(data);
                case "MD5": return MD5.HashData(data);
                default: throw new CryptographicException("'" + name.Name + "' is not a known hash algorithm.");
            }
        }

        public virtual string GetEffectiveDateString() { ThrowIfInvalid(); return FormatDate(GetNotBefore()); }

        public virtual string GetExpirationDateString() { ThrowIfInvalid(); return FormatDate(GetNotAfter()); }

        public virtual string GetFormat() => "X509";

        public virtual string GetPublicKeyString() { ThrowIfInvalid(); return Convert.ToHexString(_parsed.KeyValue); }

        public virtual byte[] GetPublicKey() { ThrowIfInvalid(); return (byte[])_parsed.KeyValue.Clone(); }

        public virtual byte[] GetRawCertData() { ThrowIfInvalid(); return (byte[])_der.Clone(); }

        public override int GetHashCode()
        {
            if (_der == null) return 0;
            byte[] t = RawHash();
            int value = 0;
            for (int i = 0; i < t.Length && i < 4; ++i) value = value << 8 | t[i];
            return value;
        }

        public virtual string GetKeyAlgorithm() { ThrowIfInvalid(); return _parsed.KeyOid; }

        public virtual byte[] GetKeyAlgorithmParameters()
        {
            ThrowIfInvalid();
            return _parsed.KeyParameters == null ? null : (byte[])_parsed.KeyParameters.Clone();
        }

        public virtual string GetKeyAlgorithmParametersString()
        {
            ThrowIfInvalid();
            return _parsed.KeyParameters == null ? null : Convert.ToHexString(_parsed.KeyParameters);
        }

        public virtual byte[] GetSerialNumber()
        {
            ThrowIfInvalid();
            byte[] s = (byte[])_parsed.Serial.Clone();
            Array.Reverse(s); // little-endian, as .NET's GetSerialNumber
            return s;
        }

        public ReadOnlyMemory<byte> SerialNumberBytes { get { ThrowIfInvalid(); return _parsed.Serial; } }

        public virtual string GetSerialNumberString() { ThrowIfInvalid(); return Convert.ToHexString(_parsed.Serial); }

        public override string ToString() => ToString(false);

        public virtual string ToString(bool fVerbose)
        {
            if (!fVerbose || _der == null) return GetType().ToString();
            var sb = new StringBuilder();
            sb.AppendLine("[Subject]");
            sb.Append("  ").AppendLine(Subject);
            sb.AppendLine();
            sb.AppendLine("[Issuer]");
            sb.Append("  ").AppendLine(Issuer);
            sb.AppendLine();
            sb.AppendLine("[Serial Number]");
            sb.Append("  ").Append(Convert.ToHexString(_parsed.Serial));
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("[Not Before]");
            sb.Append("  ").AppendLine(FormatDate(GetNotBefore()));
            sb.AppendLine();
            sb.AppendLine("[Not After]");
            sb.Append("  ").AppendLine(FormatDate(GetNotAfter()));
            sb.AppendLine();
            sb.AppendLine("[Thumbprint]");
            sb.Append("  ").Append(Convert.ToHexString(RawHash()));
            sb.AppendLine();
            return sb.ToString();
        }

        internal DateTime GetNotBefore() { ThrowIfInvalid(); return _parsed.NotBefore.ToLocalTime(); }

        internal DateTime GetNotAfter() { ThrowIfInvalid(); return _parsed.NotAfter.ToLocalTime(); }

        protected static string FormatDate(DateTime date) => date.ToString(CultureInfo.CurrentCulture);
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Certificate2")]
    public class X509Certificate2Impl : X509CertificateImpl
    {
        public X509Certificate2Impl() : base() { }
        public X509Certificate2Impl(X509Certificate certificate) : base(certificate) { }
        public X509Certificate2Impl(byte[] rawData) : base(rawData) { }
        public X509Certificate2Impl(ReadOnlySpan<byte> rawData) : base(rawData) { }
        public X509Certificate2Impl(byte[] rawData, string password) : base(rawData, password) { }
        public X509Certificate2Impl(byte[] rawData, string password, X509KeyStorageFlags keyStorageFlags) : base(rawData, password, keyStorageFlags) { }
        public X509Certificate2Impl(string fileName) : base(fileName) { }
        public X509Certificate2Impl(string fileName, string password) : base(fileName, password) { }
        public X509Certificate2Impl(string fileName, string password, X509KeyStorageFlags keyStorageFlags) : base(fileName, password, keyStorageFlags) { }

        /// <summary>A certificate over DER (and an owned key), for the chain, SslStream and the loader.</summary>
        internal static X509Certificate2Impl FromDer(byte[] der, IntPtr key)
        {
            var c = new X509Certificate2Impl();
            c._parsed = X509Parsed.Parse(der);
            c._der = der;
            c._key = key;
            return c;
        }

        internal static X509Certificate2 Wrap(byte[] der, IntPtr key) => (X509Certificate2)(object)FromDer(der, key);

        public string Thumbprint { get { ThrowIfInvalid(); return GetCertHashString(); } }

        public bool HasPrivateKey { get { ThrowIfInvalid(); return _key != IntPtr.Zero; } }

        // Kestrel's certificate loader reaches these overloads for key types csharpc has no implementation of (only RSA keys are
        // loaded, from PEM and PKCS#12); a program that loads a certificate with such a key gets this explicit diagnostic.
        public X509Certificate2 CopyWithPrivateKey(ECDiffieHellman privateKey) => throw Tls.NotSupported("X509Certificate2.CopyWithPrivateKey(ECDiffieHellman)");
        public X509Certificate2 CopyWithPrivateKey(MLDsa privateKey) => throw Tls.NotSupported("X509Certificate2.CopyWithPrivateKey(MLDsa)");
#pragma warning disable SYSLIB5006 // SlhDsa and CompositeMLDsa are [Experimental]: named here only to refuse them
        public X509Certificate2 CopyWithPrivateKey(MLKem privateKey) => throw Tls.NotSupported("X509Certificate2.CopyWithPrivateKey(MLKem)");
        public X509Certificate2 CopyWithPrivateKey(SlhDsa privateKey) => throw Tls.NotSupported("X509Certificate2.CopyWithPrivateKey(SlhDsa)");
        public X509Certificate2 CopyWithPrivateKey(CompositeMLDsa privateKey) => throw Tls.NotSupported("X509Certificate2.CopyWithPrivateKey(CompositeMLDsa)");
#pragma warning restore SYSLIB5006

        public byte[] RawData { get { ThrowIfInvalid(); return (byte[])_der.Clone(); } }

        public ReadOnlyMemory<byte> RawDataMemory { get { ThrowIfInvalid(); return _der; } }

        public DateTime NotAfter => GetNotAfter();

        public DateTime NotBefore => GetNotBefore();

        public string SerialNumber => GetSerialNumberString();

        public int Version { get { ThrowIfInvalid(); return _parsed.Version; } }

        public bool Archived
        {
            get { ThrowIfInvalid(); return false; }
            set { throw new NotSupportedException("X509Certificate2.Archived is not supported by csharpc yet."); }
        }

        public string FriendlyName
        {
            get { ThrowIfInvalid(); return ""; }
            set { throw new NotSupportedException("X509Certificate2.FriendlyName is not supported by csharpc yet."); }
        }

        public Oid SignatureAlgorithm { get { ThrowIfInvalid(); return new Oid(_parsed.SignatureOid); } }

        public X500DistinguishedName SubjectName { get { ThrowIfInvalid(); return (X500DistinguishedName)(object)new X500DistinguishedNameImpl(_parsed.Subject); } }

        public X500DistinguishedName IssuerName { get { ThrowIfInvalid(); return (X500DistinguishedName)(object)new X500DistinguishedNameImpl(_parsed.Issuer); } }

        public PublicKey PublicKey
        {
            get
            {
                ThrowIfInvalid();
                AsnEncodedData parameters = new AsnEncodedData(new Oid(_parsed.KeyOid), _parsed.KeyParameters ?? new byte[] { 0x05, 0x00 });
                AsnEncodedData value = new AsnEncodedData(new Oid(_parsed.KeyOid), _parsed.KeyValue);
                return new PublicKey(new Oid(_parsed.KeyOid), parameters, value);
            }
        }

        public X509ExtensionCollection Extensions => throw Tls.NotSupported("X509Certificate2.Extensions");

        public string GetNameInfo(X509NameType nameType, bool forIssuer)
        {
            ThrowIfInvalid();
            byte[] name = forIssuer ? _parsed.Issuer : _parsed.Subject;
            switch (nameType)
            {
                case X509NameType.SimpleName:
                {
                    // CN, then OU, then O of the name; where none exists .NET looks further (alternative names).
                    foreach (string oid in new[] { "2.5.4.3", "2.5.4.11", "2.5.4.10" })
                        if (FindAttribute(name, oid) is string value) return value;
                    throw Tls.NotSupported("X509Certificate2.GetNameInfo(SimpleName) of a name without CN, OU or O");
                }
                case X509NameType.DnsFromAlternativeName:
                    if (!forIssuer)
                    {
                        List<string> dns = _parsed.DnsNames();
                        if (dns.Count > 0) return dns[0];
                        if (_parsed.SubjectAltNames == null) return "";
                    }
                    throw Tls.NotSupported("X509Certificate2.GetNameInfo(DnsFromAlternativeName) of this certificate");
                case X509NameType.DnsName:
                {
                    string cn = FindAttribute(name, "2.5.4.3");
                    List<string> dns = forIssuer ? new List<string>() : _parsed.DnsNames();
                    if (dns.Count == 0 && _parsed.SubjectAltNames == null && cn != null) return cn;
                    if (dns.Count > 0 && (cn == null || cn == dns[0])) return dns[0];
                    throw Tls.NotSupported("X509Certificate2.GetNameInfo(DnsName) of this certificate");
                }
                default:
                    throw Tls.NotSupported("X509Certificate2.GetNameInfo(" + nameType + ")");
            }
        }

        private static string FindAttribute(byte[] name, string oidValue)
        {
            // The attribute's value in the name's own order, the way the shim's X509_NAME search finds the last match first.
            string found = null;
            Der.Read(name, 0, name.Length, out _, out int start, out _, out int end);
            for (int pos = start; pos < end;)
            {
                Der.Read(name, pos, end, out _, out int rs, out _, out int rn);
                for (int a = rs; a < rn;)
                {
                    Der.Read(name, a, rn, out _, out int astart, out _, out int anext);
                    Der.Read(name, astart, anext, out _, out int os, out int ol, out int onext);
                    if (Der.ReadOid(name, os, ol) == oidValue)
                    {
                        Der.Read(name, onext, anext, out int vt, out int vs, out int vl, out _);
                        if (vt == 0x0C) found ??= new UTF8Encoding(false, true).GetString(name, vs, vl);
                        else if (vt == 0x13 || vt == 0x16) found ??= Encoding.ASCII.GetString(name, vs, vl);
                    }
                    a = anext;
                }
                pos = rn;
            }
            return found;
        }

        public static X509ContentType GetCertContentType(byte[] rawData)
        {
            if (rawData == null || rawData.Length == 0) throw new ArgumentException("Array cannot be empty or null.", "rawData");
            if (rawData[0] == 0x30)
            {
                if (Ossl_IsCertificate(rawData)) return X509ContentType.Cert;
                try { X509Reader.Load(rawData, null, false, out IntPtr k, out _); if (k != IntPtr.Zero) Ossl.EVP_PKEY_free(k); return X509ContentType.Pfx; }
                catch (CryptographicException e) when (e.Message.StartsWith("The certificate data cannot be read", StringComparison.Ordinal)) { return X509ContentType.Pfx; }
                catch (CryptographicException) { }
            }
            else if (Encoding.ASCII.GetString(rawData).Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)) return X509ContentType.Cert;
            throw Der.Corrupt();
        }

        private static bool Ossl_IsCertificate(byte[] der)
        {
            Ossl.EnsureInitialized();
            IntPtr x = Ossl.FromDer(der);
            if (x == IntPtr.Zero) return false;
            Ossl.X509_free(x);
            return true;
        }

        public static X509ContentType GetCertContentType(string fileName)
        {
            if (fileName == null) throw new ArgumentNullException("fileName");
            return GetCertContentType(X509Reader.ReadFile(fileName));
        }

        public static X509Certificate2 CreateFromPem(ReadOnlySpan<char> certPem, ReadOnlySpan<char> keyPem)
        {
            byte[] der = X509Reader.FromPem(Encoding.UTF8.GetBytes(certPem.ToString()), Encoding.UTF8.GetBytes(keyPem.ToString()), out IntPtr key);
            if (der == null) throw new CryptographicException("No supported certificate format was found. Check that the input represents the contents of a PEM-encoded certificate file, not the path to such a file.");
            return Wrap(der, key);
        }

        public static X509Certificate2 CreateFromPem(ReadOnlySpan<char> certPem)
        {
            byte[] der = X509Reader.FromPem(Encoding.UTF8.GetBytes(certPem.ToString()), null, out IntPtr _);
            if (der == null) throw new CryptographicException("No supported certificate format was found. Check that the input represents the contents of a PEM-encoded certificate file, not the path to such a file.");
            return Wrap(der, IntPtr.Zero);
        }

        public static X509Certificate2 CreateFromPemFile(string certPemFilePath, string keyPemFilePath = null)
        {
            if (certPemFilePath == null) throw new ArgumentNullException("certPemFilePath");
            // .NET reads the key from the certificate's file when no key file is given (and fails if it holds none).
            string cert = File.ReadAllText(certPemFilePath);
            return CreateFromPem(cert, keyPemFilePath == null ? cert : File.ReadAllText(keyPemFilePath));
        }

        public static X509Certificate2 CreateFromEncryptedPem(ReadOnlySpan<char> certPem, ReadOnlySpan<char> keyPem, ReadOnlySpan<char> password)
        {
            // The certificate is read first (its error wins), then the key.
            byte[] der = X509Reader.FromPem(Encoding.UTF8.GetBytes(certPem.ToString()), Encoding.UTF8.GetBytes(keyPem.ToString()), out IntPtr key,
                Encoding.UTF8.GetBytes(password.ToString() + "\0"));
            if (der == null) throw new CryptographicException("The certificate contents do not contain a PEM with a CERTIFICATE label, or the content is malformed.");
            return Wrap(der, key);
        }

        public static X509Certificate2 CreateFromEncryptedPemFile(string certPemFilePath, ReadOnlySpan<char> password, string keyPemFilePath = null)
        {
            if (certPemFilePath == null) throw new ArgumentNullException("certPemFilePath");
            string cert = File.ReadAllText(certPemFilePath);
            return CreateFromEncryptedPem(cert, keyPemFilePath == null ? cert : File.ReadAllText(keyPemFilePath), password);
        }

        public string ExportCertificatePem()
        {
            ThrowIfInvalid();
            return new string(PemEncoding.Write("CERTIFICATE", _der));
        }

        public override string ToString() => base.ToString(true);

        public override string ToString(bool verbose)
        {
            if (!verbose || _der == null) return ToString();
            throw Tls.NotSupported("X509Certificate2.ToString(true) (it lists the extensions)");
        }

        // ---- key access (the extension methods of RSACertificateExtensions and friends call these) ----

        internal RSA GetRsaPrivateKey()
        {
            ThrowIfInvalid();
            if (_key == IntPtr.Zero || _parsed.KeyOid != "1.2.840.113549.1.1.1") return null;
            return RsaOpenSsl.FromKey(_key);
        }

        internal RSA GetRsaPublicKey()
        {
            ThrowIfInvalid();
            return _parsed.KeyOid == "1.2.840.113549.1.1.1" ? X509Keys.RsaPublic(_parsed.KeyValue) : null;
        }
    }

    [ShadowMembers("System.Security.Cryptography.X509Certificates.RSACertificateExtensions")]
    internal static class RSACertificateExtensionsMembers
    {
        public static RSA GetRSAPrivateKey(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException("certificate");
            return ((X509Certificate2Impl)(object)certificate).GetRsaPrivateKey();
        }

        public static RSA GetRSAPublicKey(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException("certificate");
            return ((X509Certificate2Impl)(object)certificate).GetRsaPublicKey();
        }

        public static X509Certificate2 CopyWithPrivateKey(X509Certificate2 certificate, RSA privateKey) => throw Tls.NotSupported("RSACertificateExtensions.CopyWithPrivateKey");
    }

    [ShadowMembers("System.Security.Cryptography.X509Certificates.X509CertificateLoader")]
    internal static class X509CertificateLoaderMembers
    {
        private static X509Certificate2 Load(byte[] data, string password, bool pfxOnly)
        {
            var c = new X509Certificate2Impl();
            c.Load(data, password, true);
            return (X509Certificate2)(object)c;
        }

        public static X509Certificate2 LoadCertificate(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            return LoadCertificate((ReadOnlySpan<byte>)data);
        }

        public static X509Certificate2 LoadCertificate(ReadOnlySpan<byte> data)
        {
            byte[] bytes = data.ToArray();
            var c = new X509Certificate2Impl();
            c.Load(bytes, null, false);
            // A certificate loader doesn't take a private key along (and refuses a PFX).
            if (c._key != IntPtr.Zero) { Ossl.EVP_PKEY_free(c._key); c._key = IntPtr.Zero; }
            return (X509Certificate2)(object)c;
        }

        public static X509Certificate2 LoadCertificateFromFile(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            return LoadCertificate(X509Reader.ReadFile(path));
        }

        public static X509Certificate2 LoadPkcs12(byte[] data, string password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits loaderLimits = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            return Load(data, password, true);
        }

        public static X509Certificate2 LoadPkcs12(ReadOnlySpan<byte> data, ReadOnlySpan<char> password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits loaderLimits = null)
            => Load(data.ToArray(), password.ToString(), true);

        public static X509Certificate2 LoadPkcs12FromFile(string path, string password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits loaderLimits = null)
        {
            if (path == null) throw new ArgumentNullException("path");
            return Load(X509Reader.ReadFile(path), password, true);
        }

        /// <summary>Every certificate of a PFX, in the reverse of the file's order (what .NET 11 on Linux returns for PFX files whose private-key certificate comes first, as OpenSSL writes them).</summary>
        private static X509Certificate2Collection LoadCollection(byte[] data, string password)
        {
            var main = Load(data, password, true);
            var mainImpl = (X509Certificate2Impl)(object)main;
            var collection = new X509Certificate2CollectionImpl();
            // The certificate of the private key is the bag equal to the one PKCS12_parse found.
            List<byte[]> order = X509Reader.Pkcs12CertificatesInFileOrder(data, password);
            if (order == null)
            {
                order = new List<byte[]> { mainImpl._der };
                if (mainImpl._others != null) order.AddRange(mainImpl._others);
            }
            order.Reverse();
            bool mainAdded = false;
            foreach (byte[] der in order)
            {
                if (!mainAdded && der.AsSpan().SequenceEqual(mainImpl._der))
                {
                    collection.Add(main);
                    mainAdded = true;
                    continue;
                }
                var other = new X509Certificate2Impl();
                other.Load(der, null, false);
                collection.Add((X509Certificate2)(object)other);
            }
            if (!mainAdded) collection.Add(main);
            return (X509Certificate2Collection)(object)collection;
        }

        public static X509Certificate2Collection LoadPkcs12Collection(byte[] data, string password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits loaderLimits = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            return LoadCollection(data, password);
        }

        public static X509Certificate2Collection LoadPkcs12Collection(ReadOnlySpan<byte> data, ReadOnlySpan<char> password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits loaderLimits = null)
            => LoadCollection(data.ToArray(), password.ToString());

        public static X509Certificate2Collection LoadPkcs12CollectionFromFile(string path, string password, X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits loaderLimits = null)
        {
            if (path == null) throw new ArgumentNullException("path");
            return LoadCollection(X509Reader.ReadFile(path), password);
        }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509CertificateCollection")]
    public class X509CertificateCollectionImpl : CollectionBaseImpl
    {
        public X509CertificateCollectionImpl() { }

        public X509CertificateCollectionImpl(X509Certificate[] value) { AddRange(value); }

        public X509CertificateCollectionImpl(X509CertificateCollection value) { AddRange(value); }

        internal X509Certificate At(int index) => (X509Certificate)InnerList[index];

        public X509Certificate this[int index]
        {
            get => (X509Certificate)GetItem(index);
            set => SetItem(index, value);
        }

        public int Add(X509Certificate value) => AddItem(value);

        public void AddRange(X509Certificate[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            for (int i = 0; i < value.Length; i++) Add(value[i]);
        }

        public void AddRange(X509CertificateCollection value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var other = (X509CertificateCollectionImpl)(object)value;
            for (int i = 0; i < other.Count; i++) Add(other.At(i));
        }

        public bool Contains(X509Certificate value) => ContainsItem(value);

        public void CopyTo(X509Certificate[] array, int index) => InnerList.CopyTo(array, index);

        public int IndexOf(X509Certificate value) => IndexOfItem(value);

        public void Insert(int index, X509Certificate value) => InsertItem(index, value);

        public void Remove(X509Certificate value) => RemoveItem(value);

        public new X509CertificateCollection.X509CertificateEnumerator GetEnumerator() =>
            (X509CertificateCollection.X509CertificateEnumerator)(object)new X509CertificateEnumeratorImpl((X509CertificateCollection)(object)this);

        [Shadows("System.Security.Cryptography.X509Certificates.X509CertificateCollection+X509CertificateEnumerator")]
        public class X509CertificateEnumeratorImpl : IEnumerator, IDisposable
        {
            private readonly IEnumerator _enumerator;

            public X509CertificateEnumeratorImpl(X509CertificateCollection mappings)
            {
                ArgumentNullException.ThrowIfNull(mappings);
                _enumerator = ((IEnumerable)(object)mappings).GetEnumerator();
            }

            public X509Certificate Current => (X509Certificate)_enumerator.Current;
            object IEnumerator.Current => _enumerator.Current;
            public bool MoveNext() => _enumerator.MoveNext();
            bool IEnumerator.MoveNext() => _enumerator.MoveNext();
            public void Reset() => _enumerator.Reset();
            void IEnumerator.Reset() => _enumerator.Reset();
            public void Dispose() { }
        }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Certificate2Collection")]
    public class X509Certificate2CollectionImpl : X509CertificateCollectionImpl, IEnumerable<X509Certificate2>
    {
        public X509Certificate2CollectionImpl() : base() { }

        public X509Certificate2CollectionImpl(X509Certificate2 certificate) : base() { Add(certificate); }

        public X509Certificate2CollectionImpl(X509Certificate2[] certificates) : base() { AddRange(certificates); }

        public X509Certificate2CollectionImpl(X509Certificate2Collection certificates) : base() { AddRange(certificates); }

        public new X509Certificate2 this[int index]
        {
            get => (X509Certificate2)GetItem(index);
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                SetItem(index, value);
            }
        }

        public int Add(X509Certificate2 certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            return AddItem(certificate);
        }

        public void AddRange(X509Certificate2[] certificates)
        {
            ArgumentNullException.ThrowIfNull(certificates);
            int i = 0;
            try
            {
                for (; i < certificates.Length; i++) Add(certificates[i]);
            }
            catch
            {
                for (int j = 0; j < i; j++) Remove(certificates[j]);
                throw;
            }
        }

        public void AddRange(X509Certificate2Collection certificates)
        {
            ArgumentNullException.ThrowIfNull(certificates);
            var other = (X509CertificateCollectionImpl)(object)certificates;
            for (int i = 0; i < other.Count; i++) Add((X509Certificate2)other.At(i));
        }

        public bool Contains(X509Certificate2 certificate) => ContainsItem(certificate);

        public int IndexOf(X509Certificate2 certificate) => IndexOfItem(certificate);

        public void Insert(int index, X509Certificate2 certificate) => InsertItem(index, certificate);

        public void Remove(X509Certificate2 certificate) => RemoveItem(certificate);

        public void CopyTo(X509Certificate2[] array, int index) => InnerList.CopyTo(array, index);

        public X509Certificate2Collection Find(X509FindType findType, object findValue, bool validOnly)
        {
            ArgumentNullException.ThrowIfNull(findValue);
            if (validOnly) throw Tls.NotSupported("X509Certificate2Collection.Find(validOnly: true)");
            var result = new X509Certificate2CollectionImpl();
            for (int i = 0; i < Count; i++)
            {
                var cert = (X509Certificate2Impl)(object)At(i);
                bool match;
                switch (findType)
                {
                    case X509FindType.FindByThumbprint:
                        match = string.Equals(cert.Thumbprint, ((string)findValue).Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
                        break;
                    case X509FindType.FindBySerialNumber:
                        match = string.Equals(cert.SerialNumber.TrimStart('0'), ((string)findValue).Replace(" ", "").TrimStart('0'), StringComparison.OrdinalIgnoreCase);
                        break;
                    case X509FindType.FindBySubjectName:
                        match = cert.Subject.IndexOf((string)findValue, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case X509FindType.FindBySubjectDistinguishedName:
                        match = string.Equals(cert.Subject, (string)findValue, StringComparison.OrdinalIgnoreCase);
                        break;
                    case X509FindType.FindByIssuerName:
                        match = cert.Issuer.IndexOf((string)findValue, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case X509FindType.FindByIssuerDistinguishedName:
                        match = string.Equals(cert.Issuer, (string)findValue, StringComparison.OrdinalIgnoreCase);
                        break;
                    default:
                        throw Tls.NotSupported("X509Certificate2Collection.Find(" + findType + ")");
                }
                if (match) result.Add((X509Certificate2)(object)cert);
            }
            return (X509Certificate2Collection)(object)result;
        }

        public void ImportFromPemFile(string certPemFilePath) => throw Tls.NotSupported("X509Certificate2Collection.ImportFromPemFile");

        public new X509Certificate2Enumerator GetEnumerator() => (X509Certificate2Enumerator)(object)new X509Certificate2EnumeratorImpl(this);

        IEnumerator<X509Certificate2> IEnumerable<X509Certificate2>.GetEnumerator() => (IEnumerator<X509Certificate2>)(object)new X509Certificate2EnumeratorImpl(this);
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Certificate2Enumerator")]
    public sealed class X509Certificate2EnumeratorImpl : IEnumerator<X509Certificate2>
    {
        private readonly X509CertificateCollectionImpl _collection;
        private int _index = -1;

        internal X509Certificate2EnumeratorImpl(X509CertificateCollectionImpl collection) { _collection = collection; }

        public X509Certificate2 Current => (X509Certificate2)_collection.At(_index);
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index >= _collection.Count - 1) return false;
            _index++;
            return true;
        }

        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}
#endif
