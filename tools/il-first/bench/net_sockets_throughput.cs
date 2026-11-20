// System.Net.Sockets throughput over loopback, the program behind the measurements of docs/il-first/System.Net.Sockets.md
// (tools/il-first/measure-aot.sh): asynchronous ping-pong of small messages (the socket engine's readiness path), a bulk transfer
// through NetworkStream, synchronous Send/Receive, and name resolution of "localhost". The output is checksums only.
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
listener.Listen(16);
var endPoint = (IPEndPoint)listener.LocalEndPoint!;
long check = 0;

// 1. Ping-pong: 100 000 round trips of 64 bytes, both ends asynchronous.
{
    var accept = listener.AcceptAsync();
    using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    await client.ConnectAsync(endPoint);
    using var server = await accept;
    server.NoDelay = true;
    var echo = Task.Run(async () =>
    {
        var buffer = new byte[64];
        while (true)
        {
            int n = await server.ReceiveAsync(buffer, SocketFlags.None);
            if (n == 0) break;
            await server.SendAsync(buffer.AsMemory(0, n), SocketFlags.None);
        }
    });
    var message = new byte[64];
    var reply = new byte[64];
    for (int i = 0; i < 100_000; i++)
    {
        message[0] = (byte)i;
        await client.SendAsync(message, SocketFlags.None);
        int got = 0;
        while (got < 64) got += await client.ReceiveAsync(reply.AsMemory(got), SocketFlags.None);
        check += reply[0];
    }
    client.Shutdown(SocketShutdown.Send);
    await echo;
}

// 2. Bulk: 512 MiB through NetworkStream in 64 KiB writes.
{
    var accept = listener.AcceptAsync();
    using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    await client.ConnectAsync(endPoint);
    using var server = await accept;
    var reader = Task.Run(async () =>
    {
        using var stream = new NetworkStream(server, ownsSocket: false);
        var buffer = new byte[65536];
        long total = 0, sum = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer)) > 0)
        {
            total += n;
            sum += buffer[n - 1];
        }
        return total + sum;
    });
    using (var stream = new NetworkStream(client, ownsSocket: false))
    {
        var chunk = new byte[65536];
        for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)i;
        for (int i = 0; i < 8192; i++) await stream.WriteAsync(chunk);
    }
    client.Shutdown(SocketShutdown.Send);
    check += await reader;
}

// 3. Synchronous Send/Receive: 50 000 round trips of 16 bytes.
{
    using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    client.Connect(endPoint);
    using var server = listener.Accept();
    server.NoDelay = true;
    var buffer = new byte[16];
    var back = new byte[16];
    for (int i = 0; i < 50_000; i++)
    {
        buffer[1] = (byte)(i >> 3);
        client.Send(buffer);
        int n = server.Receive(back);
        server.Send(back, 0, n, SocketFlags.None);
        check += client.Receive(back) + back[1];
    }
}

// 4. Name resolution: 2 000 lookups of "localhost".
for (int i = 0; i < 2_000; i++)
    check += Dns.GetHostAddresses("localhost").Length;

Console.WriteLine(check);
