// HttpClient failures over plain HTTP/1.1 against a loopback server: connection refused (HttpRequestException with a
// SocketException inside), a canceled token, the configured Timeout, a server that hangs up or sends garbage,
// invalid and unsupported request URIs, properties changed after the first request, a disposed client and an
// over-large response. Messages and exception types are .NET's.
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var release = new TaskCompletionSource();

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
    var buffer = new byte[4096];
    var head = new StringBuilder();
    while (!head.ToString().Contains("\r\n\r\n"))
    {
        int n = await stream.ReadAsync(buffer);
        if (n == 0) return;
        head.Append(Encoding.ASCII.GetString(buffer, 0, n));
    }
    string path = head.ToString().Split(' ')[1];
    switch (path)
    {
        case "/silent":
            await release.Task; // never answers until the sample ends
            return;
        case "/hangup":
            return; // closes without a response
        case "/garbage":
            await stream.WriteAsync(Encoding.ASCII.GetBytes("this is not http\r\n\r\n"));
            return;
        case "/truncated":
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\nonly a few bytes"));
            return;
        case "/big":
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 20\r\n\r\n01234567890123456789"));
            await release.Task;
            return;
        default:
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"));
            return;
    }
}

_ = Serve();
string url(string path) => "http://127.0.0.1:" + port + path;

async Task Try(string label, Func<Task> action)
{
    try
    {
        await action();
        Console.WriteLine(label + ": no exception");
    }
    catch (Exception e)
    {
        string inner = e.InnerException is null ? "" : " <- " + e.InnerException.GetType().Name + ": " + e.InnerException.Message.Replace(port.ToString(), "PORT");
        string extra = e is HttpRequestException h ? " [" + h.HttpRequestError + ", " + h.StatusCode + "]" : "";
        Console.WriteLine(label + ": " + e.GetType().Name + ": " + e.Message.Replace(port.ToString(), "PORT") + extra + inner);
    }
}

// Connection refused: a port nothing listens on.
var closed = new TcpListener(IPAddress.Loopback, 0);
closed.Start();
int closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
closed.Stop();
using (var http = new HttpClient())
{
    try { await http.GetStringAsync("http://127.0.0.1:" + closedPort + "/"); }
    catch (HttpRequestException e)
    {
        Console.WriteLine("refused: " + e.Message.Replace(closedPort.ToString(), "PORT") + " [" + e.HttpRequestError + "]");
        var socket = (SocketException)e.InnerException!;
        Console.WriteLine("  inner: " + socket.SocketErrorCode);
    }
}

using (var http = new HttpClient())
{
    // A token canceled while the request waits for the response.
    using var cts = new CancellationTokenSource(150);
    try { await http.GetAsync(url("/silent"), cts.Token); }
    catch (TaskCanceledException e) { Console.WriteLine("canceled: " + e.Message + " | token matches: " + (e.CancellationToken == cts.Token) + " | inner " + e.InnerException?.GetType().Name); }

    // An already canceled token never connects.
    using var already = new CancellationTokenSource();
    already.Cancel();
    await Try("pre-canceled", () => http.GetStringAsync(url("/hello"), already.Token));

    await Try("hangup", () => http.GetStringAsync(url("/hangup")));
    await Try("garbage", () => http.GetStringAsync(url("/garbage")));
    await Try("truncated body", () => http.GetStringAsync(url("/truncated")));
    await Try("relative uri", () => http.GetStringAsync("/relative"));
    await Try("unsupported scheme", () => http.GetStringAsync("ftp://127.0.0.1/file"));
    await Try("null uri", () => http.GetStringAsync((string?)null));
    await Try("ok", async () => Console.WriteLine("  " + await http.GetStringAsync(url("/ok"))));

    // Properties are frozen once the client has sent a request.
    try { http.Timeout = TimeSpan.FromSeconds(1); }
    catch (InvalidOperationException e) { Console.WriteLine("timeout after request: " + e.Message); }
    try { http.MaxResponseContentBufferSize = 5; }
    catch (InvalidOperationException e) { Console.WriteLine("buffer size after request: " + e.Message); }
}

using (var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(200) })
{
    Console.WriteLine("timeout: " + http.Timeout);
    await Try("timeout", () => http.GetStringAsync(url("/silent")));
    try { await http.GetStringAsync(url("/silent")); }
    catch (TaskCanceledException e) { Console.WriteLine("  timeout exception: " + e.GetType().Name + ", inner " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message); }
}

using (var http = new HttpClient { MaxResponseContentBufferSize = 5 })
    await Try("over-large response", () => http.GetStringAsync(url("/big")));

using (var http = new HttpClient())
{
    await Try("invalid timeout", () => { http.Timeout = TimeSpan.FromSeconds(-5); return Task.CompletedTask; });
    await Try("zero timeout", () => { http.Timeout = TimeSpan.Zero; return Task.CompletedTask; });
    await Try("infinite timeout", () => { http.Timeout = Timeout.InfiniteTimeSpan; return Task.CompletedTask; });
    await Try("negative buffer", () => { http.MaxResponseContentBufferSize = -1; return Task.CompletedTask; });
}

var disposed = new HttpClient();
disposed.Dispose();
await Try("disposed", () => disposed.GetStringAsync(url("/ok")));

release.SetResult();
listener.Stop();
