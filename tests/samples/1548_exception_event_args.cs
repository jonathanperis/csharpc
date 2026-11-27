// ThreadExceptionEventArgs and UnhandledExceptionEventArgs with their handlers (#95, #97), served from CoreLib's IL.
using System;
using System.Threading;

static class Program
{
    static void Main()
    {
        var threadArgs = new ThreadExceptionEventArgs(new InvalidOperationException("boom"));
        Console.WriteLine($"{threadArgs.Exception.GetType().Name}: {threadArgs.Exception.Message} {threadArgs is EventArgs}");
        ThreadExceptionEventHandler onThread = (sender, e) => Console.WriteLine($"thread handler: {sender} {e.Exception.Message}");
        onThread("form", threadArgs);

        var unhandled = new UnhandledExceptionEventArgs(new FormatException("bad"), isTerminating: true);
        Console.WriteLine($"{unhandled.ExceptionObject.GetType().Name}: {((Exception)unhandled.ExceptionObject).Message} terminating={unhandled.IsTerminating}");
        UnhandledExceptionEventHandler onUnhandled = (sender, e) => Console.WriteLine($"unhandled handler: {e.ExceptionObject} {e.IsTerminating}");
        onUnhandled(null!, new UnhandledExceptionEventArgs("not an exception", false));
        try { new ThreadExceptionEventArgs(null!); Console.WriteLine("null accepted"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }
    }
}
