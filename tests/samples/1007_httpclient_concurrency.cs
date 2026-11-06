// HttpClient under load and in other shapes against a loopback server: parallel requests over a bounded number of
// connections, a large request and response body, a close-delimited HTTP/1.0 response, the synchronous Send, reading
// a response with CopyToAsync / ReadAsStream, HttpCompletionOption.ResponseHeadersRead, and reuse of one client
// by several threads.
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
int connections = 0, maxConcurrent = 0, active = 0;

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
    var buffer = new byte[65536];
    var pending = new List<byte>();
    while (true)
    {
        int end;
        while ((end = IndexOf(pending, "\r\n\r\n")) < 0)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return;
            pending.AddRange(buffer.AsSpan(0, n).ToArray());
        }
        var lines = Encoding.ASCII.GetString(pending.GetRange(0, end).ToArray()).Split("\r\n");
        pending.RemoveRange(0, end + 4);
        string[] request = lines[0].Split(' ');
        string path = request[1];
        int length = lines.Skip(1).Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Select(l => int.Parse(l[15..])).FirstOrDefault();
        while (pending.Count < length)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return;
            pending.AddRange(buffer.AsSpan(0, n).ToArray());
        }
        var body = pending.GetRange(0, length).ToArray();
        pending.RemoveRange(0, length);

        int now = Interlocked.Increment(ref active);
        lock (listener) maxConcurrent = Math.Max(maxConcurrent, now);
        byte[] reply;
        switch (path)
        {
            case var p when p.StartsWith("/item/"):
                await Task.Delay(30);
                reply = Response("200 OK", "item " + p[6..]);
                break;
            case "/upload":
                long sum = 0;
                foreach (byte b in body) sum += b;
                reply = Response("200 OK", "received " + body.Length + " bytes, checksum " + sum);
                break;
            case "/download":
                reply = Response("200 OK", new string('x', 300_000) + "END");
                break;
            case "/http10":
                Interlocked.Decrement(ref active);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n\r\nclose-delimited body"));
                return; // the end of the connection ends the body
            case "/slow-body":
                Interlocked.Decrement(ref active);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nfirst"));
                await Task.Delay(100);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("later"));
                continue;
            default:
                reply = Response("404 Not Found", "?");
                break;
        }
        Interlocked.Decrement(ref active);
        await stream.WriteAsync(reply);
    }
}

static byte[] Response(string status, string body)
{
    byte[] payload = Encoding.UTF8.GetBytes(body);
    return [.. Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: " + payload.Length + "\r\n\r\n"), .. payload];
}

static int IndexOf(List<byte> data, string marker)
{
    var m = Encoding.ASCII.GetBytes(marker);
    for (int i = 0; i + m.Length <= data.Count; i++)
    {
        int j = 0;
        while (j < m.Length && data[i + j] == m[j]) j++;
        if (j == m.Length) return i;
    }
    return -1;
}

_ = Serve();
string url(string path) => "http://127.0.0.1:" + port + path;

// Parallel requests, at most two connections to the server at a time.
using (var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 2 }))
{
    var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => http.GetStringAsync(url("/item/" + i))));
    Console.WriteLine(string.Join(", ", results));
    Console.WriteLine("connections: " + connections + ", max concurrent requests on the server: " + maxConcurrent);
}

using (var http = new HttpClient())
{
    // Large bodies in both directions.
    byte[] payload = new byte[200_000];
    for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7);
    using (var response = await http.PostAsync(url("/upload"), new ByteArrayContent(payload)))
        Console.WriteLine(await response.Content.ReadAsStringAsync());
    string big = await http.GetStringAsync(url("/download"));
    Console.WriteLine("download: " + big.Length + " chars, ends with " + big[^3..]);

    // A close-delimited HTTP/1.0 response.
    using (var response = await http.GetAsync(url("/http10")))
        Console.WriteLine("HTTP/" + response.Version + " " + response.StatusCode + ": " + await response.Content.ReadAsStringAsync()
            + " (length known: " + response.Content.Headers.ContentLength.HasValue + ")");

    // Headers first, the body as it arrives.
    using (var response = await http.GetAsync(url("/slow-body"), HttpCompletionOption.ResponseHeadersRead))
    {
        Console.WriteLine("headers read: " + response.StatusCode + ", length " + response.Content.Headers.ContentLength);
        using var stream = await response.Content.ReadAsStreamAsync();
        var chunk = new byte[10];
        int total = 0, n;
        while (total < 10 && (n = await stream.ReadAsync(chunk.AsMemory(total))) > 0) total += n;
        Console.WriteLine("body: " + Encoding.ASCII.GetString(chunk, 0, total));
    }

    // CopyToAsync and the synchronous API.
    using (var response = await http.GetAsync(url("/item/copy")))
    using (var copy = new MemoryStream())
    {
        await response.Content.CopyToAsync(copy);
        Console.WriteLine("copied: " + Encoding.UTF8.GetString(copy.ToArray()));
    }
    using (var request = new HttpRequestMessage(HttpMethod.Get, url("/item/sync")))
    using (var response = http.Send(request))
    using (var reader = new StreamReader(response.Content.ReadAsStream()))
        Console.WriteLine("sync: " + response.StatusCode + " " + reader.ReadToEnd());

    // One client shared by threads.
    var threads = Enumerable.Range(0, 4).Select(i => new Thread(() =>
    {
        for (int k = 0; k < 3; k++)
        {
            string text = http.GetStringAsync(url("/item/t" + i + "-" + k)).GetAwaiter().GetResult();
            if (text != "item t" + i + "-" + k) Console.WriteLine("mismatch: " + text);
        }
    })).ToList();
    threads.ForEach(t => t.Start());
    threads.ForEach(t => t.Join());
    Console.WriteLine("threads done");
}

listener.Stop();
