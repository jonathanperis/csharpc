// An exception that escapes a finalizer is unhandled: the process ends like any unhandled exception
// (abort, exit code 134 on Unix) from the finalizer thread, and the program never gets past the sleep.
using System.Runtime.CompilerServices;
using System.Threading;

Make();
Console.WriteLine("before");
GC.Collect();
GC.WaitForPendingFinalizers();
Thread.Sleep(30000);
Console.WriteLine("not reached");

[MethodImpl(MethodImplOptions.NoInlining)] static void Make() { for (int i = 0; i < 20; i++) _ = new Bad(); }

class Bad { ~Bad() { throw new InvalidOperationException("finalizer failed"); } }
