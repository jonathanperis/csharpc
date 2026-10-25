// PosixSignalRegistration: handlers for a signal run newest first with one context; a
// canceled SIGTERM doesn't end the process, Dispose unregisters, raw signal numbers and argument checks.
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    [DllImport("libc")] static extern int kill(int pid, int sig);

    static void Main()
    {
        try { PosixSignalRegistration.Create(PosixSignal.SIGTERM, null); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

        using var seen = new SemaphoreSlim(0);
        using var first = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c =>
        {
            Console.WriteLine($"first {c.Signal} {c.Cancel}");
            c.Cancel = true;
            seen.Release();
        });
        var second = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c =>
        {
            Console.WriteLine($"second {c.Signal} {c.Cancel}");
        });
        kill(Environment.ProcessId, 15);
        seen.Wait();
        Console.WriteLine("still running");

        second.Dispose();
        second.Dispose();
        using var usr1 = PosixSignalRegistration.Create((PosixSignal)10, c => { Console.WriteLine($"raw {(int)c.Signal}"); c.Cancel = true; seen.Release(); });
        kill(Environment.ProcessId, 10);
        seen.Wait();
        Console.WriteLine("done");
    }
}
