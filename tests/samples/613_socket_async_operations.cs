// Asynchronous socket operations over loopback: AcceptAsync(CancellationToken), SocketAsyncEventArgs
// receives and sends (zero-byte "wait for data" receives, buffer lists, the Completed event and
// synchronous completions), ValueTask ReceiveAsync/SendAsync, cancellation, and Dispose aborting a
// pending receive with OperationAborted.
#nullable disable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Awaitable : SocketAsyncEventArgs
{
    private TaskCompletionSource<bool> _done;
    public Awaitable() : base(unsafeSuppressExecutionContextFlow: true) { }

    public Task Run(Func<SocketAsyncEventArgs, bool> start)
    {
        _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!start(this)) _done.SetResult(false);
        return _done.Task;
    }

    public bool Pending => _done.Task.IsCompleted && _done.Task.Result;

    protected override void OnCompleted(SocketAsyncEventArgs e) => _done.SetResult(true);
}

class Program
{
    static async Task Main()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(16);
        var port = ((IPEndPoint)listener.LocalEndPoint).Port;

        var acceptTask = listener.AcceptAsync(CancellationToken.None);
        Console.WriteLine($"accept pending: {!acceptTask.IsCompleted}");
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
        var server = await acceptTask;
        server.NoDelay = true;
        Console.WriteLine($"accepted {((IPEndPoint)server.RemoteEndPoint).Address} {server.Connected}");

        // A zero-byte receive waits until data arrives, then reports 0 bytes.
        var args = new Awaitable();
        args.SetBuffer(Memory<byte>.Empty);
        var waitForData = args.Run(server.ReceiveAsync);
        await Task.Delay(50);
        Console.WriteLine($"waiting: {!waitForData.IsCompleted}");
        client.Send(Encoding.ASCII.GetBytes("hello"));
        await waitForData;
        Console.WriteLine($"data ready: {args.SocketError} {args.BytesTransferred} {args.LastOperation}");

        var buffer = new byte[16];
        args.SetBuffer(buffer, 0, buffer.Length);
        await args.Run(server.ReceiveAsync);
        Console.WriteLine($"received {args.BytesTransferred}: {Encoding.ASCII.GetString(buffer, 0, args.BytesTransferred)}");

        // A buffer list goes out in order.
        args.SetBuffer(null, 0, 0);
        args.BufferList = new List<ArraySegment<byte>> { new(Encoding.ASCII.GetBytes("ab")), new(Encoding.ASCII.GetBytes("cde"), 1, 2) };
        await args.Run(server.SendAsync);
        Console.WriteLine($"sent {args.BytesTransferred} {args.SocketError}");
        args.BufferList = null;
        var got = new byte[8];
        int n = 0;
        while (n < 4) n += await client.ReceiveAsync(got.AsMemory(n), SocketFlags.None);
        Console.WriteLine(Encoding.ASCII.GetString(got, 0, n));

        try { args.SetBuffer(new byte[1], 0, 1); args.BufferList = new List<ArraySegment<byte>>(); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
        args.SetBuffer(null, 0, 0);

        // A larger payload: sends complete when everything is written.
        var big = new byte[1 << 20];
        for (int i = 0; i < big.Length; i++) big[i] = (byte)i;
        var sendBig = client.SendAsync(big.AsMemory(), SocketFlags.None).AsTask();
        long sum = 0, total = 0;
        var chunk = new byte[65536];
        while (total < big.Length)
        {
            int r = await server.ReceiveAsync(chunk, SocketFlags.None);
            for (int i = 0; i < r; i++) sum += chunk[i];
            total += r;
        }
        Console.WriteLine($"big: sent {await sendBig} received {total} sum {sum}");

        // Cancellation of a pending receive and accept.
        using (var cts = new CancellationTokenSource())
        {
            var pending = server.ReceiveAsync(new byte[4], SocketFlags.None, cts.Token);
            cts.Cancel();
            try { await pending; } catch (OperationCanceledException) { Console.WriteLine("receive canceled"); }
            var pendingAccept = listener.AcceptAsync(cts.Token);
            try { await pendingAccept; } catch (OperationCanceledException) { Console.WriteLine("accept canceled"); }
        }

        // The peer closing: a receive returns 0.
        client.Shutdown(SocketShutdown.Send);
        Console.WriteLine($"eof: {await server.ReceiveAsync(new byte[4], SocketFlags.None)}");

        // Disposing the socket aborts a pending operation.
        var aborted = new Awaitable();
        aborted.SetBuffer(new byte[4], 0, 4);
        var other = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connect = other.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
        var third = await listener.AcceptAsync(CancellationToken.None);
        await connect;
        var abortedTask = aborted.Run(third.ReceiveAsync);
        await Task.Delay(20);
        third.Dispose();
        await abortedTask;
        Console.WriteLine($"after dispose: {aborted.SocketError}");
        try { third.ReceiveAsync(aborted); } catch (ObjectDisposedException) { Console.WriteLine("disposed socket"); }

        other.Dispose();
        client.Dispose();
        server.Dispose();
    }
}
