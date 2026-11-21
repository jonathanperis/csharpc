// SslStream over loopback, the program behind the measurements of docs/il-first/System.Net.Security.md (tools/il-first/measure-aot.sh):
// full TLS 1.3 and TLS 1.2 handshakes (the fixed self-signed RSA certificate of sample 1101, from PKCS#12, the client validating with a callback), then bulk
// transfer through one connection (16 KB writes, the record-sized path) and small-message ping-pong (the per-record cost). The output is
// checksums only.
using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

// The certificate of tests/samples/1101_sslstream_loopback.cs.
const string PfxBase64 = "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";
using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(PfxBase64), "csharpc");
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
long check = 0;

async Task<(SslStream client, SslStream server)> Connect(SslProtocols protocols)
{
    Task<TcpClient> accept = listener.AcceptTcpClientAsync();
    var tcp = new TcpClient { NoDelay = true };
    await tcp.ConnectAsync(IPAddress.Loopback, port);
    TcpClient serverTcp = await accept;
    serverTcp.NoDelay = true;
    var server = new SslStream(serverTcp.GetStream(), false);
    var client = new SslStream(tcp.GetStream(), false);
    Task serverAuth = server.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert, EnabledSslProtocols = protocols });
    await client.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = protocols,
        RemoteCertificateValidationCallback = (s, c, ch, e) => c is not null });
    await serverAuth;
    return (client, server);
}

// 1. Handshakes: 200 of each protocol.
foreach (SslProtocols protocol in new[] { SslProtocols.Tls13, SslProtocols.Tls12 })
    for (int i = 0; i < 200; i++)
    {
        var (client, server) = await Connect(protocol);
        check += (int)client.SslProtocol;
        client.Dispose();
        server.Dispose();
    }

// 2. Bulk: 256 MB in 16 KB writes, and 3. ping-pong: 20 000 round trips of 64 bytes.
{
    var (client, server) = await Connect(SslProtocols.Tls13);
    Task<long> sink = Task.Run(async () =>
    {
        var buffer = new byte[65536];
        long total = 0, sum = 0;
        while (total < 256L << 20)
        {
            int n = await server.ReadAsync(buffer);
            total += n;
            sum += buffer[n - 1];
        }
        return total + sum;
    });
    var chunk = new byte[16384];
    for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)i;
    for (int i = 0; i < (256 << 20) / chunk.Length; i++) await client.WriteAsync(chunk);
    check += await sink;

    Task echo = Task.Run(async () =>
    {
        var buffer = new byte[64];
        for (int i = 0; i < 20000; i++)
        {
            int got = 0;
            while (got < 64) got += await server.ReadAsync(buffer.AsMemory(got));
            await server.WriteAsync(buffer);
        }
    });
    var message = new byte[64];
    var reply = new byte[64];
    for (int i = 0; i < 20000; i++)
    {
        message[0] = (byte)i;
        await client.WriteAsync(message);
        int got = 0;
        while (got < 64) got += await client.ReadAsync(reply.AsMemory(got));
        check += reply[0];
    }
    await echo;
    client.Dispose();
    server.Dispose();
}
listener.Stop();
Console.WriteLine(check);
