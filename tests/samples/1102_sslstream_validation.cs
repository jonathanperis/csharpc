// SslStream certificate validation and handshake failures on Linux (docs/adr/0022-tls.md): the exception types and messages
// of a rejected certificate (untrusted root without a callback, a callback returning false, a host name mismatch, trusted
// through a custom root and chain policy), protocol mismatches, garbage and plain-text peers, a peer that hangs up, a
// missing client certificate, and invalid server certificates. Server-side results of failures the client causes are not
// printed (they depend on whether the alert or the close reaches the server first).
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

const string PfxBase64 = "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";
X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(PfxBase64), "csharpc");

static string Describe(Exception e)
{
    string text = e.GetType().Name + ": " + e.Message;
    if (e.InnerException != null) text += " / " + (e.InnerException is AuthenticationException ? "AuthenticationException" : "inner") + ": " + e.InnerException.Message;
    return text;
}

// server: given the raw stream and the SslStream; client: likewise. Either may return a result string; null results are not printed.
async Task Pair(string name, Func<SslStream, Stream, Task<string>> server, Func<SslStream, Stream, Task<string>> client, bool printServer, bool printClient = true)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task<string> serverTask = Task.Run(async () =>
    {
        using TcpClient tcp = await listener.AcceptTcpClientAsync();
        Stream raw = tcp.GetStream();
        using var ssl = new SslStream(raw, false);
        try { return await server(ssl, raw); }
        catch (Exception e) { return "failed: " + Describe(e); }
    });
    string clientResult;
    using (var tcp = new TcpClient())
    {
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        Stream raw = tcp.GetStream();
        using var ssl = new SslStream(raw, false);
        try { clientResult = await client(ssl, raw); }
        catch (Exception e) { clientResult = "failed: " + Describe(e); }
    }
    string serverResult = await Task.WhenAny(serverTask, Task.Delay(5000)) == serverTask ? serverTask.Result : "timeout";
    listener.Stop();
    Console.WriteLine("== " + name);
    if (printServer) Console.WriteLine("  server: " + serverResult);
    if (printClient) Console.WriteLine("  client: " + clientResult);
}

Task<string> AcceptingServer(SslStream ssl, Stream raw) => ServeOnce(ssl);
async Task<string> ServeOnce(SslStream ssl)
{
    await ssl.AuthenticateAsServerAsync(cert);
    var buffer = new byte[8];
    int n = await ssl.ReadAsync(buffer);
    return "read " + n;
}

var trustCert = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
trustCert.CustomTrustStore.Add(X509CertificateLoader.LoadCertificate(cert.RawData));

// 1. the certificate is self-signed: no callback, no trust
await Pair("untrusted root", AcceptingServer,
    async (ssl, raw) => { await ssl.AuthenticateAsClientAsync("localhost"); return "authenticated"; }, printServer: false);

// 2. a callback that says no, and what it was shown
await Pair("callback returns false", AcceptingServer,
    async (ssl, raw) =>
    {
        string shown = "";
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (s, c, chain, errors) => { shown = $"errors={errors} status={string.Join(",", chain!.ChainStatus.Select(x => x.Status))}"; return false; },
            });
            return "authenticated";
        }
        catch (Exception e) { return Describe(e) + " | " + shown; }
    }, printServer: false);

// 3. a callback that says yes to a certificate for another host
await Pair("host name mismatch accepted by callback", AcceptingServer,
    async (ssl, raw) =>
    {
        string shown = "";
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "example.com",
            RemoteCertificateValidationCallback = (s, c, chain, errors) => { shown = "errors=" + errors; return true; },
        });
        return "authenticated " + shown;
    }, printServer: true);

// 4. trust through the chain policy: the right host passes with no callback, the wrong one fails with a policy error
await Pair("custom root trust, right host", AcceptingServer,
    async (ssl, raw) =>
    {
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", CertificateChainPolicy = trustCert });
        return "authenticated, remote=" + ssl.RemoteCertificate!.Subject;
    }, printServer: true);
