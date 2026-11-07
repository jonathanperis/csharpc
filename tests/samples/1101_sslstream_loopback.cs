// SslStream over libssl on Linux (docs/adr/0022-tls.md): TLS client and server on loopback TcpListener / TcpClient sockets
// in the same program, with a fixed self-signed certificate loaded from PKCS#12. Handshake with an accepting validation
// callback (its arguments: policy errors, chain, sender), the stream properties on both ends, data round trips (small,
// large, byte-wise, synchronous and asynchronous), the close-notify end of stream, TLS 1.2 and 1.3 selection, mutual
// authentication, ALPN, and the state before authentication. Only protocol-independent facts are printed.
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

const string PfxBase64 = "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";
X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(PfxBase64), "csharpc");
RemoteCertificateValidationCallback acceptAll = (sender, certificate, chain, errors) => true;

// Runs a server and a client against each other and prints what each reported, in a fixed order.
async Task Pair(string name, Func<SslStream, Task<string>> server, Func<SslStream, Task<string>> client, bool callbackInConstructor = false)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task<string> serverTask = Task.Run(async () =>
    {
        using TcpClient tcp = await listener.AcceptTcpClientAsync();
        using var ssl = new SslStream(tcp.GetStream(), false, callbackInConstructor ? acceptAll : null);
        try { return await server(ssl); }
        catch (Exception e) { return "failed: " + e.GetType().Name + ": " + e.Message; }
    });
    string clientResult;
    using (var tcp = new TcpClient())
    {
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        using var ssl = new SslStream(tcp.GetStream(), false, callbackInConstructor ? acceptAll : null);
        try { clientResult = await client(ssl); }
        catch (Exception e) { clientResult = "failed: " + e.GetType().Name + ": " + e.Message; }
    }
    string serverResult = await serverTask;
    listener.Stop();
    Console.WriteLine("== " + name);
    Console.WriteLine("  server: " + serverResult);
    Console.WriteLine("  client: " + clientResult);
}

string Facts(SslStream s) =>
    $"auth={s.IsAuthenticated} enc={s.IsEncrypted} signed={s.IsSigned} server={s.IsServer} mutual={s.IsMutuallyAuthenticated} tls12+={s.SslProtocol >= SslProtocols.Tls12} read={s.CanRead} write={s.CanWrite} seek={s.CanSeek}";

static long Checksum(byte[] data, int count)
{
    long sum = 0;
    for (int i = 0; i < count; i++) sum = (sum * 31 + data[i]) % 1_000_000_007;
    return sum;
}

// 1. a plain handshake and an echo
await Pair("handshake and echo",
    async ssl =>
    {
        await ssl.AuthenticateAsServerAsync(cert);
        string facts = Facts(ssl) + " local=" + ((X509Certificate2)ssl.LocalCertificate!).Subject + " remote=" + (ssl.RemoteCertificate == null) + " alpn=[" + ssl.NegotiatedApplicationProtocol + "] host=" + ssl.TargetHostName + " cipher known=" + ((int)ssl.NegotiatedCipherSuite != 0);
        var buffer = new byte[64];
        int n = await ssl.ReadAsync(buffer);
        await ssl.WriteAsync(Encoding.UTF8.GetBytes("echo:" + Encoding.UTF8.GetString(buffer, 0, n).ToUpperInvariant()));
        await ssl.FlushAsync();
        n = await ssl.ReadAsync(buffer);
        return facts + " end-of-stream=" + n;
    },
    async ssl =>
    {
        string callbackFacts = "";
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
            {
                callbackFacts = $"callback: sender={sender!.GetType().Name} cert={certificate!.Subject} errors={errors} chain={chain!.ChainElements.Count}/{chain.ChainStatus.Length} status={chain.ChainStatus[0].Status}";
                return true;
            },
        };
        await ssl.AuthenticateAsClientAsync(options);
        string facts = Facts(ssl) + " remote=" + ((X509Certificate2)ssl.RemoteCertificate!).Subject + " local=" + (ssl.LocalCertificate == null) + " host=" + ssl.TargetHostName + " alpn=[" + ssl.NegotiatedApplicationProtocol + "]";
        await ssl.WriteAsync(Encoding.UTF8.GetBytes("hello tls"));
        var buffer = new byte[64];
        int n = await ssl.ReadAsync(buffer);
        string echoed = Encoding.UTF8.GetString(buffer, 0, n);
        try { _ = ssl.Length; } catch (NotSupportedException e) { echoed += " | " + e.Message; }
        try { ssl.Seek(0, SeekOrigin.Begin); } catch (NotSupportedException e) { echoed += " | " + e.Message; }
        return facts + "\n    " + callbackFacts + "\n    " + echoed;
    });

