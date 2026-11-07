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
    [Shadows("System.Net.Security.NegotiateAuthenticationClientOptions")]
    public class NegotiateAuthenticationClientOptionsImpl
    {
        public string Package { get; set; } = "Negotiate";
        public NetworkCredential Credential { get; set; } = CredentialCache.DefaultNetworkCredentials;
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

        public bool IsAuthenticated => false;
        public string GetOutgoingBlob(string incomingBlob, out NegotiateAuthenticationStatusCode statusCode) => throw NotSupported();
        public void Dispose() { }

        private static NotSupportedException NotSupported() =>
            new NotSupportedException("csharpc: NTLM and Negotiate (Kerberos) authentication (System.Net.Security.NegotiateAuthentication) are not supported yet.");
    }
}
