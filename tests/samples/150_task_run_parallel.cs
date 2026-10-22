// Task.Run work runs concurrently on the thread pool; results are combined deterministically.
using System.Threading;

const int N = 64;
long counter = 0;
var tasks = new Task<long>[N];
for (int i = 0; i < N; i++)
{
    int k = i;
    tasks[i] = Task.Run(() =>
    {
        long sum = 0;
        for (int j = 0; j <= 100_000; j++) sum += j % (k + 1);
        Interlocked.Increment(ref counter);
        return sum;
    });
}
long[] results = await Task.WhenAll(tasks);
Console.WriteLine($"results: {results.Length}, counter: {Interlocked.Read(ref counter)}");
Console.WriteLine($"first: {results[0]}, last: {results[^1]}, total: {results.Sum()}");

// Task.Run(Action) and Task.Run(Func<Task<T>>) unwrap.
int done = 0;
await Task.Run(() => { Interlocked.Add(ref done, 5); });
int inner = await Task.Run(async () => { await Task.Delay(5); return 42; });
Console.WriteLine($"{done} {inner}");

// Exceptions thrown on the pool surface at await.
try { await Task.Run(() => { if (done > 0) throw new InvalidOperationException("pool failure"); return 1; }); }
catch (InvalidOperationException e) { Console.WriteLine($"caught: {e.Message}"); }

// .Result from a synchronous context blocks until the pool finishes.
var square = Task.Run(() => 12 * 12);
Console.WriteLine(square.Result);
