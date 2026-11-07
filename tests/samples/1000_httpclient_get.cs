// HttpClient over plain HTTP/1.1 against a loopback TcpListener server in the same program (System.Net.Http is
// compiled from the shared framework's IL): GET with GetStringAsync / GetAsync / GetByteArrayAsync / GetStreamAsync,
// status line and header parsing (repeated, content and chunked responses), HEAD and 204, EnsureSuccessStatusCode,
// the request head the server sees, DefaultRequestHeaders and connection reuse.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var seen = new ConcurrentQueue<string>();
int connections = 0;

async Task Serve()
{
    while (true)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(); }
        catch (Exception) { return; }
        Interlocked.Increment(ref connections);
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
        string head = pending.ToString(0, end);
        pending.Remove(0, end + 4);
        var lines = head.Split("\r\n");
        seen.Enqueue(string.Join(" | ", lines.Select(l => l.Replace(port.ToString(), "PORT"))));
        string path = lines[0].Split(' ')[1];
        string method = lines[0].Split(' ')[0];
        byte[] response = path switch
        {
            "/hello" => Reply("200 OK", ["Content-Type: text/plain; charset=utf-8"], "hello world"),
            "/headers" => Reply("200 OK", ["X-Multi: a", "X-Multi: b", "ETag: \"abc\"", "Server: sample", "Content-Type: application/json"], "{\"a\":1}"),
            "/chunked" => Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Type: text/plain\r\n\r\n5\r\nhello\r\n7\r\n, world\r\n1\r\n!\r\n0\r\n\r\n"),
            "/empty" => Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\n\r\n"),
            "/missing" => Reply("404 Not Found", ["Content-Type: text/plain"], "no such page"),
            "/teapot" => Reply("418 I'm a teapot", [], "short and stout"),
            "/close" => Reply("200 OK", ["Connection: close"], "bye"),
            _ => Reply("500 Internal Server Error", [], "unexpected " + path),
        };
        if (method == "HEAD") response = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(response).Split("\r\n\r\n")[0] + "\r\n\r\n");
        await stream.WriteAsync(response);
        if (path == "/close") return;
    }
}

static byte[] Reply(string status, string[] headers, string body)
{
    var sb = new StringBuilder("HTTP/1.1 " + status + "\r\n");
    foreach (var h in headers) sb.Append(h).Append("\r\n");
    sb.Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(body)).Append("\r\n\r\n").Append(body);
    return Encoding.UTF8.GetBytes(sb.ToString());
}

void Server(string label)
{
    while (seen.TryDequeue(out var line)) Console.WriteLine("  server saw: " + line);
}

_ = Serve();
string url(string path) => "http://127.0.0.1:" + port + path;

using var http = new HttpClient();
Console.WriteLine("timeout: " + http.Timeout);
Console.WriteLine("default version: " + http.DefaultRequestVersion + ", policy " + http.DefaultVersionPolicy);

// GetStringAsync
Console.WriteLine(await http.GetStringAsync(url("/hello")));
Server("hello");

// GetAsync: status, headers, content headers.
using (var response = await http.GetAsync(url("/headers")))
{
    Console.WriteLine((int)response.StatusCode + " " + response.StatusCode + " / " + response.ReasonPhrase + " / HTTP " + response.Version);
    Console.WriteLine("success: " + response.IsSuccessStatusCode);
    foreach (var header in response.Headers) Console.WriteLine("  header " + header.Key + " = " + string.Join(", ", header.Value));
    foreach (var header in response.Content.Headers) Console.WriteLine("  content header " + header.Key + " = " + string.Join(", ", header.Value));
    Console.WriteLine("ETag: " + response.Headers.ETag + ", Server: " + response.Headers.Server);
    Console.WriteLine("ContentType: " + response.Content.Headers.ContentType + ", media " + response.Content.Headers.ContentType?.MediaType
        + ", length " + response.Content.Headers.ContentLength);
    Console.WriteLine("body: " + await response.Content.ReadAsStringAsync());
    Console.WriteLine("request: " + response.RequestMessage?.Method + " " + response.RequestMessage?.RequestUri?.AbsolutePath);
}
Server("headers");

// Chunked transfer encoding, bytes and streams.
using (var response = await http.GetAsync(url("/chunked")))
{
    Console.WriteLine("chunked: " + response.Headers.TransferEncodingChunked + " " + response.Content.Headers.ContentLength.HasValue);
    Console.WriteLine(await response.Content.ReadAsStringAsync());
}
byte[] bytes = await http.GetByteArrayAsync(url("/hello"));
Console.WriteLine("bytes: " + bytes.Length + " " + Convert.ToHexString(bytes, 0, 5));
using (var stream = await http.GetStreamAsync(url("/hello")))
using (var reader = new StreamReader(stream))
    Console.WriteLine("stream: " + await reader.ReadToEndAsync());
Server("chunked/bytes/stream");

// 204 and HEAD.
using (var response = await http.GetAsync(url("/empty")))
    Console.WriteLine("204: " + response.StatusCode + " length " + response.Content.Headers.ContentLength + " body '" + await response.Content.ReadAsStringAsync() + "'");
using (var head = new HttpRequestMessage(HttpMethod.Head, url("/hello")))
using (var response = await http.SendAsync(head))
    Console.WriteLine("HEAD: " + response.StatusCode + " length " + response.Content.Headers.ContentLength + " body '" + await response.Content.ReadAsStringAsync() + "'");
Server("empty/head");

// Failure statuses: GetStringAsync throws, GetAsync returns them, EnsureSuccessStatusCode throws.
try { await http.GetStringAsync(url("/missing")); }
catch (HttpRequestException e) { Console.WriteLine("GetStringAsync: " + e.Message + " | " + e.StatusCode + " | " + e.HttpRequestError); }
using (var response = await http.GetAsync(url("/teapot")))
{
    Console.WriteLine((int)response.StatusCode + " " + response.ReasonPhrase + " success " + response.IsSuccessStatusCode + ": " + await response.Content.ReadAsStringAsync());
    try { response.EnsureSuccessStatusCode(); }
    catch (HttpRequestException e) { Console.WriteLine("EnsureSuccessStatusCode: " + e.Message + " | " + e.StatusCode); }
}
Server("failures");

// Request headers: defaults, per-request, and the User-Agent / Accept parsing.
http.DefaultRequestHeaders.Add("X-Default", "one");
http.DefaultRequestHeaders.UserAgent.ParseAdd("sample-agent/1.0");
http.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
using (var request = new HttpRequestMessage(HttpMethod.Get, url("/hello")))
{
    request.Headers.Add("X-Request", "two");
    request.Headers.Add("X-Request", "three");
    request.Headers.Referrer = new Uri("http://example.invalid/page");
    using var response = await http.SendAsync(request);
    Console.WriteLine("with headers: " + response.StatusCode + " " + await response.Content.ReadAsStringAsync());
    Console.WriteLine("request headers: " + request.Headers);
}
Server("request headers");

// A response with Connection: close ends the connection; the next request opens another.
Console.WriteLine(await http.GetStringAsync(url("/close")));
Console.WriteLine(await http.GetStringAsync(url("/hello")));
Server("close");
Console.WriteLine("connections opened: " + connections);

listener.Stop();
