// Npgsql's synchronous connect: a non-blocking socket (Socket.Blocking = false) whose Connect fails with
// WouldBlock while under way, Socket.Select for writability, SO_ERROR through GetSocketOption(Error) (a
// SocketError), then Blocking = true again; and the same for a refused connection. Plus Select's
// argument checks, socket buffer sizes and keep-alive options.
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
listener.Listen(4);
var endPoint = (IPEndPoint)listener.LocalEndPoint!;

SocketError ConnectNonBlocking(Socket socket, EndPoint target)
{
    Console.WriteLine("blocking before: " + socket.Blocking);
    socket.Blocking = false;
    Console.WriteLine("blocking now: " + socket.Blocking);
    try
    {
        socket.Connect(target);
    }
    catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
    {
    }
    catch (SocketException e)
    {
        return e.SocketErrorCode;
    }
    var write = new List<Socket> { socket };
    var error = new List<Socket> { socket };
    Socket.Select(null, write, error, 5_000_000);
    return (SocketError)(int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
}

var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
Console.WriteLine("keep-alive: " + ((int)client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) != 0));
client.ReceiveBufferSize = 65536;
client.SendBufferSize = 65536;
Console.WriteLine("buffers set: " + (client.ReceiveBufferSize >= 65536) + " " + (client.SendBufferSize >= 65536));
Console.WriteLine("connect: " + ConnectNonBlocking(client, endPoint));
Console.WriteLine("connected: " + client.Connected);
client.Blocking = true;
Console.WriteLine("blocking again: " + client.Blocking);
var server = listener.Accept();
client.Send(new byte[] { 1, 2, 3 });
var buffer = new byte[8];
int got = 0;
while (got < 3) got += server.Receive(buffer, got, buffer.Length - got, SocketFlags.None);
Console.WriteLine("server got: " + string.Join(",", buffer[..got]));

// Readability: the server's socket has data once the client sends.
client.Send(new byte[] { 9 });
var read = new List<Socket> { server, listener };
Socket.Select(read, null, null, 5_000_000);
Console.WriteLine("readable: " + read.Count + " " + (read[0] == server));
server.Close();
client.Close();

// A port nothing listens on: refused, through Connect itself or SO_ERROR after Select.
int closedPort;
using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    closedPort = ((IPEndPoint)probe.LocalEndPoint!).Port;
}
using (var refused = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    var result = ConnectNonBlocking(refused, new IPEndPoint(IPAddress.Loopback, closedPort));
    Console.WriteLine("refused: " + result);
}
listener.Close();

try { Socket.Select(null, new List<Socket>(), null, 0); }
catch (ArgumentNullException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
