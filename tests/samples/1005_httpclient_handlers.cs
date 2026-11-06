// HttpClientHandler / SocketsHttpHandler features over plain HTTP/1.1 against a loopback server: cookies
// (Set-Cookie into a CookieContainer and back out as a Cookie header), automatic redirects (absolute Location, the
// redirect limit, AllowAutoRedirect = false), gzip / deflate response decompression, Basic authentication with
// NetworkCredential, handler properties, and a custom DelegatingHandler pipeline that needs no network at all.
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
string root = "http://127.0.0.1:" + port;
var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();

async Task Serve()
{
    while (true)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(); }
        catch (Exception) { return; }
        _ = Task.Run(() => Handle(client));
    }
}

async Task Handle(TcpClient client)
{
    using var tcp = client;
    var stream = tcp.GetStream();
    var buffer = new byte[8192];
    var pending = new StringBuilder();
    while (true)
    {
        int end;
        while ((end = pending.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return;
            pending.Append(Encoding.ASCII.GetString(buffer, 0, n));
        }
        var lines = pending.ToString(0, end).Split("\r\n");
        pending.Remove(0, end + 4);
        string path = lines[0].Split(' ')[1];
        string? Header(string name) => lines.Skip(1).Where(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)).Select(l => l[(name.Length + 1)..].Trim()).FirstOrDefault();
        seen.Enqueue(lines[0].Split(' ')[0] + " " + path + " cookie=[" + Header("Cookie") + "] auth=[" + Header("Authorization") + "] accept-encoding=[" + Header("Accept-Encoding") + "]");
        await stream.WriteAsync(path switch
        {
            "/set-cookies" => Raw("200 OK", ["Set-Cookie: session=abc123; Path=/", "Set-Cookie: theme=dark; Path=/; HttpOnly", "Set-Cookie: gone=x; Path=/; Max-Age=0"], "cookies set"),
            "/echo-cookie" => Raw("200 OK", [], "cookie header: " + Header("Cookie")),
            "/hello" => Raw("200 OK", ["Content-Type: text/plain"], "hello"),
            "/redirect" => Raw("302 Found", ["Location: " + root + "/hello"], ""),
            "/redirect-moved" => Raw("301 Moved Permanently", ["Location: " + root + "/redirect"], ""),
            "/loop" => Raw("302 Found", ["Location: " + root + "/loop"], ""),
            "/gzip" => RawBytes("200 OK", ["Content-Encoding: gzip", "Content-Type: text/plain"], Compress("compressed hello hello hello hello", gzip: true)),
            "/deflate" => RawBytes("200 OK", ["Content-Encoding: deflate", "Content-Type: text/plain"], Compress("deflated hello hello hello hello", gzip: false)),
            "/secret" when Header("Authorization") is { } auth && auth == "Basic " + Convert.ToBase64String("user:pass"u8) => Raw("200 OK", [], "welcome user"),
            "/secret" => Raw("401 Unauthorized", ["WWW-Authenticate: Basic realm=\"sample\""], "denied"),
            _ => Raw("404 Not Found", [], "?"),
        });
    }
}

static byte[] Raw(string status, string[] headers, string body) => RawBytes(status, headers, Encoding.UTF8.GetBytes(body));

static byte[] RawBytes(string status, string[] headers, byte[] body)
{
    var head = new StringBuilder("HTTP/1.1 " + status + "\r\n");
    foreach (var h in headers) head.Append(h).Append("\r\n");
    head.Append("Content-Length: ").Append(body.Length).Append("\r\n\r\n");
    return [.. Encoding.ASCII.GetBytes(head.ToString()), .. body];
}

static byte[] Compress(string text, bool gzip)
{
    using var output = new MemoryStream();
    using (Stream z = gzip ? new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true) : new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        z.Write(Encoding.UTF8.GetBytes(text));
    return output.ToArray();
}

void Seen()
{
    while (seen.TryDequeue(out var line)) Console.WriteLine("  server saw: " + line);
}

_ = Serve();

// Cookies.
var jar = new CookieContainer();
using (var http = new HttpClient(new HttpClientHandler { CookieContainer = jar, UseCookies = true }))
{
    Console.WriteLine(await http.GetStringAsync(root + "/set-cookies"));
    foreach (Cookie cookie in jar.GetCookies(new Uri(root)))
        Console.WriteLine("  jar: " + cookie.Name + "=" + cookie.Value + " path " + cookie.Path + " httpOnly " + cookie.HttpOnly + " domain " + cookie.Domain);
    Console.WriteLine(jar.Count + " cookies, header: " + jar.GetCookieHeader(new Uri(root + "/anything")));
    Console.WriteLine(await http.GetStringAsync(root + "/echo-cookie"));
    jar.Add(new Uri(root), new Cookie("manual", "1"));
    Console.WriteLine(await http.GetStringAsync(root + "/echo-cookie"));
}
Seen();
using (var http = new HttpClient(new HttpClientHandler { UseCookies = false }))
{
    Console.WriteLine(await http.GetStringAsync(root + "/set-cookies"));
    using var request = new HttpRequestMessage(HttpMethod.Get, root + "/echo-cookie");
    request.Headers.Add("Cookie", "own=1");
    using var response = await http.SendAsync(request);
    Console.WriteLine(await response.Content.ReadAsStringAsync());
    Console.WriteLine("Set-Cookie headers seen: " + string.Join(" / ", response.Headers.TryGetValues("Set-Cookie", out var values) ? values : []));
}
Seen();

