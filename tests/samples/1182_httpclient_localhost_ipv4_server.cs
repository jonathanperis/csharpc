// HttpClient reaches an IPv4-only server through the host name "localhost": the connection is made by name
// (SocketsHttpHandler's ConnectAsync(DnsEndPoint)), so every address the name resolves to is tried in turn
// and ::1 being refused must not fail the request. Plain HTTP against a TcpListener in the same program: GET and
// POST by name, a second request on the pooled connection, the Host header the server sees, and a
// request to a port nothing listens on (the exception type and status; its text is in sample 1181).
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
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
        string[] request = lines[0].Split(' ');
        int length = 0;
        foreach (var line in lines)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
        while (pending.Length < length)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return;
            pending.Append(Encoding.ASCII.GetString(buffer, 0, n));
        }
        string body = pending.ToString(0, length);
        pending.Remove(0, length);
        string host = lines.First(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).Replace(port.ToString(), "PORT");
        string text = $"{request[0]} {request[1]} {host} body=[{body}]";
        byte[] payload = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {payload.Length}\r\n\r\n"));
        await stream.WriteAsync(payload);
    }
}

_ = Serve();

using var http = new HttpClient();
Console.WriteLine(await http.GetStringAsync($"http://localhost:{port}/hello"));
Console.WriteLine(await http.GetStringAsync($"http://localhost:{port}/again"));
var post = await http.PostAsync($"http://localhost:{port}/submit", new StringContent("payload", Encoding.UTF8, "text/plain"));
Console.WriteLine($"{(int)post.StatusCode} {await post.Content.ReadAsStringAsync()}");
Console.WriteLine($"connections used: {connections}");

listener.Stop();

// Nothing listens any more on that port (a new client: the old one may reuse a connection it already holds).
using var fresh = new HttpClient();
try { await fresh.GetStringAsync($"http://localhost:{port}/gone"); }
catch (HttpRequestException e) { Console.WriteLine($"failed: {e.GetType().Name} {(e.InnerException as SocketException)?.SocketErrorCode} {e.StatusCode?.ToString() ?? "no status"}"); }
