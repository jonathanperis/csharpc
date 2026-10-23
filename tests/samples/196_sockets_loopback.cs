// Sockets over loopback: a TcpListener thread echoing to a TcpClient (dual-mode, so the peer is
// ::ffff:127.0.0.1), UDP datagrams, and a refused connection's SocketException.
using System.Net;
using System.Net.Sockets;
using System.Text;

// TCP echo over loopback: listener on an ephemeral port, client in this thread.
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var server = new Thread(() =>
{
    using var client = listener.AcceptTcpClient();
    var stream = client.GetStream();
    var buffer = new byte[256];
    int n = stream.Read(buffer, 0, buffer.Length);
    var reply = Encoding.UTF8.GetBytes("echo: " + Encoding.UTF8.GetString(buffer, 0, n));
    stream.Write(reply, 0, reply.Length);
});
server.Start();
using (var c = new TcpClient())
{
    c.Connect(IPAddress.Loopback, port);
    Console.WriteLine(c.Connected + " " + ((IPEndPoint)c.Client.RemoteEndPoint).Address);
    var s = c.GetStream();
    var msg = Encoding.UTF8.GetBytes("hello socket");
    s.Write(msg, 0, msg.Length);
    var buf = new byte[256];
    int total = 0, r;
    while ((r = s.Read(buf, total, buf.Length - total)) > 0) total += r;
    Console.WriteLine(Encoding.UTF8.GetString(buf, 0, total));
}
server.Join();
listener.Stop();

// UDP loopback
using (var udpServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
using (var udpClient = new UdpClient())
{
    var ep = (IPEndPoint)udpServer.Client.LocalEndPoint;
    udpClient.Send(new byte[] { 1, 2, 3 }, 3, ep);
    IPEndPoint? from = null;
    var got = udpServer.Receive(ref from);
    Console.WriteLine(string.Join(",", got) + " from " + from.Address);
}

// Connection refused: a port that was just free.
var probe = new TcpListener(IPAddress.Loopback, 0);
probe.Start();
int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
probe.Stop();
try { using var c2 = new TcpClient(); c2.Connect(IPAddress.Loopback, closedPort); }
catch (SocketException e)
{
    Console.WriteLine(e.SocketErrorCode + " " + (int)e.SocketErrorCode + " " + (e.ErrorCode == e.NativeErrorCode));
    Console.WriteLine(e.Message.Replace(closedPort.ToString(), "PORT"));
    Console.WriteLine(e is System.ComponentModel.Win32Exception);
}
var dual = new Socket(SocketType.Stream, ProtocolType.Tcp);
Console.WriteLine(dual.AddressFamily + " " + dual.DualMode);
dual.Dispose();
try { var x = dual.LocalEndPoint; } catch (ObjectDisposedException e) { Console.WriteLine(e.Message); }
Console.WriteLine(new SocketException(10061).Message);
Console.WriteLine(Dns.GetHostAddresses("127.0.0.1")[0]);
