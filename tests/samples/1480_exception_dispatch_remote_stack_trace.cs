// ExceptionDispatchInfo.SetRemoteStackTrace: the text becomes the start of StackTrace and ToString(), followed by .NET's
// "--- End of stack trace from previous location ---" line; a second call, or a call on an exception already thrown, is an
// InvalidOperationException; null arguments are ArgumentNullExceptions. The Windows CoreLib's cancellable synchronous I/O builds its
// OperationCanceledException this way.
using System;
using System.Runtime.ExceptionServices;

class Program
{
    static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");

    static void Main()
    {
        var fresh = new InvalidOperationException("remote");
        Console.WriteLine(fresh.StackTrace == null);
        var returned = ExceptionDispatchInfo.SetRemoteStackTrace(fresh, "   at Remote.Server.Handle()");
        Console.WriteLine(ReferenceEquals(returned, fresh));
        Console.WriteLine(Escape(fresh.StackTrace!.Replace(Environment.NewLine, "\n")));
        Console.WriteLine(Escape(fresh.ToString().Replace(Environment.NewLine, "\n")));

        try { ExceptionDispatchInfo.SetRemoteStackTrace(fresh, "again"); }
        catch (InvalidOperationException e) { Console.WriteLine("second: " + e.GetType().Name + ": " + e.Message); }

        Exception thrown;
        try { throw new FormatException("thrown"); }
        catch (FormatException e) { thrown = e; }
        try { ExceptionDispatchInfo.SetRemoteStackTrace(thrown, "late"); }
        catch (InvalidOperationException e) { Console.WriteLine("thrown: " + e.GetType().Name + ": " + e.Message); }

        try { ExceptionDispatchInfo.SetRemoteStackTrace(null!, "x"); }
        catch (ArgumentNullException e) { Console.WriteLine("null source: " + e.ParamName); }
        try { ExceptionDispatchInfo.SetRemoteStackTrace(new Exception(), null!); }
        catch (ArgumentNullException e) { Console.WriteLine("null trace: " + e.ParamName); }

        // Thrown after the remote text was set: the remote text first, then the frames from the throw.
        var later = ExceptionDispatchInfo.SetRemoteStackTrace(new ArgumentException("later"), "   at Remote.Worker.Run()");
        try { throw later; }
        catch (ArgumentException e)
        {
            string trace = e.StackTrace!.Replace(Environment.NewLine, "\n");
            Console.WriteLine(trace.StartsWith("   at Remote.Worker.Run()\n--- End of stack trace from previous location ---\n   at Program.Main()", StringComparison.Ordinal));
        }
    }
}
