// Socket.ConnectAsync is an asynchronous connect, not a blocking connect on a pool thread: it connects
// (Connected, a bound local endpoint), fails like .NET's asynchronous connect (no endpoint in the message,
// unlike the synchronous Connect's), and a canceled token ends it with OperationCanceledException.
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
listener.Listen(16);
var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

using (var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    var accept = listener.AcceptAsync();
    await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), CancellationToken.None);
    using var server = await accept;
    Console.WriteLine($"connected {client.Connected}, bound {client.IsBound}, local port set {((IPEndPoint)client.LocalEndPoint!).Port != 0}, remote {((IPEndPoint)client.RemoteEndPoint!).Port == port}");
    await client.SendAsync("ping"u8.ToArray(), SocketFlags.None);
    var buffer = new byte[4];
    Console.WriteLine($"server received {await server.ReceiveAsync(buffer, SocketFlags.None)} bytes");
    try { await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port)); }
    catch (SocketException e) { Console.WriteLine($"again: {e.SocketErrorCode}"); }
}

// A port nothing listens on: the listener's, once closed.
int closedPort;
using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    closedPort = ((IPEndPoint)probe.LocalEndPoint!).Port;
}

using (var refused = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    try { await refused.ConnectAsync(new IPEndPoint(IPAddress.Loopback, closedPort)); }
    catch (SocketException e) { Console.WriteLine($"async: {e.SocketErrorCode} endpoint in message {e.Message.Contains(closedPort.ToString())} connected {refused.Connected}"); }
}

using (var refused = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    try { refused.Connect(new IPEndPoint(IPAddress.Loopback, closedPort)); }
    // The message itself is the OS's ("Connection refused" on Linux).
    catch (SocketException e) { Console.WriteLine($"sync: {e.SocketErrorCode} ends with endpoint {e.Message.EndsWith(" 127.0.0.1:" + closedPort)}"); }
}

using (var canceled = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    try { await canceled.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), new CancellationToken(true)); }
    catch (OperationCanceledException e) { Console.WriteLine($"canceled: {e.GetType().Name}"); }
}

using (var task = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    Task connect = task.ConnectAsync(IPAddress.Loopback, port);
    await connect;
    Console.WriteLine($"Task overload: connected {task.Connected}");
}