// Redirects.
using (var http = new HttpClient())
{
    Console.WriteLine(await http.GetStringAsync(root + "/redirect"));
    using var response = await http.GetAsync(root + "/redirect-moved");
    Console.WriteLine(response.StatusCode + " " + await response.Content.ReadAsStringAsync() + " final " + response.RequestMessage!.RequestUri!.AbsolutePath);
}
using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
{
    using var response = await http.GetAsync(root + "/redirect");
    Console.WriteLine(response.StatusCode + " -> " + response.Headers.Location!.ToString().Replace(port.ToString(), "PORT"));
}
using (var http = new HttpClient(new HttpClientHandler { MaxAutomaticRedirections = 3 }))
{
    using var response = await http.GetAsync(root + "/loop");
    Console.WriteLine("loop: " + response.StatusCode + " at " + response.RequestMessage!.RequestUri!.AbsolutePath);
}
Seen();

// Decompression.
using (var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }))
{
    Console.WriteLine(await http.GetStringAsync(root + "/gzip"));
    using var response = await http.GetAsync(root + "/deflate");
    Console.WriteLine(await response.Content.ReadAsStringAsync() + " | content encoding header: " + response.Content.Headers.ContentEncoding.Count);
}
using (var http = new HttpClient())
{
    using var response = await http.GetAsync(root + "/gzip");
    Console.WriteLine("not decompressed: " + response.Content.Headers.ContentEncoding.Single() + " " + (await response.Content.ReadAsByteArrayAsync()).Length + " bytes");
}
Seen();

// Basic authentication.
using (var http = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential("user", "pass") }))
    Console.WriteLine(await http.GetStringAsync(root + "/secret"));
using (var http = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential("user", "wrong") }))
{
    using var response = await http.GetAsync(root + "/secret");
    Console.WriteLine(response.StatusCode + " " + response.Headers.WwwAuthenticate);
}
using (var http = new HttpClient())
{
    using var response = await http.GetAsync(root + "/secret");
    Console.WriteLine("no credentials: " + response.StatusCode);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String("user:pass"u8));
    Console.WriteLine("header: " + await http.GetStringAsync(root + "/secret"));
}
Seen();

// Handler properties.
var handler = new HttpClientHandler();
Console.WriteLine($"HttpClientHandler: redirects {handler.AllowAutoRedirect}/{handler.MaxAutomaticRedirections}, cookies {handler.UseCookies}, "
    + $"decompression {handler.AutomaticDecompression}, proxy {handler.UseProxy}, preauth {handler.PreAuthenticate}, default creds {handler.UseDefaultCredentials}, "
    + $"max connections {handler.MaxConnectionsPerServer}, max response header {handler.MaxResponseHeadersLength}, tls {handler.SslProtocols}");
var sockets = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(3), MaxConnectionsPerServer = 4, UseCookies = false };
Console.WriteLine($"SocketsHttpHandler: lifetime {sockets.PooledConnectionLifetime}, idle {sockets.PooledConnectionIdleTimeout}, connect {sockets.ConnectTimeout}, max {sockets.MaxConnectionsPerServer}, "
    + $"expect100 {sockets.Expect100ContinueTimeout}, cookies {sockets.UseCookies}, redirects {sockets.AllowAutoRedirect}");
using (var http = new HttpClient(sockets))
    Console.WriteLine(await http.GetStringAsync(root + "/hello"));
Seen();

// A DelegatingHandler pipeline, no network.
using (var http = new HttpClient(new LoggingHandler(new StubHandler())))
{
    using var response = await http.GetAsync("http://stub.invalid/items?id=7");
    Console.WriteLine(response.StatusCode + " " + await response.Content.ReadAsStringAsync() + " | X-Stub: " + string.Join(",", response.Headers.GetValues("X-Stub")));
    Console.WriteLine(await http.PostAsync("http://stub.invalid/post", new StringContent("payload")) is { } posted ? "post: " + await posted.Content.ReadAsStringAsync() : "");
}

listener.Stop();

class StubHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? "no body" : "body=" + await request.Content.ReadAsStringAsync(cancellationToken);
        var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(request.Method + " " + request.RequestUri!.PathAndQuery + " " + body), RequestMessage = request };
        response.Headers.Add("X-Stub", "yes");
        return response;
    }
}

class LoggingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine("  -> " + request.Method + " " + request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        Console.WriteLine("  <- " + (int)response.StatusCode);
        return response;
    }
}
