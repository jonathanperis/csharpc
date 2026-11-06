// Connecting to a host NAME ("localhost") tries every address the name resolves to, in order, and keeps the
// first one that works: "localhost" is ::1 and 127.0.0.1 on most machines, and the listener here is IPv4 only.
// Covers Socket.Connect/ConnectAsync(string, int), (DnsEndPoint) and (IPAddress[], int), SocketAsyncEventArgs
// with a DnsEndPoint (instance and static ConnectAsync), TcpClient's constructor, Connect and ConnectAsync
// overloads, the address-family rules (a socket created for IPv4 only tries IPv4 addresses, a dual-mode
// socket reaches IPv4 through mapped addresses) and the error codes when nothing listens. Nothing printed
// depends on the machine's name resolution or on the port; error message texts are in sample 1181.
using System.Net;
using System.Net.Sockets;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;

// Echo server: every accepted connection returns one byte it receives.
_ = Task.Run(async () =>
{
    while (true)
    {
        TcpClient accepted;
        try { accepted = await listener.AcceptTcpClientAsync(); }
        catch (Exception) { return; }
        _ = Task.Run(async () =>
        {
            using var c = accepted;
            var s = c.GetStream();
            var one = new byte[1];
            try { while (await s.ReadAsync(one) == 1) await s.WriteAsync(one); }
            catch (IOException) { }
        });
    }
});

int closedPort;
using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    closedPort = ((IPEndPoint)probe.LocalEndPoint!).Port;
}

// Where the peer is, whichever way the name was resolved: the IPv4 address (mapped or not) and the port.
string Peer(Socket s)
{
    var ep = (IPEndPoint)s.RemoteEndPoint!;
    return $"{ep.Address.MapToIPv4()} port ok {ep.Port == port}";
}

async Task<string> Echo(Socket s)
{
    await s.SendAsync(new byte[] { 0x5A });
    var buffer = new byte[1];
    int n = await s.ReceiveAsync(buffer);
    return $"echo {n} byte 0x{buffer[0]:X2}";
}

async Task Case(string label, Func<Task<string>> body)
{
    try { Console.WriteLine($"{label}: {await body()}"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name} {(e as SocketException)?.SocketErrorCode}"); }
}

Socket Dual() => new Socket(SocketType.Stream, ProtocolType.Tcp);
Socket V4() => new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

await Case("Socket.ConnectAsync(DnsEndPoint)", async () =>
{
    using var s = Dual();
    await s.ConnectAsync(new DnsEndPoint("localhost", port));
    return $"{s.Connected} {Peer(s)} {await Echo(s)}";
});
await Case("Socket.ConnectAsync(DnsEndPoint, token)", async () =>
{
    using var s = Dual();
    await s.ConnectAsync(new DnsEndPoint("localhost", port), CancellationToken.None);
    return $"{s.Connected} {Peer(s)}";
});
await Case("Socket.ConnectAsync(host, port)", async () =>
{
    using var s = Dual();
    await s.ConnectAsync("localhost", port);
    return $"{s.Connected} {Peer(s)} {await Echo(s)}";
});
await Case("Socket.ConnectAsync(host, port, token)", async () =>
{
    using var s = Dual();
    await s.ConnectAsync("localhost", port, CancellationToken.None);
    return $"{s.Connected} {Peer(s)}";
});
await Case("Socket.Connect(host, port)", () =>
{
    using var s = Dual();
    s.Connect("localhost", port);
    return Task.FromResult($"{s.Connected} {Peer(s)}");
});
await Case("Socket.Connect(DnsEndPoint)", () =>
{
    using var s = Dual();
    s.Connect(new DnsEndPoint("localhost", port));
    return Task.FromResult($"{s.Connected} {Peer(s)}");
});
await Case("Socket.Connect(IPAddress[]) ::1 then IPv4", () =>
{
    using var s = Dual();
    s.Connect(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }, port);
    return Task.FromResult($"{s.Connected} {Peer(s)}");
});
await Case("Socket.ConnectAsync(IPAddress[]) ::1 then IPv4", async () =>
{
    using var s = Dual();
    await s.ConnectAsync(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }, port);
    return $"{s.Connected} {Peer(s)} {await Echo(s)}";
});
await Case("Socket.ConnectAsync(IPAddress[], token)", async () =>
{
    using var s = Dual();
    await s.ConnectAsync(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }, port, CancellationToken.None);
    return $"{s.Connected} {Peer(s)}";
});