await Pair("custom root trust, wrong host", AcceptingServer,
    async (ssl, raw) =>
    {
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "example.com", CertificateChainPolicy = trustCert });
        return "authenticated";
    }, printServer: false);
await Pair("custom root trust, IP address host", AcceptingServer,
    async (ssl, raw) =>
    {
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "127.0.0.1", CertificateChainPolicy = trustCert });
        return "authenticated";
    }, printServer: true);

// 5. no protocol in common
await Pair("protocol mismatch",
    async (ssl, raw) =>
    {
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert, EnabledSslProtocols = SslProtocols.Tls13 });
        return "authenticated";
    },
    async (ssl, raw) =>
    {
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12, RemoteCertificateValidationCallback = (s, c, ch, e) => true });
        return "authenticated";
    }, printServer: false);

// 6. peers that don't speak TLS
await Pair("plain text sent to a TLS server", AcceptingServer,
    async (ssl, raw) =>
    {
        await raw.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n"));
        await raw.FlushAsync();
        await Task.Delay(500);
        return "sent";
    }, printServer: true);
await Pair("a TLS client talking to an HTTP server",
    async (ssl, raw) =>
    {
        await raw.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"));
        await raw.FlushAsync();
        await Task.Delay(500);
        return "sent";
    },
    async (ssl, raw) => { await ssl.AuthenticateAsClientAsync("localhost"); return "authenticated"; }, printServer: false);
await Pair("server hangs up",
    async (ssl, raw) => { _ = await raw.ReadAsync(new byte[4096]); raw.Close(); return "closed"; }, // reads the ClientHello first: closing with unread data would reset the connection
    async (ssl, raw) => { await ssl.AuthenticateAsClientAsync("localhost"); return "authenticated"; }, printServer: false);

// 7. a client certificate is required and the client has none
await Pair("client certificate required but missing",
    async (ssl, raw) =>
    {
        string seen = "";
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = cert,
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (s, c, chain, errors) => { seen = $"callback: certificate={(c == null ? "none" : "present")} chain={(chain == null ? "none" : "present")} errors={errors}"; return false; },
            });
            return "authenticated";
        }
        catch (Exception e) { return Describe(e) + " | " + seen; }
    },
    async (ssl, raw) =>
    {
        try { await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", RemoteCertificateValidationCallback = (s, c, ch, e) => true }); }
        catch (Exception) { }
        return "done";
    }, printServer: true, printClient: false);

// 8. server certificates that can't be used
await Pair("invalid server certificates", (ssl, raw) => Task.FromResult("unused"),
    async (ssl, raw) =>
    {
        var lines = new List<string>();
        async Task Try(string what, Func<SslStream, Task> run)
        {
            using var fresh = new SslStream(new MemoryStream(), false);
            try { await run(fresh); lines.Add(what + ": no exception"); }
            catch (Exception e) { lines.Add(what + ": " + Describe(e)); }
        }
        await Try("null certificate", s => s.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = null }));
        await Try("certificate without a key", s => s.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = X509CertificateLoader.LoadCertificate(cert.RawData) }));
        await Try("null options", s => s.AuthenticateAsServerAsync((SslServerAuthenticationOptions)null!));
        await Try("null client options", s => s.AuthenticateAsClientAsync((SslClientAuthenticationOptions)null!));
        return string.Join("\n    ", lines);
    }, printServer: false);

// 9. callbacks given twice
await Pair("callback set twice", (ssl, raw) => Task.FromResult("unused"),
    async (ssl, raw) =>
    {
        using var twice = new SslStream(new MemoryStream(), false, (s, c, ch, e) => true);
        try { await twice.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "x", RemoteCertificateValidationCallback = (s, c, ch, e) => true }); return "no exception"; }
        catch (Exception e) { return Describe(e); }
    }, printServer: false);
