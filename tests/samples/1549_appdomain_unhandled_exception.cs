// AppDomain.UnhandledException (#97): raised once with IsTerminating = true and a null sender, before the runtime reports the
// exception and terminates; a removed handler is not called. Also AppDomain.IsFinalizingForUnload.
using System;

static class Program
{
    static void Removed(object sender, UnhandledExceptionEventArgs e) => Console.WriteLine("removed handler called");

    static void Main()
    {
        Console.WriteLine($"finalizing for unload: {AppDomain.CurrentDomain.IsFinalizingForUnload()}");
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = (Exception)e.ExceptionObject;
            Console.WriteLine($"first: sender {(sender == null ? "null" : "set")} {exception.GetType().Name}: {exception.Message} terminating={e.IsTerminating}");
        };
        AppDomain.CurrentDomain.UnhandledException += Removed;
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => Console.WriteLine("second handler");
        AppDomain.CurrentDomain.UnhandledException -= Removed;
        Console.WriteLine("throwing");
        throw new InvalidOperationException("nobody catches this");
    }
}
