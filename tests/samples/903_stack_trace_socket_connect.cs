// A refused asynchronous connect, as a database driver's connection attempt meets it: .NET throws the
// SocketException in AwaitableSocketAsyncEventArgs.ThrowException, called by the awaited ValueTask's
// GetResult, or by the Task overloads' completion callback, whose rethrow the await marks with
// "--- End of stack trace from previous location ---". Those frames precede the program's own.
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static int closedPort;

    static async Task Main()
    {
        // A port nothing listens on: a listener's, once closed.
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            closedPort = ((IPEndPoint)probe.LocalEndPoint!).Port;
        }
        var endPoint = new IPEndPoint(IPAddress.Loopback, closedPort);
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            try { await s.ConnectAsync(endPoint, CancellationToken.None); } catch (Exception e) { Report("ValueTask ConnectAsync(EndPoint, CancellationToken)", e); }
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            try { await s.ConnectAsync(endPoint); } catch (Exception e) { Report("Task ConnectAsync(EndPoint)", e); }
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            try { await s.ConnectAsync(IPAddress.Loopback, closedPort); } catch (Exception e) { Report("Task ConnectAsync(IPAddress, Int32)", e); }
        try { await OpenAsync(endPoint); } catch (Exception e) { Report("through async methods", e); }
    }

    static async Task OpenAsync(EndPoint endPoint)
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await ConnectCore(s, endPoint);
    }

    static async ValueTask ConnectCore(Socket s, EndPoint endPoint) => await s.ConnectAsync(endPoint, CancellationToken.None);

    static void Report(string name, Exception e)
    {
        Console.WriteLine("== " + name + ": " + ((SocketException)e).SocketErrorCode);
        Console.Write(Clean(e.StackTrace));
    }

    /// <summary>"   at M() in /some/dir/file.cs:line 3" -> "   at M() in file.cs:line 3", one line each.</summary>
    static string Clean(string? text)
    {
        if (text == null) return "<null>" + Environment.NewLine;
        var sb = new StringBuilder();
        foreach (var line in text.Split(Environment.NewLine))
        {
            var at = line.IndexOf(" in ", StringComparison.Ordinal);
            var cut = Math.Max(line.LastIndexOf('/'), line.LastIndexOf('\\'));
            sb.AppendLine(at >= 0 && cut > at ? line.Substring(0, at + 4) + line.Substring(cut + 1) : line);
        }
        return sb.ToString();
    }
}
