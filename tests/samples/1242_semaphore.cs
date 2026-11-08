// System.Threading.Semaphore (the unnamed, wait-handle semaphore): counts, Release and its errors, SemaphoreFullException,
// WaitAny/WaitAll with other wait handles, disposal and a bounded-producer hand-off between threads.
using System;
using System.Threading;

class Program
{
    static void Try(string label, Action action)
    {
        try { action(); Console.WriteLine(label + ": ok"); }
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().FullName + ": " + e.Message.Replace("\n", "|").Replace("\r", "")); }
    }

    static void Main()
    {
        Try("negative initial", () => new Semaphore(-1, 1));
        Try("zero maximum", () => new Semaphore(0, 0));
        Try("negative maximum", () => new Semaphore(0, -3));
        Try("initial above maximum", () => new Semaphore(2, 1));

        var s = new Semaphore(1, 3);
        Console.WriteLine(s.GetType().FullName);
        Console.WriteLine("take: " + s.WaitOne(0) + " " + s.WaitOne(0));
        Console.WriteLine("Release() returns the previous count: " + s.Release());
        Console.WriteLine("Release(2): " + s.Release(2));
        Try("Release above the maximum", () => s.Release());
        Try("Release(0)", () => s.Release(0));
        Try("Release(-1)", () => s.Release(-1));
        Try("Release(4)", () => s.Release(4));
        Console.WriteLine("count kept: " + s.WaitOne(0) + " " + s.WaitOne(0) + " " + s.WaitOne(0) + " " + s.WaitOne(0));
        Console.WriteLine(s.WaitOne(10));
        Console.WriteLine(s.WaitOne(TimeSpan.Zero));
        Try("WaitOne(-2)", () => s.WaitOne(-2));

        // No named semaphores on Unix in .NET: PlatformNotSupportedException. (Windows has them.)
        if (!OperatingSystem.IsWindows())
        {
            Try("named", () => new Semaphore(1, 1, "csharpc_1242_name"));
            Try("named, createdNew", () => { bool created; new Semaphore(1, 1, "csharpc_1242_name", out created); });
            Try("OpenExisting", () => Semaphore.OpenExisting("csharpc_1242_name"));
            Try("TryOpenExisting", () => { Semaphore opened; Semaphore.TryOpenExisting("csharpc_1242_name", out opened); });
            Try("OpenExisting null", () => Semaphore.OpenExisting(null));
            Try("OpenExisting empty", () => Semaphore.OpenExisting(""));
        }
        bool unnamedCreated;
        var unnamed = new Semaphore(0, 1, null, out unnamedCreated);
        Console.WriteLine("unnamed through the named constructor: " + unnamedCreated + " " + unnamed.WaitOne(0));
        if (!OperatingSystem.IsWindows()) Try("empty name", () => new Semaphore(1, 1, ""));

        // With other wait handles.
        var empty = new Semaphore(0, 2);
        var one = new Semaphore(1, 2);
        var gate = new ManualResetEvent(true);
        Console.WriteLine("WaitAny: " + WaitHandle.WaitAny(new WaitHandle[] { empty, one, gate }));
        Console.WriteLine("WaitAny again: " + WaitHandle.WaitAny(new WaitHandle[] { empty, one, gate }));
        one.Release();
        Console.WriteLine("WaitAll: " + WaitHandle.WaitAll(new WaitHandle[] { one, gate }));
        Console.WriteLine("WaitAll, one is empty: " + WaitHandle.WaitAll(new WaitHandle[] { one, gate, empty }, 5));
        Console.WriteLine("nothing was taken: " + empty.WaitOne(0));
        empty.Release();
        Console.WriteLine("WaitAll, all there: " + WaitHandle.WaitAll(new WaitHandle[] { empty, gate }, 5));
        var mutex = new Mutex();
        var pair = new Semaphore(1, 1);
        Console.WriteLine("with a mutex: " + WaitHandle.WaitAll(new WaitHandle[] { mutex, pair }));
        mutex.ReleaseMutex();
        pair.Release();

        // Disposal.
        var disposed = new Semaphore(1, 1);
        disposed.Dispose();
        Try("disposed WaitOne", () => disposed.WaitOne(0));
        Try("disposed Release", () => disposed.Release());
        disposed.Dispose();
        disposed.Close();
        using (var scoped = new Semaphore(1, 1)) { scoped.WaitOne(); scoped.Release(); }
        IDisposable viaInterface = new Semaphore(1, 1);
        viaInterface.Dispose();
        Try("disposed through IDisposable", () => ((Semaphore)viaInterface).WaitOne(0));

        // A limit of two concurrent users: the semaphore never lets a third in.
        var limit = new Semaphore(2, 2);
        int inside = 0, maximum = 0, done = 0;
        var threads = new Thread[6];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                for (int k = 0; k < 200; k++)
                {
                    limit.WaitOne();
                    int now = Interlocked.Increment(ref inside);
                    int seen;
                    while (now > (seen = Volatile.Read(ref maximum)))
                        if (Interlocked.CompareExchange(ref maximum, now, seen) == seen) break;
                    Thread.SpinWait(50);
                    Interlocked.Decrement(ref inside);
                    limit.Release();
                    Interlocked.Increment(ref done);
                }
            });
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();
        Console.WriteLine("done: " + done + ", never more than two inside: " + (maximum <= 2) + ", at the end: " + inside);
        Console.WriteLine("all permits back: " + limit.WaitOne(0) + " " + limit.WaitOne(0) + " " + limit.WaitOne(0));

        // Hand-off: the consumer's WaitOne returns once the producer releases.
        var items = new Semaphore(0, 10);
        var results = new int[5];
        var consumer = new Thread(() =>
        {
            for (int i = 0; i < results.Length; i++)
            {
                items.WaitOne();
                results[i] = i * 10;
            }
        });
        consumer.Start();
        for (int i = 0; i < results.Length; i++) items.Release();
        consumer.Join();
        Console.WriteLine(string.Join(",", results));
    }
}
