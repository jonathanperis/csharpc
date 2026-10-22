// Proof of concurrency without printing anything scheduling-dependent: N tasks must all be inside
// the barrier at the same time, which is only possible if they run on different threads.
using System.Threading;

int mainId = Environment.CurrentManagedThreadId;   // before any await: the main thread
const int N = 4;
using var barrier = new CountdownEvent(N);
int finished = 0;
var tasks = Enumerable.Range(0, N).Select(i => Task.Run(() =>
{
    barrier.Signal();
    bool allArrived = barrier.Wait(5000);   // would time out on a single-threaded scheduler
    if (allArrived) Interlocked.Increment(ref finished);
})).ToArray();
await Task.WhenAll(tasks);
Console.WriteLine($"all {N} ran concurrently: {finished == N}");

// Continuations after await on a pending task resume on a pool thread (the main thread is blocked
// in async Main's GetResult), so we're no longer on the main thread.
await Task.Delay(10);
Console.WriteLine($"resumed off the main thread: {Environment.CurrentManagedThreadId != mainId}");

// Awaiting an already-completed task continues synchronously on the same thread.
int before = Environment.CurrentManagedThreadId;
await Task.CompletedTask;
Console.WriteLine($"completed await stays on thread: {Environment.CurrentManagedThreadId == before}");

// Blocking a pool thread on another task must not deadlock the pool.
var outer = Enumerable.Range(0, 32).Select(i => Task.Run(() => Task.Run(() => i * 2).Result)).ToArray();
Console.WriteLine((await Task.WhenAll(outer)).Sum());
