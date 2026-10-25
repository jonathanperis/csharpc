// System.Threading.Lock under contention and in every entry form: four threads incrementing shared
// counters through lock statements, Enter/Exit and EnterScope, recursion, TryEnter with and without
// timeouts against a lock another thread holds, IsHeldByCurrentThread, Exit by a thread that doesn't
// hold the lock, and Monitor on a Lock instance being a separate lock.
using System;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
    static readonly Lock s_lock = new();
    static long s_counter;

    static void Main()
    {
        var other = new Lock();
        long otherCounter = 0;
        var workers = new Task[4];
        for (int w = 0; w < workers.Length; w++)
        {
            int id = w;
            workers[w] = Task.Run(() =>
            {
                for (int i = 0; i < 50000; i++)
                {
                    switch ((i + id) % 3)
                    {
                        case 0: lock (s_lock) s_counter++; break;
                        case 1:
                            s_lock.Enter();
                            try { s_lock.Enter(); s_counter++; s_lock.Exit(); }
                            finally { s_lock.Exit(); }
                            break;
                        default:
                            using (s_lock.EnterScope()) s_counter++;
                            break;
                    }
                    if (i % 7 == 0) { using (other.EnterScope()) otherCounter += 2; }
                }
            });
        }
        Task.WaitAll(workers);
        Console.WriteLine($"{s_counter} {otherCounter} {s_lock.IsHeldByCurrentThread}");

        var held = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            s_lock.Enter();
            held.Set();
            release.Wait();
            s_lock.Exit();
        });
        holder.Start();
        held.Wait();
        Console.WriteLine($"try now: {s_lock.TryEnter()}, try 50 ms: {s_lock.TryEnter(50)}, held here: {s_lock.IsHeldByCurrentThread}");
        try { s_lock.Exit(); } catch (SynchronizationLockException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
        // Monitor on a Lock instance is the object's monitor, not the Lock: it is free.
#pragma warning disable CS9216
        object asObject = s_lock;
        Console.WriteLine($"monitor: {Monitor.TryEnter(asObject)}");
        Monitor.Exit(asObject);
#pragma warning restore CS9216
        var waiter = Task.Run(() => { s_lock.Enter(); bool mine = s_lock.IsHeldByCurrentThread; s_lock.Exit(); return mine; });
        Thread.Sleep(50);
        Console.WriteLine($"waiter done before release: {waiter.IsCompleted}");
        release.Set();
        Console.WriteLine($"waiter got it: {waiter.Result}");
        holder.Join();
        Console.WriteLine($"try after: {s_lock.TryEnter(-1)} {s_lock.IsHeldByCurrentThread}");
        s_lock.Exit();
        Console.WriteLine(s_lock.IsHeldByCurrentThread);
    }
}
