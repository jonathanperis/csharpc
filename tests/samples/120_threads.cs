// System.Threading.Thread: start, parameters, join, names, background, current thread.
using System.Threading;

long[] partial = new long[4];
var threads = new Thread[4];
for (int t = 0; t < threads.Length; t++)
{
    int id = t;
    threads[t] = new Thread(() =>
    {
        long sum = 0;
        for (int i = id * 250_000; i < (id + 1) * 250_000; i++) sum += i;
        partial[id] = sum;
    });
    threads[t].Name = "worker-" + t;
}
foreach (var th in threads) th.Start();
foreach (var th in threads) th.Join();
long total = 0;
foreach (var p in partial) total += p;
Console.WriteLine($"sum = {total}");
Console.WriteLine(threads[2].Name + " alive=" + threads[2].IsAlive);

string? received = null;
var withParameter = new Thread(o => received = "got " + o);
withParameter.Start(42);
withParameter.Join();
Console.WriteLine(received);

var named = new Thread(ShowName) { Name = "named-thread", IsBackground = true };
named.Start();
Console.WriteLine(named.Join(5000));
Console.WriteLine(Thread.CurrentThread.ManagedThreadId == 1);
Console.WriteLine(named.ManagedThreadId != 1);

var sleeper = new Thread(() => Thread.Sleep(50));
sleeper.Start();
Console.WriteLine(sleeper.Join(1) ? "finished early?" : "still sleeping");
sleeper.Join();
Console.WriteLine("joined sleeper");

try { withParameter.Start(1); }
catch (ThreadStateException e) { Console.WriteLine("ThreadStateException: " + e.Message); }

// A foreground thread keeps the process alive after Main returns.
new Thread(() => { Thread.Sleep(100); Console.WriteLine("last words from a foreground thread"); }).Start();
Console.WriteLine("main done");

static void ShowName() => Console.WriteLine("hello from " + Thread.CurrentThread.Name);
