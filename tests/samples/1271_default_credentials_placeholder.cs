// CredentialCache.DefaultCredentials / DefaultNetworkCredentials are placeholders (empty user name, password and domain, the
// same object each time) that fail only when something authenticates with them; reading them and creating an HttpClient,
// an HttpClientHandler with UseDefaultCredentials, and NegotiateAuthenticationClientOptions (whose Credential defaults to
// them) must not throw.
using System.Net;
using System.Net.Http;
using System.Net.Security;

ICredentials credentials = CredentialCache.DefaultCredentials;
NetworkCredential network = CredentialCache.DefaultNetworkCredentials;
Console.WriteLine($"same object: {ReferenceEquals(credentials, network)} {ReferenceEquals(CredentialCache.DefaultCredentials, credentials)}");
Console.WriteLine($"members: '{network.UserName}' '{network.Password}' '{network.Domain}'");
Console.WriteLine($"type: {credentials is NetworkCredential}");
NetworkCredential? fromUri = credentials.GetCredential(new Uri("http://example.com/"), "Negotiate");
Console.WriteLine($"GetCredential: {ReferenceEquals(fromUri, network)}");

using var client = new HttpClient();
Console.WriteLine($"client: {client.Timeout.TotalSeconds}");
using var handler = new HttpClientHandler { UseDefaultCredentials = true };
Console.WriteLine($"handler: {handler.UseDefaultCredentials} {handler.Credentials == null} {handler.PreAuthenticate}");
handler.UseDefaultCredentials = false;
Console.WriteLine($"handler off: {handler.UseDefaultCredentials}");
handler.Credentials = new NetworkCredential("user", "secret", "domain");
Console.WriteLine($"explicit: {((NetworkCredential)handler.Credentials).UserName} {((NetworkCredential)handler.Credentials).Domain}");
using var sockets = new SocketsHttpHandler { Credentials = CredentialCache.DefaultCredentials };
Console.WriteLine($"sockets handler: {ReferenceEquals(sockets.Credentials, credentials)}");

var options = new NegotiateAuthenticationClientOptions { TargetName = "host/example.com" };
Console.WriteLine($"options: {options.Package} {options.TargetName} {ReferenceEquals(options.Credential, network)}");
options.Credential = new NetworkCredential("a", "b");
Console.WriteLine($"options credential set: {options.Credential.UserName}");