// A socket created for IPv4 only tries the IPv4 addresses of the name.
await Case("IPv4 socket, ConnectAsync(host, port)", async () =>
{
    using var s = V4();
    await s.ConnectAsync("localhost", port);
    return $"{s.AddressFamily} {Peer(s)}";
});
await Case("IPv4 socket, ConnectAsync(DnsEndPoint)", async () =>
{
    using var s = V4();
    await s.ConnectAsync(new DnsEndPoint("localhost", port));
    return $"{s.AddressFamily} {Peer(s)}";
});
await Case("IPv4 socket, Connect(host, port)", () =>
{
    using var s = V4();
    s.Connect("localhost", port);
    return Task.FromResult($"{s.AddressFamily} {Peer(s)}");
});
await Case("IPv4 socket, DnsEndPoint for IPv6", () =>
{
    using var s = V4();
    s.Connect(new DnsEndPoint("localhost", port, AddressFamily.InterNetworkV6));
    return Task.FromResult("connected");
});
await Case("IPv4 socket, only IPv6 addresses", async () =>
{
    using var s = V4();
    await s.ConnectAsync(new[] { IPAddress.IPv6Loopback }, port);
    return "connected";
});
await Case("dual-mode socket, DnsEndPoint with IPv4 address family", async () =>
{
    using var s = Dual();
    await s.ConnectAsync(new DnsEndPoint("localhost", port, AddressFamily.InterNetwork));
    return $"{s.Connected} {Peer(s)}";
});
await Case("connecting twice", async () =>
{
    using var s = Dual();
    await s.ConnectAsync(new DnsEndPoint("localhost", port));
    await s.ConnectAsync(new DnsEndPoint("localhost", port));
    return "connected twice";
});
await Case("canceled token", async () =>
{
    using var s = Dual();
    await s.ConnectAsync("localhost", port, new CancellationToken(true));
    return "connected";
});
await Case("port out of range", async () =>
{
    using var s = Dual();
    await s.ConnectAsync("localhost", 70000);
    return "connected";
});

// SocketAsyncEventArgs: a DnsEndPoint is resolved and tried address by address; ConnectSocket is the socket
// that connected (null after a failure), ConnectByNameError the failure of the last address.
async Task<SocketAsyncEventArgs> RunAsync(Func<SocketAsyncEventArgs, bool> start, EndPoint remote)
{
    var args = new SocketAsyncEventArgs { RemoteEndPoint = remote };
    var done = new TaskCompletionSource();
    args.Completed += (_, _) => done.TrySetResult();
    if (start(args)) await done.Task;
    return args;
}
string Outcome(SocketAsyncEventArgs a, Socket? user) =>
    $"{a.SocketError} {a.LastOperation} socket {(a.ConnectSocket == null ? "none" : a.ConnectSocket == user ? "the caller's" : a.ConnectSocket.AddressFamily.ToString())} error {a.ConnectByNameError?.GetType().Name ?? "none"}";

using (var s = Dual())
{
    var a = await RunAsync(s.ConnectAsync, new DnsEndPoint("localhost", port));
    Console.WriteLine($"SocketAsyncEventArgs DnsEndPoint: {Outcome(a, s)} connected {s.Connected} {Peer(s)}");
}
using (var s = V4())
{
    var a = await RunAsync(s.ConnectAsync, new DnsEndPoint("localhost", port));
    Console.WriteLine($"SocketAsyncEventArgs DnsEndPoint, IPv4 socket: {Outcome(a, s)} connected {s.Connected} {Peer(s)}");
}
using (var s = Dual())
{
    var a = await RunAsync(s.ConnectAsync, new DnsEndPoint("localhost", closedPort));
    Console.WriteLine($"SocketAsyncEventArgs DnsEndPoint, nothing listens: {Outcome(a, s)} connected {s.Connected}");
}
using (var s = Dual())
{
    var a = await RunAsync(s.ConnectAsync, new IPEndPoint(IPAddress.Loopback, closedPort));
    Console.WriteLine($"SocketAsyncEventArgs IPEndPoint, nothing listens: {Outcome(a, s)} connected {s.Connected}");
}
using (var s = V4())
{
    var a = await RunAsync(s.ConnectAsync, new IPEndPoint(IPAddress.Loopback, port));
    Console.WriteLine($"SocketAsyncEventArgs IPEndPoint: {Outcome(a, s)} connected {s.Connected} {Peer(s)}");
}
{
    var a = await RunAsync(x => Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, x), new DnsEndPoint("localhost", port));
    using var connected = a.ConnectSocket!;
    Console.WriteLine($"static ConnectAsync DnsEndPoint: {Outcome(a, null)} {Peer(connected)} {await Echo(connected)}");
}
{
    var a = await RunAsync(x => Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, x), new DnsEndPoint("localhost", closedPort));
    Console.WriteLine($"static ConnectAsync DnsEndPoint, nothing listens: {Outcome(a, null)}");
}
{
    var a = await RunAsync(x => Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, x), new IPEndPoint(IPAddress.Loopback, port));
    using var connected = a.ConnectSocket!;
    Console.WriteLine($"static ConnectAsync IPEndPoint: {Outcome(a, null)} {Peer(connected)}");
}
try { new SocketAsyncEventArgs().Let(a => Dual().ConnectAsync(a)); }
catch (Exception e) { Console.WriteLine($"no RemoteEndPoint: {e.GetType().Name}"); }

