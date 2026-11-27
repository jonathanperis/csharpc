// SslStream and X.509 from the framework's IL on Linux (docs/il-first/System.Net.Security.md): a CA and leaf certificates made at run
// time with CertificateRequest (ECDSA keys on P-256 and P-384, basic constraints, key usage, EKU, SAN, key identifiers), a chain built against a
// custom root, then TLS on loopback with an ECDSA server certificate sent through SslStreamCertificateContext with its intermediate,
// validated by the client against that custom root (X509ChainPolicy on SslClientAuthenticationOptions), a pinned TLS 1.2 cipher suite
// (CipherSuitesPolicy), a host-name mismatch, an untrusted root, and the server's choice of certificate by SNI. Keys are random: only
// facts that do not depend on them are printed.
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1), notAfter = DateTimeOffset.UtcNow.AddDays(30);

X509Certificate2 MakeCa(string name, AsymmetricAlgorithm key)
{
    CertificateRequest req = key is RSA rsa
        ? new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        : new CertificateRequest(name, (ECDsa)key, HashAlgorithmName.SHA256);
    req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
    req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
    req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
    return req.CreateSelfSigned(notBefore, notAfter);
}

int serial = 1;
X509Certificate2 Issue(X509Certificate2 issuer, string name, ECDsa key, bool ca, params string[] dnsNames)
{
    var req = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
    req.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
    req.CertificateExtensions.Add(new X509KeyUsageExtension(ca ? X509KeyUsageFlags.KeyCertSign : X509KeyUsageFlags.DigitalSignature, true));
    if (!ca) req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
    if (dnsNames.Length > 0)
    {
        var san = new SubjectAlternativeNameBuilder();
        foreach (string dns in dnsNames) san.AddDnsName(dns);
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());
    }
    req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
    req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));
    byte[] serialNumber = [0x10, (byte)serial++];
    using X509Certificate2 cert = req.Create(issuer, notBefore, notAfter, serialNumber);
    return cert.CopyWithPrivateKey(key);
}

using ECDsa caKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
using X509Certificate2 root = MakeCa("CN=csharpc test root, O=csharpc", caKey);
using ECDsa interKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using X509Certificate2 inter = Issue(root, "CN=csharpc test intermediate", interKey, true);
using ECDsa leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using X509Certificate2 leaf = Issue(inter, "CN=localhost", leafKey, false, "localhost", "csharpc.test");
using ECDsa otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
using X509Certificate2 other = Issue(inter, "CN=other.test", otherKey, false, "other.test");

Console.WriteLine("== certificates");
foreach (X509Certificate2 c in new[] { root, inter, leaf, other })
{
    Console.WriteLine($"{c.Subject} <- {c.Issuer}; v{c.Version} key={c.PublicKey.Oid.FriendlyName} sig={c.SignatureAlgorithm.FriendlyName} private={c.HasPrivateKey} serial={(c.Subject == c.Issuer ? "(random)" : c.SerialNumber)}");
    foreach (X509Extension e in c.Extensions)
        Console.WriteLine($"  {e.Oid!.Value} {e.Oid.FriendlyName} critical={e.Critical} {e.GetType().Name}");
}
var sanExt = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
Console.WriteLine("san dns: " + string.Join(",", sanExt.EnumerateDnsNames()) + " ip: " + string.Join(",", sanExt.EnumerateIPAddresses()));
Console.WriteLine($"matches localhost={leaf.MatchesHostname("localhost")} csharpc.test={leaf.MatchesHostname("csharpc.test")} example.com={leaf.MatchesHostname("example.com")}");
Console.WriteLine("issuer key id matches: " + (leaf.Extensions.OfType<X509AuthorityKeyIdentifierExtension>().Single().KeyIdentifier!.Value.Span
    .SequenceEqual(Convert.FromHexString(inter.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single().SubjectKeyIdentifier!))));

Console.WriteLine("== chain");
void BuildChain(string name, X509Certificate2 cert, bool trustRoot, bool withIntermediate)
{
    using var chain = new X509Chain();
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.DisableCertificateDownloads = true;
    if (trustRoot)
    {
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
    }
    if (withIntermediate) chain.ChainPolicy.ExtraStore.Add(inter);
    bool ok = chain.Build(cert);
    Console.WriteLine($"{name}: built={ok} elements={chain.ChainElements.Count} status=[{string.Join(",", chain.ChainStatus.Select(s => s.Status))}]");
    foreach (X509ChainElement el in chain.ChainElements)
        Console.WriteLine($"  {el.Certificate.Subject} [{string.Join(",", el.ChainElementStatus.Select(s => s.Status))}]");
}
BuildChain("trusted", leaf, true, true);
BuildChain("no intermediate", leaf, true, false);
BuildChain("untrusted root", leaf, false, true);

