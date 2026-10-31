// NetworkStream's asynchronous reads and writes are the socket's asynchronous operations: the socket's
// ReceiveTimeout applies to synchronous reads only, so a ReadAsync whose data comes later than the timeout
// still completes (Npgsql sets ReceiveTimeout and then reads asynchronously). A synchronous read does time out.
using System.Net;
using System.Net.Sockets;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var client = new TcpClient();
await client.ConnectAsync(IPAddress.Loopback, port);
using var server = await listener.AcceptTcpClientAsync();
var stream = client.GetStream();
client.Client.ReceiveTimeout = 100;

var buffer = new byte[16];
var read = stream.ReadAsync(buffer, 0, buffer.Length);
await Task.Delay(400);
Console.WriteLine("pending after the timeout: " + !read.IsCompleted);
await server.GetStream().WriteAsync(new byte[] { 1, 2, 3 });
Console.WriteLine("read: " + await read);

var memory = new Memory<byte>(new byte[16]);
var readMemory = stream.ReadAsync(memory);
await server.GetStream().WriteAsync(new byte[] { 4, 5 });
Console.WriteLine("read memory: " + await readMemory);

var big = new byte[1 << 20];
for (int i = 0; i < big.Length; i++) big[i] = (byte)i;
var write = stream.WriteAsync(big);
var received = 0;
var chunk = new byte[65536];
long sum = 0;
var serverStream = server.GetStream();
while (received < big.Length)
{
    int n = await serverStream.ReadAsync(chunk);
    for (int i = 0; i < n; i++) sum += chunk[i];
    received += n;
}
await write;
Console.WriteLine($"server received {received} bytes, sum {sum}");

try { stream.Read(buffer, 0, buffer.Length); }
catch (IOException e) { Console.WriteLine($"{e.GetType().Name} ({((SocketException)e.InnerException!).SocketErrorCode})"); }

using var cts = new CancellationTokenSource();
var canceled = stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
cts.Cancel();
try { await canceled; }
catch (OperationCanceledException e) { Console.WriteLine("canceled: " + e.GetType().Name); }
client.Close();
listener.Stop();
