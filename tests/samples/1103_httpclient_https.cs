// HttpClient over https (docs/adr/0022-tls.md): SocketsHttpHandler from the shared framework's IL on csharpc's SslStream,
// against an HTTPS server written with SslStream in the same program (loopback, no network): GET and POST, connection reuse
// over one TLS session, the server certificate validation callback (its arguments), DangerousAcceptAnyServerCertificateValidator,
// a custom root trust through SslOptions.CertificateChainPolicy, the exception of an untrusted certificate and of a host name
// mismatch, and a server that doesn't speak TLS.
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

const string PfxBase64 = "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";
X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(PfxBase64), "csharpc");

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
int accepted = 0, handshakes = 0, requests = 0;

async Task Serve()
{
    while (true)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(); }
        catch (Exception) { return; }
        Interlocked.Increment(ref accepted);
        _ = Task.Run(() => Handle(client));
    }
}

async Task Handle(TcpClient client)
{
    using (client)
    using (var ssl = new SslStream(client.GetStream(), false))
    {
        try
        {
            await ssl.AuthenticateAsServerAsync(cert);
            Interlocked.Increment(ref handshakes);
            var pending = new List<byte>();
            var buffer = new byte[4096];
            while (true)
            {
                // the request head
                int end;
                while ((end = IndexOfHeadEnd(pending)) < 0)
                {
                    int n = await ssl.ReadAsync(buffer);
                    if (n == 0) return;
                    pending.AddRange(buffer.AsSpan(0, n).ToArray());
                }
                string head = Encoding.ASCII.GetString(pending.GetRange(0, end).ToArray());
                pending.RemoveRange(0, end + 4);
                string[] lines = head.Split("\r\n");
                string[] request = lines[0].Split(' ');
                int length = 0;
                foreach (string line in lines.Skip(1))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                while (pending.Count < length)
                {
                    int n = await ssl.ReadAsync(buffer);
                    if (n == 0) return;
                    pending.AddRange(buffer.AsSpan(0, n).ToArray());
                }
                string body = Encoding.UTF8.GetString(pending.GetRange(0, length).ToArray());
                pending.RemoveRange(0, length);
                Interlocked.Increment(ref requests);
                string text = request[1] switch
                {
                    "/hello" => "hello over tls",
                    "/echo" => "echo:" + body.ToUpperInvariant(),
                    "/big" => new string('x', 200_000),
                    _ => "unknown " + request[1],
                };
                byte[] payload = Encoding.UTF8.GetBytes(text);
                string response = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {payload.Length}\r\nX-Method: {request[0]}\r\n\r\n";
                await ssl.WriteAsync(Encoding.ASCII.GetBytes(response));
                await ssl.WriteAsync(payload);
                await ssl.FlushAsync();
            }
        }
        catch (Exception) { }
    }
}

static int IndexOfHeadEnd(List<byte> bytes)
{
    for (int i = 0; i + 3 < bytes.Count; i++)
        if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n') return i;
    return -1;
}

Task server = Serve();
// 127.0.0.1, not localhost: csharpc's Socket.ConnectAsync(DnsEndPoint) can't yet fall back from ::1 to 127.0.0.1 (the certificate names both).
string baseUrl = $"https://127.0.0.1:{port}";

// The failing connections come first: .NET resumes the TLS session of a client without a validation callback, and a
// resumed session is validated after the handshake (a differently wrapped exception); csharpc never resumes.
// 4. no trust: the default validation rejects the self-signed certificate (resumption off: .NET resumes the sessions of
// clients without a callback, which moves the check after the handshake and changes the exception's wrapping)
using (var strict = new HttpClient(new SocketsHttpHandler { SslOptions = new SslClientAuthenticationOptions { AllowTlsResume = false } }))
{
    try { await strict.GetStringAsync(baseUrl + "/hello"); Console.WriteLine("untrusted: ok"); }
    catch (Exception e) { Console.WriteLine("untrusted: " + Describe(e)); }
}

// 5. the callback refuses
using (var refusing = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (r, c, ch, e) => false }))
{
    try { await refusing.GetStringAsync(baseUrl + "/hello"); Console.WriteLine("refused: ok"); }
    catch (Exception e) { Console.WriteLine("refused: " + Describe(e)); }
}

// 6. a certificate for another host name (the Host header is the name TLS checks)
using (var mismatch = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (r, c, ch, e) => { Console.WriteLine("mismatch callback errors: " + e); return false; } }))
{
    var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/hello");
    request.Headers.Host = "example.test";
    try { await mismatch.SendAsync(request); }
    catch (HttpRequestException e) { Console.WriteLine("other host: " + Describe(e)); }
}

// 1. the callback decides: GET and POST over one connection
int callbackCalls = 0;
string callbackFacts = "";
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
    {
        callbackCalls++;
        callbackFacts = $"request={request.RequestUri!.Host} cert={certificate!.Subject} errors={errors} chain={chain!.ChainElements.Count}";
        return true;
    },
};
using (var client = new HttpClient(handler))
{
    Console.WriteLine("GET: " + await client.GetStringAsync(baseUrl + "/hello"));
    var post = await client.PostAsync(baseUrl + "/echo", new StringContent("secure payload"));
    Console.WriteLine($"POST: {(int)post.StatusCode} {await post.Content.ReadAsStringAsync()} method={post.Headers.GetValues("X-Method").First()} version={post.Version}");
    Console.WriteLine("big: " + (await client.GetStringAsync(baseUrl + "/big")).Length);
    Console.WriteLine("callback: calls=" + callbackCalls + " " + callbackFacts);
    Console.WriteLine($"connections: accepted={accepted} handshakes={handshakes} requests={requests}");
}

// 2. accept anything
using (var client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }))
    Console.WriteLine("any certificate: " + await client.GetStringAsync(baseUrl + "/hello"));

// 3. trust the certificate through the chain policy: no callback, the host name (here the IP address) is checked
var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
policy.CustomTrustStore.Add(X509CertificateLoader.LoadCertificate(cert.RawData));
using (var trusting = new HttpClient(new SocketsHttpHandler { SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy } }))
{
    Console.WriteLine("trusted through the chain policy: " + await trusting.GetStringAsync(baseUrl + "/hello"));
}

listener.Stop();

// 7. a plain HTTP server on the https port
var plain = new TcpListener(IPAddress.Loopback, 0);
plain.Start();
int plainPort = ((IPEndPoint)plain.LocalEndpoint).Port;
_ = Task.Run(async () =>
{
    using var tcp = await plain.AcceptTcpClientAsync();
    var stream = tcp.GetStream();
    _ = await stream.ReadAsync(new byte[4096]);
    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nhi"));
    await Task.Delay(300);
});
using (var client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }))
{
    try { await client.GetStringAsync($"https://127.0.0.1:{plainPort}/"); Console.WriteLine("plain server: ok"); }
    catch (Exception e) { Console.WriteLine("plain server: " + Describe(e)); }
}
plain.Stop();

static string Describe(Exception e)
{
    string text = e.GetType().Name + ": " + e.Message;
    for (Exception? inner = e.InnerException; inner != null; inner = inner.InnerException)
        text += " / " + inner.GetType().Name + ": " + inner.Message;
    return text;
}
