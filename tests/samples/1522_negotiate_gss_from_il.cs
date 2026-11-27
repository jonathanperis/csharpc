// NegotiateAuthentication from the framework's IL on Linux over the GSS-API contract (corelib/Net/NetSecurityNative.cs, libgssapi_krb5
// loaded at run time): client contexts for Negotiate and Kerberos with a service principal and no Kerberos ticket (no KDC is reachable,
// so the first token fails with the status .NET reports), a server context fed a malformed token, and the members' state around them.
// KRB5 configuration is pointed at an empty file so the result does not depend on the machine's realm.
using System.Net;
using System.Net.Security;

string conf = Path.Combine(Path.GetTempPath(), "csharpc-1522-krb5.conf");
File.WriteAllText(conf, "");
Environment.SetEnvironmentVariable("KRB5_CONFIG", conf);
Environment.SetEnvironmentVariable("KRB5CCNAME", "FILE:" + Path.Combine(Path.GetTempPath(), "csharpc-1522-no-such-cache"));
Environment.SetEnvironmentVariable("KRB5_KTNAME", "FILE:" + Path.Combine(Path.GetTempPath(), "csharpc-1522-no-such-keytab"));

void Client(string package, string target, NetworkCredential? credential)
{
    var options = new NegotiateAuthenticationClientOptions { Package = package, TargetName = target };
    if (credential is not null) options.Credential = credential;
    using var auth = new NegotiateAuthentication(options);
    Console.WriteLine($"{package} {target}: package={auth.Package} server={auth.IsServer} authenticated={auth.IsAuthenticated} target={auth.TargetName}");
    byte[]? blob = auth.GetOutgoingBlob(ReadOnlySpan<byte>.Empty, out NegotiateAuthenticationStatusCode status);
    Console.WriteLine($"  first token: status={status} blob={(blob is null ? "null" : "bytes")} authenticated={auth.IsAuthenticated}");
}

Client("Kerberos", "HTTP/csharpc.invalid", null);
Client("Kerberos", "HTTP/csharpc.invalid@NO.SUCH.REALM", null);
Client("Negotiate", "HTTP/csharpc.invalid", null);

using (var server = new NegotiateAuthentication(new NegotiateAuthenticationServerOptions { Package = "Negotiate" }))
{
    byte[]? blob = server.GetOutgoingBlob(new byte[] { 0x60, 0x03, 0x06, 0x01, 0x00 }, out NegotiateAuthenticationStatusCode status);
    Console.WriteLine($"server malformed token: status={status} blob={(blob is null ? "null" : "bytes")} authenticated={server.IsAuthenticated} server={server.IsServer}");
}
File.Delete(conf);
