// A server rebinding its port while a connection it closed first is in TIME_WAIT: .NET sets SO_REUSEADDR
// on the TCP sockets it binds on Unix (SystemNative_Bind), so the second bind succeeds there, as it
// does for a server restarted at once (Kestrel, the TechEmpower harness). Only TCP sockets get it, and the
// ReuseAddress option reads SO_REUSEPORT there, so it still reads false.
using System.Net;
using System.Net.Sockets;

var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
Console.WriteLine("reuse address after bind: " + ((int)listener.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0));
listener.Listen(1);
var endPoint = (IPEndPoint)listener.LocalEndPoint!;

var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
client.Connect(endPoint);
var accepted = listener.Accept();
// The server closes first, so its end of the connection (the listener's port) goes to TIME_WAIT.
accepted.Shutdown(SocketShutdown.Both);
accepted.Close();
var buffer = new byte[16];
Console.WriteLine("client read after server close: " + client.Receive(buffer));
client.Close();
listener.Close();

for (int round = 1; round <= 3; round++)
{
    using var again = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    try
    {
        again.Bind(endPoint);
        again.Listen(1);
        Console.WriteLine($"rebind {round}: ok, same port {((IPEndPoint)again.LocalEndPoint!).Port == endPoint.Port}");
    }
    catch (SocketException e)
    {
        Console.WriteLine($"rebind {round}: {e.SocketErrorCode}");
    }
}

// Set explicitly, the option reads back (on Unix .NET sets SO_REUSEPORT and SO_REUSEADDR, and reads SO_REUSEPORT).
using (var shared = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
{
    shared.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    Console.WriteLine("reuse address set: " + ((int)shared.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0));
}

using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
udp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
Console.WriteLine("udp reuse address after bind: " + ((int)udp.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0));
