// Named and anonymous pipes from System.IO.Pipes: a server and a client in one process, over a Unix domain socket on Unix.
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

var name = "csharpc-pipe-" + Environment.ProcessId;
using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
{
    var accept = server.WaitForConnectionAsync();
    await client.ConnectAsync(5000);
    await accept;
    Console.WriteLine($"connected: server {server.IsConnected}, client {client.IsConnected}");
    Console.WriteLine($"can read/write: {client.CanRead}/{client.CanWrite}, transmission {client.TransmissionMode}");

    var request = Encoding.UTF8.GetBytes("ping from client");
    await client.WriteAsync(request);
    var buffer = new byte[64];
    var read = await server.ReadAsync(buffer);
    Console.WriteLine($"server got: {Encoding.UTF8.GetString(buffer, 0, read)}");

    using (var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true })
    using (var reader = new StreamReader(client, leaveOpen: true))
    {
        await writer.WriteLineAsync("pong from server");
        Console.WriteLine($"client got: {await reader.ReadLineAsync()}");
    }

    server.Disconnect();
    Console.WriteLine($"after disconnect: server {server.IsConnected}");
    Console.WriteLine($"client read after disconnect: {client.Read(buffer, 0, buffer.Length)}");
}

using (var parent = new AnonymousPipeServerStream(PipeDirection.Out))
using (var child = new AnonymousPipeClientStream(PipeDirection.In, parent.ClientSafePipeHandle))
{
    parent.Write(Encoding.ASCII.GetBytes("anonymous"));
    var bytes = new byte[9];
    var total = 0;
    while (total < bytes.Length) total += child.Read(bytes, total, bytes.Length - total);
    Console.WriteLine($"anonymous: {Encoding.ASCII.GetString(bytes)} ({parent.TransmissionMode}, {child.ReadMode})");
}

try { new NamedPipeClientStream(".", "csharpc-missing-" + Environment.ProcessId).Connect(50); }
catch (TimeoutException e) { Console.WriteLine($"no server: {e.GetType().Name}"); }
