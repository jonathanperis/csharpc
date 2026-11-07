// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Security.Cryptography/src/
// System/Security/Cryptography/X509Certificates/{X509Chain,X509ChainPolicy,X509ChainElement,X509ChainStatus}.cs and
// OpenSslX509ChainProcessor.cs; see THIRD-PARTY-NOTICES.md.
// X509Chain on Linux and Windows: OpenSSL builds and verifies the chain (X509_verify_cert with a verify callback that records every
// error and carries on, as .NET's shim does), the errors map to X509ChainStatusFlags with OpenSSL's own message as the
// status information ("self-signed certificate"). Trust is the system store (SSL_CERT_FILE / SSL_CERT_DIR as OpenSSL
// reads them on Linux, the ROOT certificate stores of the current user and the machine on Windows) or the CustomTrustStore; revocation checking and downloading missing issuers are not implemented, so a
// RevocationMode other than NoCheck is an explicit NotSupportedException (docs/adr/0022-tls.md).
#nullable disable
#if CSHARPC_OPENSSL
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CsharpcCoreLib
{
    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainStatus")]
    public struct X509ChainStatusImpl
    {
        public X509ChainStatusFlags Status { get; set; }
        public string StatusInformation { get; set; }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainPolicy")]
    public sealed class X509ChainPolicyImpl
    {
        private X509RevocationMode _revocationMode;
        private X509RevocationFlag _revocationFlag;
        private OidCollection _applicationPolicy;
        private OidCollection _certificatePolicy;
        private X509Certificate2Collection _extraStore;
        private X509Certificate2Collection _customTrustStore;
        private DateTime _verificationTime;
        private bool _verificationTimeSet;

        public X509ChainPolicyImpl() { Reset(); }

        public OidCollection ApplicationPolicy => _applicationPolicy ??= new OidCollection();
        public OidCollection CertificatePolicy => _certificatePolicy ??= new OidCollection();
        public X509Certificate2Collection ExtraStore => _extraStore ??= new X509Certificate2Collection();
        public X509Certificate2Collection CustomTrustStore => _customTrustStore ??= new X509Certificate2Collection();
        public X509ChainTrustMode TrustMode { get; set; }
        public bool DisableCertificateDownloads { get; set; }
        public TimeSpan UrlRetrievalTimeout { get; set; }
        public X509VerificationFlags VerificationFlags { get; set; }

        public X509RevocationMode RevocationMode
        {
            get => _revocationMode;
            set
            {
                if (value < X509RevocationMode.NoCheck || value > X509RevocationMode.Offline)
                    throw new ArgumentException("The value '" + (int)value + "' is not valid for this usage of the type X509RevocationMode.", "value");
                _revocationMode = value;
            }
        }

        public X509RevocationFlag RevocationFlag
        {
            get => _revocationFlag;
            set
            {
                if (value < X509RevocationFlag.EndCertificateOnly || value > X509RevocationFlag.ExcludeRoot)
                    throw new ArgumentException("The value '" + (int)value + "' is not valid for this usage of the type X509RevocationFlag.", "value");
                _revocationFlag = value;
            }
        }

        public DateTime VerificationTime
        {
            get => _verificationTimeSet ? _verificationTime : DateTime.Now;
            set { _verificationTime = value; _verificationTimeSet = true; }
        }

        internal bool VerificationTimeIsSet => _verificationTimeSet;

        public void Reset()
        {
            _applicationPolicy = null;
            _certificatePolicy = null;
            _extraStore = null;
            _customTrustStore = null;
            _revocationMode = X509RevocationMode.Online;
            _revocationFlag = X509RevocationFlag.ExcludeRoot;
            _verificationTimeSet = false;
            TrustMode = X509ChainTrustMode.System;
            DisableCertificateDownloads = false;
            UrlRetrievalTimeout = TimeSpan.Zero;
            VerificationFlags = X509VerificationFlags.NoFlag;
        }

        public X509ChainPolicy Clone()
        {
            var clone = new X509ChainPolicyImpl();
            foreach (Oid o in ApplicationPolicy) clone.ApplicationPolicy.Add(o);
            foreach (Oid o in CertificatePolicy) clone.CertificatePolicy.Add(o);
            clone.ExtraStore.AddRange(ExtraStore);
            clone.CustomTrustStore.AddRange(CustomTrustStore);
            clone.TrustMode = TrustMode;
            clone.DisableCertificateDownloads = DisableCertificateDownloads;
            clone.UrlRetrievalTimeout = UrlRetrievalTimeout;
            clone.VerificationFlags = VerificationFlags;
            clone._revocationMode = _revocationMode;
            clone._revocationFlag = _revocationFlag;
            clone._verificationTime = _verificationTime;
            clone._verificationTimeSet = _verificationTimeSet;
            return (X509ChainPolicy)(object)clone;
        }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainElement")]
    public class X509ChainElementImpl
    {
        private readonly X509Certificate2 _certificate;
        private readonly X509ChainStatus[] _status;

        internal X509ChainElementImpl(X509Certificate2 certificate, X509ChainStatus[] status)
        {
            _certificate = certificate;
            _status = status;
        }

        public X509Certificate2 Certificate => _certificate;
        public X509ChainStatus[] ChainElementStatus => _status;
        public string Information => _status.Length == 0 ? "" : _status[0].StatusInformation;
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainElementCollection")]
    public sealed class X509ChainElementCollectionImpl : ICollection, IEnumerable
    {
        private readonly List<X509ChainElement> _elements;

        internal X509ChainElementCollectionImpl(List<X509ChainElement> elements) { _elements = elements; }

        public int Count => _elements.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public X509ChainElement this[int index] => _elements[index];

        public void CopyTo(X509ChainElement[] array, int index) => _elements.CopyTo(array, index);

        void ICollection.CopyTo(Array array, int index) { for (int i = 0; i < _elements.Count; i++) array.SetValue(_elements[i], index + i); }

        public X509ChainElementEnumerator GetEnumerator() => (X509ChainElementEnumerator)(object)new X509ChainElementEnumeratorImpl(_elements);

        IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)(object)new X509ChainElementEnumeratorImpl(_elements);
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509ChainElementEnumerator")]
    public sealed class X509ChainElementEnumeratorImpl : IEnumerator, IDisposable
    {
        private readonly List<X509ChainElement> _elements;
        private int _index = -1;

        internal X509ChainElementEnumeratorImpl(List<X509ChainElement> elements) { _elements = elements; }

        public X509ChainElement Current => _elements[_index];
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index >= _elements.Count - 1) return false;
            _index++;
            return true;
        }

        public void Reset() => _index = -1;

        // Roslyn's foreach tests the enumerator for IDisposable: the real class isn't one, here it is a no-op.
        public void Dispose() { }
    }

    internal static unsafe class ChainVerifier
    {
        private static readonly object s_lock = new object();
        private static readonly Dictionary<IntPtr, List<long>> s_errors = new Dictionary<IntPtr, List<long>>();

        [UnmanagedCallersOnly]
        private static int VerifyCallback(int ok, IntPtr ctx)
        {
            if (ok == 0)
            {
                long entry = ((long)Ossl.X509_STORE_CTX_get_error_depth(ctx) << 32) | (uint)Ossl.X509_STORE_CTX_get_error(ctx);
                lock (s_lock)
                    if (s_errors.TryGetValue(ctx, out List<long> list)) list.Add(entry);
            }
            return 1; // carry on: .NET reports every problem of every element
        }

        // X509_PURPOSE_SSL_CLIENT / SSL_SERVER
        private static int Purpose(OidCollection applicationPolicy)
        {
            foreach (Oid o in applicationPolicy)
            {
                if (o.Value == "1.3.6.1.5.5.7.3.1") return 2;
                if (o.Value == "1.3.6.1.5.5.7.3.2") return 1;
            }
            return 0;
        }

        private static X509ChainStatusFlags Map(int error)
        {
            switch (error)
            {
                case 18: case 19: case 27: return X509ChainStatusFlags.UntrustedRoot;
                case 2: case 20: case 21: return X509ChainStatusFlags.PartialChain;
                case 7: return X509ChainStatusFlags.NotSignatureValid;
                case 9: case 10: case 13: case 14: return X509ChainStatusFlags.NotTimeValid;
                case 23: return X509ChainStatusFlags.Revoked;
                case 24: case 25: return X509ChainStatusFlags.InvalidBasicConstraints;
                case 26: case 32: return X509ChainStatusFlags.NotValidForUsage;
                case 28: return X509ChainStatusFlags.ExplicitDistrust;
                default: throw Tls.NotSupported("X509Chain: OpenSSL verification error " + error + " (" + Marshal.PtrToStringUTF8(Ossl.X509_verify_cert_error_string(error)) + ")");
            }
        }

        /// <summary>The status text: OpenSSL's, as .NET shows on Linux; on macOS the Security.framework messages for the common errors.</summary>
        private static string Information(int error)
        {
#if CSHARPC_TARGET_MACOS
            switch (error)
            {
                case 18: case 19: return "The certificate was not trusted."; // errSecNotTrusted
                case 10: return "An expired certificate was detected."; // errSecCertificateExpired
            }
#endif
            return Marshal.PtrToStringUTF8(Ossl.X509_verify_cert_error_string(error));
        }

        private static bool Ignored(X509ChainStatusFlags flag, X509VerificationFlags flags)
        {
            switch (flag)
            {
                case X509ChainStatusFlags.NotTimeValid: return (flags & X509VerificationFlags.IgnoreNotTimeValid) != 0;
                case X509ChainStatusFlags.UntrustedRoot:
                case X509ChainStatusFlags.PartialChain: return (flags & X509VerificationFlags.AllowUnknownCertificateAuthority) != 0;
                case X509ChainStatusFlags.InvalidBasicConstraints: return (flags & X509VerificationFlags.IgnoreInvalidBasicConstraints) != 0;
                case X509ChainStatusFlags.NotValidForUsage: return (flags & X509VerificationFlags.IgnoreWrongUsage) != 0;
                case X509ChainStatusFlags.Revoked: return false;
                default: return false;
            }
        }

        internal static bool Build(X509Certificate2Impl leaf, X509ChainPolicyImpl policy, out List<X509ChainElement> elements, out X509ChainStatus[] chainStatus)
        {
            leaf.ThrowIfInvalid();
            if (policy.RevocationMode != X509RevocationMode.NoCheck)
                throw Tls.NotSupported("X509Chain revocation checking (set ChainPolicy.RevocationMode to NoCheck)");
            Ossl.EnsureInitialized();
            IntPtr store = Ossl.X509_STORE_new();
            IntPtr untrusted = Ossl.OPENSSL_sk_new_null();
            IntPtr ctx = Ossl.X509_STORE_CTX_new();
            if (store == IntPtr.Zero || untrusted == IntPtr.Zero || ctx == IntPtr.Zero) throw Ossl.Error();
            var errors = new List<long>();
            bool blocking = false;
            var result = new List<(byte[] Der, List<X509ChainStatusFlags> Flags, List<string> Info)>();
            try
            {
                if (policy.TrustMode == X509ChainTrustMode.System) Ossl.SetDefaultTrust(store);
                else
                    foreach (X509Certificate c in policy.CustomTrustStore)
                        Ossl.X509_STORE_add_cert(store, ((X509CertificateImpl)(object)c).Handle);
                Ossl.ERR_clear_error();
                foreach (X509Certificate c in policy.ExtraStore) Ossl.OPENSSL_sk_push(untrusted, ((X509CertificateImpl)(object)c).Handle);
                if (Ossl.X509_STORE_CTX_init(ctx, store, leaf.Handle, untrusted) != 1) throw Ossl.Error();
                int purpose = Purpose(policy.ApplicationPolicy);
                if (purpose != 0) Ossl.X509_VERIFY_PARAM_set_purpose(Ossl.X509_STORE_CTX_get0_param(ctx), purpose);
                if (policy.VerificationTimeIsSet)
                    Ossl.X509_STORE_CTX_set_time(ctx, 0, new DateTimeOffset(policy.VerificationTime.ToUniversalTime()).ToUnixTimeSeconds());
                lock (s_lock) s_errors[ctx] = errors;
                Ossl.X509_STORE_CTX_set_verify_cb(ctx, &VerifyCallback);
                Ossl.X509_verify_cert(ctx);
                IntPtr chain = Ossl.X509_STORE_CTX_get1_chain(ctx);
                if (chain != IntPtr.Zero)
                {
                    int n = Ossl.OPENSSL_sk_num(chain);
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr x = Ossl.OPENSSL_sk_value(chain, i);
                        result.Add((Ossl.ToDer(x), new List<X509ChainStatusFlags>(), new List<string>()));
                        Ossl.X509_free(x);
                    }
                    Ossl.OPENSSL_sk_free(chain);
                }
                if (result.Count == 0) result.Add((leaf._der, new List<X509ChainStatusFlags>(), new List<string>()));
                // The first error of the verification context may not have gone through the callback (a failure before the walk).
                int ctxError = Ossl.X509_STORE_CTX_get_error(ctx);
                if (errors.Count == 0 && ctxError != 0) errors.Add(((long)Ossl.X509_STORE_CTX_get_error_depth(ctx) << 32) | (uint)ctxError);
                foreach (long entry in errors)
                {
                    int depth = Math.Min((int)(entry >> 32), result.Count - 1), error = (int)(entry & 0xFFFFFFFF);
                    X509ChainStatusFlags flag = Map(error);
                    if (result[depth].Flags.Contains(flag)) continue;
                    if (!Ignored(flag, policy.VerificationFlags)) blocking = true;
                    result[depth].Flags.Add(flag);
                    result[depth].Info.Add(Information(error));
                }
            }
            finally
            {
                lock (s_lock) s_errors.Remove(ctx);
                Ossl.X509_STORE_CTX_free(ctx);
                Ossl.OPENSSL_sk_free(untrusted);
                Ossl.X509_STORE_free(store);
            }
            elements = new List<X509ChainElement>();
            var all = new List<X509ChainStatus>();
            foreach (var (der, flags, info) in result)
            {
                var statuses = new X509ChainStatus[flags.Count];
                for (int i = 0; i < flags.Count; i++)
                {
                    statuses[i] = new X509ChainStatus { Status = flags[i], StatusInformation = info[i] };
                    bool seen = false;
                    foreach (X509ChainStatus s in all) if (s.Status == flags[i]) seen = true;
                    if (!seen) all.Add(statuses[i]);
                }
                elements.Add((X509ChainElement)(object)new X509ChainElementImpl(X509Certificate2Impl.Wrap(der, IntPtr.Zero), statuses));
            }
            chainStatus = all.ToArray();
            return !blocking; // a status an ignore flag covers is still reported, as in .NET
        }
    }

    [Shadows("System.Security.Cryptography.X509Certificates.X509Chain")]
    public class X509ChainImpl : IDisposable
    {
        private X509ChainPolicy _policy = new X509ChainPolicy();
        private List<X509ChainElement> _elements;
        private X509ChainStatus[] _status;

        public X509ChainImpl() { }

        public X509ChainImpl(bool useMachineContext)
        {
            if (useMachineContext) throw Tls.NotSupported("X509Chain(useMachineContext: true)");
        }

        public X509ChainPolicy ChainPolicy
        {
            get => _policy;
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                _policy = value;
            }
        }

        public X509ChainElementCollection ChainElements => (X509ChainElementCollection)(object)new X509ChainElementCollectionImpl(_elements ?? new List<X509ChainElement>());

        public X509ChainStatus[] ChainStatus => _status == null ? new X509ChainStatus[0] : (X509ChainStatus[])_status.Clone();

        public bool Build(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException("certificate");
            var leaf = (X509Certificate2Impl)(object)certificate;
            bool ok = ChainVerifier.Build(leaf, (X509ChainPolicyImpl)(object)_policy, out _elements, out _status);
            return ok;
        }

        public void Reset()
        {
            _elements = null;
            _status = null;
        }

        public void Dispose() => Reset();
    }
}
#endif
