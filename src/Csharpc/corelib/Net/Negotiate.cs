// System.Net.Security.NegotiateAuthentication: NTLM and Negotiate (Kerberos) go through the platform's GSSAPI/SSPI,
// which csharpc doesn't bind yet. HttpClient's handler reaches these types when a server answers 401 with a
// "Negotiate" or "NTLM" challenge and the request carries credentials for that scheme: creating the context
// is an explicit NotSupportedException (Basic and Digest are managed code and work).
#nullable disable
using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication.ExtendedProtection;
using System.Security.Principal;

namespace CsharpcCoreLib
{
    /// <summary>
    /// The current user's credentials are the ticket cache's, which needs the GSSAPI binding this file lacks (and
    /// NetworkCredential itself is not implemented yet): an explicit NotSupportedException, so that code reaching the
    /// property compiles instead of stopping the build.
    /// </summary>
    [ShadowMembers("System.Net.CredentialCache")]
    internal static class CredentialCacheMembers
    {
        public static NetworkCredential DefaultNetworkCredentials => throw Tls.NotSupported("CredentialCache.DefaultNetworkCredentials (the current user's credentials)");
        public static ICredentials DefaultCredentials => throw Tls.NotSupported("CredentialCache.DefaultCredentials (the current user's credentials)");
    }

    [Shadows("System.Net.Security.NegotiateAuthenticationClientOptions")]
    public class NegotiateAuthenticationClientOptionsImpl
    {
        private NetworkCredential _credential;
        private bool _credentialSet;

        public string Package { get; set; } = "Negotiate";
        // Defaults to the current user's credentials, read when asked for (an options object can be made and used with its
        // own credential without them).
        public NetworkCredential Credential
        {
            get => _credentialSet ? _credential : CredentialCache.DefaultNetworkCredentials;
            set { _credential = value; _credentialSet = true; }
        }
        public string TargetName { get; set; }
        public ChannelBinding Binding { get; set; }
        public ProtectionLevel RequiredProtectionLevel { get; set; } = ProtectionLevel.None;
        public bool RequireMutualAuthentication { get; set; }
        public TokenImpersonationLevel AllowedImpersonationLevel { get; set; } = TokenImpersonationLevel.Identification;
    }

    [Shadows("System.Net.Security.NegotiateAuthentication")]
    public sealed class NegotiateAuthenticationImpl : IDisposable
    {
        public NegotiateAuthenticationImpl(NegotiateAuthenticationClientOptions clientOptions) =>
            throw new NotSupportedException("csharpc: NTLM and Negotiate (Kerberos) authentication (System.Net.Security.NegotiateAuthentication) are not supported yet.");

        public NegotiateAuthenticationImpl(NegotiateAuthenticationServerOptions serverOptions) =>
            throw new NotSupportedException("csharpc: NTLM and Negotiate (Kerberos) authentication (System.Net.Security.NegotiateAuthentication) are not supported yet.");

        // The constructors throw, so no instance exists and none of these runs: they are here so that code which
        // uses them (Npgsql's GSS / SSPI authentication, HttpClient's Negotiate handler) compiles.
        public bool IsAuthenticated => false;
        public bool IsEncrypted => false;
        public bool IsSigned => false;
        public bool IsServer => false;
        public bool IsMutuallyAuthenticated => false;
        public string Package => throw NotSupported();
        public string TargetName => throw NotSupported();
        public ProtectionLevel ProtectionLevel => throw NotSupported();
        public TokenImpersonationLevel ImpersonationLevel => throw NotSupported();
        public IIdentity RemoteIdentity => throw NotSupported();
        public string GetOutgoingBlob(string incomingBlob, out NegotiateAuthenticationStatusCode statusCode) => throw NotSupported();
        public byte[] GetOutgoingBlob(ReadOnlySpan<byte> incomingBlob, out NegotiateAuthenticationStatusCode statusCode) => throw NotSupported();
        public NegotiateAuthenticationStatusCode Wrap(ReadOnlySpan<byte> input, System.Buffers.IBufferWriter<byte> outputWriter, bool requestEncryption, out bool isEncrypted) => throw NotSupported();
        public NegotiateAuthenticationStatusCode Unwrap(ReadOnlySpan<byte> input, System.Buffers.IBufferWriter<byte> outputWriter, out bool wasEncrypted) => throw NotSupported();
        public NegotiateAuthenticationStatusCode UnwrapInPlace(Span<byte> input, out int unwrappedOffset, out int unwrappedLength, out bool wasEncrypted) => throw NotSupported();
        public void Dispose() { }

        private static NotSupportedException NotSupported() =>
            new NotSupportedException("csharpc: NTLM and Negotiate (Kerberos) authentication (System.Net.Security.NegotiateAuthentication) are not supported yet.");
    }
}
