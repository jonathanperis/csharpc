// An await on a socket operation continues on a thread pool thread, never on the thread that delivers readiness
// (csharpc's epoll thread): the continuation may make synchronous socket calls that wait for other asynchronous
// operations to be delivered. Before the fix this program hung: the continuation after the first receive ran on the
// event loop and blocked it in a synchronous Receive whose peer was waiting on the same loop.
using System.Net;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;

// An echo server written with asynchronous operations only.
_ = Task.Run(async () =>
{
    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();
        _ = Task.Run(async () =>
        {
            var stream = client.GetStream();
            var buffer = new byte[64];
            int n;
            while ((n = await stream.ReadAsync(buffer)) > 0) await stream.WriteAsync(buffer.AsMemory(0, n));
        });
    }
});

using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
Console.WriteLine("connected after await: " + socket.Connected);

// First an asynchronous round trip...
await socket.SendAsync(Encoding.ASCII.GetBytes("first"));
var buffer = new byte[64];
int received = await socket.ReceiveAsync(buffer);
Console.WriteLine("async echo: " + Encoding.ASCII.GetString(buffer, 0, received));

// ...then, in the continuation, synchronous calls that wait on the server's asynchronous completions.
for (int i = 0; i < 3; i++)
{
    socket.Send(Encoding.ASCII.GetBytes("sync " + i));
    int n = socket.Receive(buffer);
    Console.WriteLine("sync echo: " + Encoding.ASCII.GetString(buffer, 0, n));
    await socket.SendAsync(Encoding.ASCII.GetBytes("async " + i));
    n = await socket.ReceiveAsync(buffer);
    Console.WriteLine("async echo: " + Encoding.ASCII.GetString(buffer, 0, n));
}

// The same through a NetworkStream and a TcpClient.
using var tcp = new TcpClient();
await tcp.ConnectAsync(IPAddress.Loopback, port);
var stream = tcp.GetStream();
await stream.WriteAsync(Encoding.ASCII.GetBytes("stream"));
int count = await stream.ReadAsync(buffer);
stream.Write(Encoding.ASCII.GetBytes("blocking"));
count = stream.Read(buffer, 0, buffer.Length);
Console.WriteLine("stream: " + Encoding.ASCII.GetString(buffer, 0, count));

listener.Stop();
