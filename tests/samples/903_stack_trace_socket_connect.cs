// A refused asynchronous connect, as a database driver's connection attempt meets it: the SocketException's
// trace ends with the program's own frames, through the awaits that rethrew it. Only those are printed: the
// frames before them depend on whether the refusal arrives before ConnectAsync returns, which varies from run to
// run under .NET (AwaitableSocketAsyncEventArgs.ThrowException from GetResult, or CreateException with the thread
// pool's frames and a "--- End of stack trace from previous location ---" marker; for the Task overloads
// ValueTask.GetTaskForValueTaskSource or ValueTaskSourceAsTask's completion callback, #378).
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
        Console.WriteLine("== " + name + ": " + e.GetType().Name + " " + ((SocketException)e).SocketErrorCode + ": " + e.Message);
        Console.Write(OwnFrames(e.StackTrace));
    }

    /// <summary>
    /// The program's frames with a line in the trace's last part (after its last "--- End of stack trace ..." marker),
    /// "   at M() in /some/dir/file.cs:line 3" -> "   at M() in file.cs:line 3".
    /// </summary>
    static string OwnFrames(string? text)
    {
        if (text == null) return "<null>" + Environment.NewLine;
        var lines = text.Split(Environment.NewLine);
        var start = Array.FindLastIndex(lines, l => l.StartsWith("--- ", StringComparison.Ordinal)) + 1;
        var sb = new StringBuilder();
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            var at = line.IndexOf(" in ", StringComparison.Ordinal);
            if (!line.StartsWith("   at Program.", StringComparison.Ordinal) || at < 0) continue;
            var cut = Math.Max(line.LastIndexOf('/'), line.LastIndexOf('\\'));
            sb.AppendLine(cut > at ? line.Substring(0, at + 4) + line.Substring(cut + 1) : line);
        }
        return sb.ToString();
    }
}
