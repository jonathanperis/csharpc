// The exceptions a connect by host name throws when no address works, with their texts (the OS's, so this
// sample is recorded on Linux: tests/platform-skips.txt lists it for the other systems): the failure of the
// last address is the one reported, asynchronous connects carry no endpoint in the message, synchronous ones do,
// and a name that does not exist is HostNotFound. Sample 1180 has the codes on every platform. The
// address and port in the texts are replaced, as they depend on the machine and the run.
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;

int closed;
using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    closed = ((IPEndPoint)probe.LocalEndPoint!).Port;
}

string Scrub(string text) =>
    Regex.Replace(text.Replace(closed.ToString(), "PORT"), @"\[?(::ffff:127\.0\.0\.1|::1)\]?|127\.0\.0\.1", "ADDRESS");

async Task Case(string label, Func<Task> body)
{
    try { await body(); Console.WriteLine($"{label}: connected"); }
    catch (Exception e)
    {
        Console.WriteLine($"{label}: {e.GetType().FullName}: {Scrub(e.Message)}");
        if (e is SocketException se) Console.WriteLine($"    code {se.SocketErrorCode}, native {se.NativeErrorCode}");
    }
}

Socket Dual() => new Socket(SocketType.Stream, ProtocolType.Tcp);

await Case("Socket.ConnectAsync(DnsEndPoint)", async () => { using var s = Dual(); await s.ConnectAsync(new DnsEndPoint("localhost", closed)); });
await Case("Socket.ConnectAsync(host, port)", async () => { using var s = Dual(); await s.ConnectAsync("localhost", closed); });
await Case("Socket.ConnectAsync(host, port, token)", async () => { using var s = Dual(); await s.ConnectAsync("localhost", closed, CancellationToken.None); });
await Case("Socket.ConnectAsync(IPAddress[])", async () => { using var s = Dual(); await s.ConnectAsync(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }, closed); });
await Case("Socket.Connect(host, port)", () => { using var s = Dual(); s.Connect("localhost", closed); return Task.CompletedTask; });
await Case("Socket.Connect(DnsEndPoint)", () => { using var s = Dual(); s.Connect(new DnsEndPoint("localhost", closed)); return Task.CompletedTask; });
await Case("Socket.Connect(IPAddress[])", () => { using var s = Dual(); s.Connect(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }, closed); return Task.CompletedTask; });
await Case("IPv4 socket, Connect(host, port)", () =>
{
    using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    s.Connect("localhost", closed);
    return Task.CompletedTask;
});
await Case("IPv4 socket, ConnectAsync(host, port)", async () =>
{
    using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    await s.ConnectAsync("localhost", closed);
});
await Case("TcpClient.Connect(host, port)", () => { using var c = new TcpClient(); c.Connect("localhost", closed); return Task.CompletedTask; });
await Case("TcpClient.ConnectAsync(host, port)", async () => { using var c = new TcpClient(); await c.ConnectAsync("localhost", closed); });
await Case("new TcpClient(host, port)", () => { using var c = new TcpClient("localhost", closed); return Task.CompletedTask; });
await Case("IPv4 socket, only IPv6 addresses", () => { using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); s.Connect(new[] { IPAddress.IPv6Loopback }, closed); return Task.CompletedTask; });
await Case("IPv4 socket, DnsEndPoint for IPv6", async () =>
{
    using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    await s.ConnectAsync(new DnsEndPoint("localhost", closed, AddressFamily.InterNetworkV6));
});
await Case("empty address list", async () => { using var s = Dual(); await s.ConnectAsync(new IPAddress[0], closed); });
await Case("port out of range", async () => { using var s = Dual(); await s.ConnectAsync("localhost", 70000); });
await Case("TcpClient, port out of range", () => { using var c = new TcpClient(); c.Connect("localhost", -1); return Task.CompletedTask; });

// SocketAsyncEventArgs report the failure in SocketError and ConnectByNameError instead of throwing.
{
    var eventArgs = new SocketAsyncEventArgs { RemoteEndPoint = new DnsEndPoint("localhost", closed) };
    var done = new TaskCompletionSource();
    eventArgs.Completed += (_, _) => done.TrySetResult();
    using var s = Dual();
    if (s.ConnectAsync(eventArgs)) await done.Task;
    Console.WriteLine($"SocketAsyncEventArgs: {eventArgs.SocketError}: {eventArgs.ConnectByNameError!.GetType().Name}: {Scrub(eventArgs.ConnectByNameError.Message)}");
}

// HttpClient names the host and port it could not reach.
using (var http = new HttpClient())
{
    try { await http.GetStringAsync($"http://localhost:{closed}/"); }
    catch (HttpRequestException e)
    {
        Console.WriteLine($"HttpClient: {e.GetType().FullName}: {Scrub(e.Message)}");
        Console.WriteLine($"  inner: {e.InnerException?.GetType().FullName}: {e.InnerException?.Message}");
    }
}