Console.WriteLine("== tls");
SslStreamCertificateContext leafContext = SslStreamCertificateContext.Create(leaf, new X509Certificate2Collection(inter), offline: true);
SslStreamCertificateContext otherContext = SslStreamCertificateContext.Create(other, new X509Certificate2Collection(inter), offline: true);

X509ChainPolicy ClientPolicy(bool trustRoot)
{
    var policy = new X509ChainPolicy { RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true };
    if (trustRoot)
    {
        policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        policy.CustomTrustStore.Add(root);
    }
    return policy;
}

async Task Run(string name, string targetHost, bool trustRoot, SslProtocols protocols = SslProtocols.None, CipherSuitesPolicy? ciphers = null,
    bool acceptAnyway = false)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    string? sniSeen = null;
    Task<string> serverTask = Task.Run(async () =>
    {
        using TcpClient tcp = await listener.AcceptTcpClientAsync();
        using var ssl = new SslStream(tcp.GetStream());
        try
        {
            await ssl.AuthenticateAsServerAsync((stream, hello, state, ct) =>
            {
                sniSeen = hello.ServerName;
                return ValueTask.FromResult(new SslServerAuthenticationOptions
                {
                    ServerCertificateContext = hello.ServerName == "other.test" ? otherContext : leafContext,
                    EnabledSslProtocols = protocols,
                    CipherSuitesPolicy = ciphers,
                });
            }, null);
            var buffer = new byte[64];
            int n = await ssl.ReadAsync(buffer);
            await ssl.WriteAsync(Encoding.UTF8.GetBytes("echo:" + Encoding.UTF8.GetString(buffer, 0, n)));
            await ssl.ShutdownAsync();
            return $"ok, cipher {ssl.NegotiatedCipherSuite}, target host '{ssl.TargetHostName}'";
        }
        catch (Exception e) { return "failed: " + e.GetType().Name; }
    });
    string clientResult;
    using (var tcp = new TcpClient())
    {
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        using var ssl = new SslStream(tcp.GetStream());
        SslPolicyErrors seen = SslPolicyErrors.None;
        string chainSeen = "";
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = protocols,
                CertificateChainPolicy = ClientPolicy(trustRoot),
                CipherSuitesPolicy = ciphers,
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    seen = errors;
                    chainSeen = string.Join(" / ", chain!.ChainElements.Select(e => e.Certificate.GetNameInfo(X509NameType.SimpleName, false)));
                    return errors == SslPolicyErrors.None || acceptAnyway;
                },
            });
            await ssl.WriteAsync(Encoding.UTF8.GetBytes(name));
            var buffer = new byte[64];
            int total = 0, n;
            while ((n = await ssl.ReadAsync(buffer.AsMemory(total))) > 0) total += n;
            clientResult = $"ok, errors={seen}, chain={chainSeen}, remote={ssl.RemoteCertificate?.Subject}, protocol tls12+={ssl.SslProtocol >= SslProtocols.Tls12}, "
                + $"cipher {(ciphers is null ? "default" : ssl.NegotiatedCipherSuite.ToString())}, read '{Encoding.UTF8.GetString(buffer, 0, total)}'";
        }
        catch (AuthenticationException e) { clientResult = $"rejected: errors={seen}, chain={chainSeen}: {e.GetType().Name}: {e.Message}"; }
        catch (Exception e) { clientResult = "failed: " + e.GetType().Name + ": " + e.Message; }
    }
    string serverResult = await serverTask;
    listener.Stop();
    Console.WriteLine("-- " + name);
    Console.WriteLine("  sni: " + (sniSeen ?? "(none)"));
    Console.WriteLine("  server: " + serverResult);
    Console.WriteLine("  client: " + clientResult);
}

await Run("trusted", "localhost", true);
await Run("second san name", "csharpc.test", true);
await Run("sni picks the other certificate", "other.test", true);
await Run("tls12 pinned suite", "localhost", true, SslProtocols.Tls12, new CipherSuitesPolicy([TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256]));
await Run("tls13 pinned suite", "localhost", true, SslProtocols.Tls13, new CipherSuitesPolicy([TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256]));
await Run("name mismatch", "example.com", true);
await Run("untrusted root", "localhost", false);
await Run("untrusted root accepted by the callback", "localhost", false, acceptAnyway: true);