// TcpClient: a dual-mode socket by default, an IPv4 one when asked for.
await Case("new TcpClient(host, port)", () =>
{
    using var c = new TcpClient("localhost", port);
    return Task.FromResult($"{c.Connected} {c.Client.AddressFamily} {Peer(c.Client)}");
});
await Case("TcpClient.Connect(host, port)", () =>
{
    using var c = new TcpClient();
    c.Connect("localhost", port);
    return Task.FromResult($"{c.Connected} {c.Client.AddressFamily} {Peer(c.Client)}");
});
await Case("TcpClient.ConnectAsync(host, port)", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync("localhost", port);
    var stream = c.GetStream();
    await stream.WriteAsync(new byte[] { 0x21 });
    var one = new byte[1];
    int n = await stream.ReadAsync(one);
    return $"{c.Connected} {c.Client.AddressFamily} {Peer(c.Client)} echo {n} 0x{one[0]:X2}";
});
await Case("TcpClient.ConnectAsync(host, port, token)", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync("localhost", port, CancellationToken.None);
    return $"{c.Connected} {Peer(c.Client)}";
});
await Case("TcpClient.ConnectAsync(IPAddress[], port)", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }, port);
    return $"{c.Connected} {Peer(c.Client)}";
});
await Case("TcpClient.Connect(IPAddress[], port)", () =>
{
    using var c = new TcpClient();
    c.Connect(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback }, port);
    return Task.FromResult($"{c.Connected} {Peer(c.Client)}");
});
await Case("TcpClient.ConnectAsync(IPEndPoint)", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
    return $"{c.Connected} {Peer(c.Client)}";
});
await Case("TcpClient.ConnectAsync(IPAddress, port, token)", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None);
    return $"{c.Connected} {Peer(c.Client)}";
});
await Case("TcpClient(AddressFamily.InterNetwork).ConnectAsync(host, port)", async () =>
{
    using var c = new TcpClient(AddressFamily.InterNetwork);
    await c.ConnectAsync("localhost", port);
    return $"{c.Connected} {c.Client.AddressFamily} {Peer(c.Client)}";
});
await Case("TcpClient(AddressFamily.InterNetwork).Connect(host, port)", () =>
{
    using var c = new TcpClient(AddressFamily.InterNetwork);
    c.Connect("localhost", port);
    return Task.FromResult($"{c.Connected} {c.Client.AddressFamily} {Peer(c.Client)}");
});
await Case("TcpClient connecting twice", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync("localhost", port);
    await c.ConnectAsync("localhost", port);
    return "connected twice";
});
await Case("TcpClient connecting twice (sync)", () =>
{
    using var c = new TcpClient();
    c.Connect("localhost", port);
    c.Connect("localhost", port);
    return Task.FromResult("connected twice");
});
await Case("TcpClient.ConnectAsync, canceled token", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync("localhost", port, new CancellationToken(true));
    return "connected";
});
await Case("TcpClient.ConnectAsync, port out of range", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync("localhost", 70000);
    return "connected";
});
await Case("TcpClient.Connect, null host", () =>
{
    using var c = new TcpClient();
    c.Connect((string)null!, port);
    return Task.FromResult("connected");
});

// Nothing listens: every attempt fails, and the failure of the last address is the exception.
await Case("Socket.ConnectAsync(DnsEndPoint), nothing listens", async () =>
{
    using var s = Dual();
    await s.ConnectAsync(new DnsEndPoint("localhost", closedPort));
    return "connected";
});
await Case("Socket.ConnectAsync(host, port), nothing listens", async () =>
{
    using var s = Dual();
    await s.ConnectAsync("localhost", closedPort);
    return "connected";
});
await Case("Socket.Connect(host, port), nothing listens", () =>
{
    using var s = Dual();
    s.Connect("localhost", closedPort);
    return Task.FromResult("connected");
});
await Case("TcpClient.ConnectAsync, nothing listens", async () =>
{
    using var c = new TcpClient();
    await c.ConnectAsync("localhost", closedPort);
    return "connected";
});
await Case("TcpClient.Connect, nothing listens", () =>
{
    using var c = new TcpClient();
    c.Connect("localhost", closedPort);
    return Task.FromResult("connected");
});
await Case("the socket stays usable after a failed name connect", async () =>
{
    using var s = Dual();
    try { await s.ConnectAsync("localhost", closedPort); } catch (SocketException) { }
    await s.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
    return $"{s.Connected} {Peer(s)}";
});

listener.Stop();

static class Extensions
{
    public static void Let<T>(this T value, Action<T> action) => action(value);
}
