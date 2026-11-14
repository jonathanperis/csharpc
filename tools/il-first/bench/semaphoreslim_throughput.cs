// SemaphoreSlim throughput, the program behind the numbers of the SemaphoreSlim row of docs/il-first/System.Threading.md (#412):
// uncontended Wait/Release, uncontended WaitAsync, a contended hand-off between threads (Wait + Release, spinning and blocking),
// and async waiters queued and released in bulk. The checksum counts acquisitions only, so it is independent of scheduling.
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        long check = 0;

        var gate = new SemaphoreSlim(1, 1);
        for (int i = 0; i < 5_000_000; i++) { gate.Wait(); gate.Release(); }
        check += gate.CurrentCount;

        for (int i = 0; i < 2_000_000; i++) { await gate.WaitAsync(); gate.Release(); }
        check += gate.CurrentCount;

        // Ping-pong between two threads: each side waits on its own semaphore and releases the other's.
        var ping = new SemaphoreSlim(0, 1);
        var pong = new SemaphoreSlim(0, 1);
        const int rounds = 200_000;
        var other = new Thread(() => { for (int i = 0; i < rounds; i++) { ping.Wait(); pong.Release(); } });
        other.Start();
        for (int i = 0; i < rounds; i++) { ping.Release(); pong.Wait(); check++; }
        other.Join();

        // Async waiters queued, then released in bulk.
        var pool = new SemaphoreSlim(0, int.MaxValue);
        for (int round = 0; round < 200; round++)
        {
            var waiters = new Task[1_000];
            for (int i = 0; i < waiters.Length; i++) waiters[i] = pool.WaitAsync();
            pool.Release(waiters.Length);
            await Task.WhenAll(waiters);
            check += waiters.Length;
        }

        Console.WriteLine(check);
    }
}
