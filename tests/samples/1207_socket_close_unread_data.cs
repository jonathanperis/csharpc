// Closing a connected TCP socket that still holds unread received data (#116). A bare close() with unread data resets the
// connection, and the peer's kernel then throws away what it had received but not yet read (a TLS client that never read
// the server's session tickets lost its close_notify that way). .NET's NetworkStream shuts the connection down before it
// closes an owned socket, so the FIN follows the data already sent and the peer reads that data and then the end of the
// stream; disposing a Socket directly closes it as it is, and Close(0) is abortive.
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

static void Run(string name, Action<Socket, TcpClient> closeClient)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var tcp = new TcpClient();
    tcp.Connect((IPEndPoint)listener.LocalEndpoint);
    using TcpClient accepted = listener.AcceptTcpClient();
    Socket server = accepted.Client;
    Socket client = tcp.Client;

    // The server sends data the client never reads; wait until it has arrived.
    server.Send(new byte[100]);
    bool arrived = client.Poll(5_000_000, SelectMode.SelectRead);
    Thread.Sleep(50); // all 100 bytes, not just the first segment
    Console.WriteLine($"== {name}: unread data at the client = {arrived}");

    client.Send(new byte[] { 1, 2, 3 });
    closeClient(client, tcp);
    Thread.Sleep(200); // whatever the close sent has reached the server by now

    var buffer = new byte[16];
    string result;
    try
    {
        int first = server.Receive(buffer);
        int second = first == 0 ? 0 : server.Receive(buffer);
        result = $"read={first} then={second}";
    }
    catch (SocketException e)
    {
        result = "SocketException " + e.SocketErrorCode;
    }
    Console.WriteLine("  server: " + result);
    listener.Stop();
}

Run("Socket.Dispose", (s, t) => s.Dispose());
Run("Socket.Close(0)", (s, t) => s.Close(0));
Run("Socket.Shutdown then Dispose", (s, t) => { s.Shutdown(SocketShutdown.Both); s.Dispose(); });
Run("TcpClient.Dispose after GetStream", (s, t) => { t.GetStream(); t.Dispose(); });
Run("NetworkStream.Dispose", (s, t) => t.GetStream().Dispose());
Run("NetworkStream that does not own the socket", (s, t) => { new NetworkStream(s, false).Dispose(); s.Dispose(); });
Run("TcpClient.Dispose without a stream", (s, t) => t.Dispose());