// 2. synchronous API, byte-wise I/O and a large transfer in both directions
var payload = new byte[1_000_000];
for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7 + (i >> 8));
await Pair("synchronous and large",
    ssl => Task.Run(() =>
    {
        ssl.AuthenticateAsServer(cert, false, SslProtocols.None, false);
        int first = ssl.ReadByte();
        var data = new byte[payload.Length];
        int total = 0;
        while (total < data.Length)
        {
            int n = ssl.Read(data, total, data.Length - total);
            if (n == 0) break;
            total += n;
        }
        ssl.Write(data, 0, total);
        ssl.WriteByte(255);
        ssl.Flush();
        return $"first={first} received={total} sum={Checksum(data, total)}";
    }),
    ssl => Task.Run(() =>
    {
        ssl.AuthenticateAsClient("localhost");
        ssl.WriteByte(7);
        ssl.Write(payload);
        ssl.Flush();
        var back = new byte[payload.Length];
        int total = 0;
        while (total < back.Length)
        {
            int n = ssl.Read(back, total, back.Length - total);
            if (n == 0) break;
            total += n;
        }
        int last = ssl.ReadByte();
        return $"echoed={total} equal={back.AsSpan().SequenceEqual(payload)} sum={Checksum(back, total)} last={last}";
    }), callbackInConstructor: true);

// 3. TLS 1.2 only, then TLS 1.3 only (macOS: .NET's SecureTransport has no TLS 1.3 and fails the handshake; csharpc's OpenSSL
// backend negotiates it, a superset the sample does not compare)
foreach (var protocol in OperatingSystem.IsMacOS() ? new[] { SslProtocols.Tls12 } : new[] { SslProtocols.Tls12, SslProtocols.Tls13 })
{
    await Pair("only " + protocol,
        async ssl =>
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert, EnabledSslProtocols = protocol });
            return "protocol=" + ssl.SslProtocol;
        },
        async ssl =>
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = protocol, RemoteCertificateValidationCallback = acceptAll });
            return "protocol=" + ssl.SslProtocol;
        });
}

// 4. mutual authentication
await Pair("mutual authentication",
    async ssl =>
    {
        string seen = "";
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = cert,
            ClientCertificateRequired = true,
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => { seen = $"cert={certificate!.Subject} errors={errors}"; return true; },
        });
        return $"{seen} mutual={ssl.IsMutuallyAuthenticated} remote={((X509Certificate2)ssl.RemoteCertificate!).Subject}";
    },
    async ssl =>
    {
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            ClientCertificates = new X509CertificateCollection { cert },
            RemoteCertificateValidationCallback = acceptAll,
        });
        return $"mutual={ssl.IsMutuallyAuthenticated} local={((X509Certificate2)ssl.LocalCertificate!).Subject}";
    });

// 5. ALPN
await Pair("application protocol negotiation",
    async ssl =>
    {
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = cert,
            ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2, SslApplicationProtocol.Http11 },
        });
        return "negotiated=" + ssl.NegotiatedApplicationProtocol;
    },
    async ssl =>
    {
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http11, new SslApplicationProtocol("other") },
            RemoteCertificateValidationCallback = acceptAll,
        });
        return "negotiated=" + ssl.NegotiatedApplicationProtocol + " equals=" + (ssl.NegotiatedApplicationProtocol == SslApplicationProtocol.Http11);
    });

// 6. a stream that is not authenticated
await Pair("not authenticated",
    async ssl => { await Task.Delay(50); return "idle"; },
    async ssl =>
    {
        var lines = new List<string>();
        void Check(string what, Action action)
        {
            try { action(); lines.Add(what + ": no exception"); }
            catch (Exception e) { lines.Add(what + ": " + e.GetType().Name + ": " + e.Message); }
        }
        Check("write", () => ssl.WriteByte(1));
        Check("read", () => ssl.ReadByte());
        Check("protocol", () => _ = ssl.SslProtocol);
        Check("remote certificate", () => _ = ssl.RemoteCertificate);
        Check("local certificate", () => _ = ssl.LocalCertificate);
        Check("negotiated protocol", () => _ = ssl.NegotiatedApplicationProtocol);
        lines.Add($"flags auth={ssl.IsAuthenticated} enc={ssl.IsEncrypted} signed={ssl.IsSigned} server={ssl.IsServer} mutual={ssl.IsMutuallyAuthenticated} read={ssl.CanRead} write={ssl.CanWrite} seek={ssl.CanSeek} leaveOpen={ssl.LeaveInnerStreamOpen}");
        return string.Join("\n    ", lines);
    });

// 7. a graceful shutdown: the peer's close_notify is the end of the stream
await Pair("shutdown",
    async ssl =>
    {
        await ssl.AuthenticateAsServerAsync(cert);
        var buffer = new byte[16];
        int n = await ssl.ReadAsync(buffer);
        int end = await ssl.ReadAsync(buffer);
        return $"read={n} then={end}";
    },
    async ssl =>
    {
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", RemoteCertificateValidationCallback = acceptAll });
        await ssl.WriteAsync(new byte[] { 1, 2, 3 });
        await ssl.ShutdownAsync();
        return "shutdown write=" + ssl.CanWrite;
    });
