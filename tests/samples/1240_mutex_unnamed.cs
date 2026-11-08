// System.Threading.Mutex without a name: ownership by thread, recursion, ReleaseMutex errors, AbandonedMutexException
// when the owner thread ends, composition with other wait handles (WaitAny/WaitAll), disposal and argument checks.
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
        var m = new Mutex();
        WaitHandle asHandle = m;
        Console.WriteLine(asHandle.WaitOne(0) + " " + ((object)asHandle is Mutex));
        asHandle.Dispose();
        m = new Mutex();
        Console.WriteLine(m.GetType().FullName);
        Try("release unowned", () => m.ReleaseMutex());

        // Recursion by the owning thread: every WaitOne needs its own ReleaseMutex.
        Console.WriteLine(m.WaitOne());
        Console.WriteLine(m.WaitOne(0));
        Console.WriteLine(m.WaitOne(TimeSpan.Zero));
        m.ReleaseMutex();
        m.ReleaseMutex();
        m.ReleaseMutex();
        Try("release one too many", () => m.ReleaseMutex());

        // initiallyOwned
        var owned = new Mutex(true);
        owned.ReleaseMutex();
        Try("release after initial owner", () => owned.ReleaseMutex());
        var notOwned = new Mutex(false);
        Try("release not owned", () => notOwned.ReleaseMutex());

        // Another thread: can't release, can't acquire while the main thread owns it, can once it is released.
        var shared = new Mutex(true);
        var attempted = new ManualResetEvent(false);
        var worker = new Thread(() =>
        {
            Try("worker release", () => shared.ReleaseMutex());
            Console.WriteLine("worker WaitOne(0): " + shared.WaitOne(0));
            Console.WriteLine("worker WaitOne(5): " + shared.WaitOne(5));
            attempted.Set();
            shared.WaitOne();
            Console.WriteLine("worker acquired");
            shared.ReleaseMutex();
        });
        worker.Start();
        attempted.WaitOne();
        Console.WriteLine("main releasing");
        shared.ReleaseMutex();
        worker.Join();
        Console.WriteLine("main WaitOne(0): " + shared.WaitOne(0));
        shared.ReleaseMutex();

        // The owner ends without releasing: the next wait acquires it and throws AbandonedMutexException.
        var abandoned = new Mutex();
        var owner = new Thread(() => { abandoned.WaitOne(); });
        owner.Start();
        owner.Join();
        try
        {
            abandoned.WaitOne();
            Console.WriteLine("no exception");
        }
        catch (AbandonedMutexException e)
        {
            Console.WriteLine("abandoned: " + e.MutexIndex + " " + (e.Mutex == null) + " " + e.Message);
        }
        Console.WriteLine("owned after abandoned: " + abandoned.WaitOne(0));
        abandoned.ReleaseMutex();
        abandoned.ReleaseMutex();

        // A recursive owner: abandoned however deep.
        var deep = new Mutex();
        var deepOwner = new Thread(() => { deep.WaitOne(); deep.WaitOne(); deep.WaitOne(); });
        deepOwner.Start();
        deepOwner.Join();
        try { deep.WaitOne(0); Console.WriteLine("no exception"); }
        catch (AbandonedMutexException) { Console.WriteLine("deep abandoned"); }
        deep.ReleaseMutex();
        Try("deep release again", () => deep.ReleaseMutex());

        // Released before the thread ends: not abandoned.
        var clean = new Mutex();
        var cleanOwner = new Thread(() => { clean.WaitOne(); clean.ReleaseMutex(); });
        cleanOwner.Start();
        cleanOwner.Join();
        Console.WriteLine("clean: " + clean.WaitOne(0));
        clean.ReleaseMutex();

        // WaitAny / WaitAll with other handles.
        var a = new Mutex();
        var b = new ManualResetEvent(true);
        Console.WriteLine("WaitAny: " + WaitHandle.WaitAny(new WaitHandle[] { a, b }));
        a.ReleaseMutex();
        Console.WriteLine("WaitAll: " + WaitHandle.WaitAll(new WaitHandle[] { a, b }));
        a.ReleaseMutex();
        var notSignaled = new ManualResetEvent(false);
        Console.WriteLine("WaitAll with an unsignaled event: " + WaitHandle.WaitAll(new WaitHandle[] { a, notSignaled }, 0));
        Console.WriteLine("a free again: " + a.WaitOne(0));
        a.ReleaseMutex();
        // WaitAny reports the abandoned handle's index, WaitAll none. `held` is owned by a thread that is still running.
        var held = new Mutex();
        var holding = new ManualResetEvent(false);
        var letGo = new ManualResetEvent(false);
        var holder = new Thread(() => { held.WaitOne(); holding.Set(); letGo.WaitOne(); held.ReleaseMutex(); });
        holder.Start();
        holding.WaitOne();
        var right = new Mutex();
        var both = new Thread(() => { right.WaitOne(); });
        both.Start();
        both.Join();
        try { WaitHandle.WaitAny(new WaitHandle[] { held, right }); Console.WriteLine("no exception"); }
        catch (AbandonedMutexException e) { Console.WriteLine("WaitAny abandoned: " + e.MutexIndex + " " + (e.Mutex == right)); }
        right.ReleaseMutex();
        letGo.Set();
        holder.Join();
        var left = new Mutex();
        var rightAgain = new Mutex();
        var both2 = new Thread(() => { rightAgain.WaitOne(); });
        both2.Start();
        both2.Join();
        try { WaitHandle.WaitAll(new WaitHandle[] { left, rightAgain }); Console.WriteLine("no exception"); }
        catch (AbandonedMutexException e) { Console.WriteLine("WaitAll abandoned: " + e.MutexIndex + " " + (e.Mutex == null)); }
        Console.WriteLine("both owned: " + left.WaitOne(0) + " " + rightAgain.WaitOne(0));
        // Held by another thread: neither WaitAny nor WaitAll gets it, and WaitAll leaves the others untouched.
        var heldByMain = new Mutex(true);
        var semaphore = new Semaphore(1, 1);
        var other = new Thread(() =>
        {
            Console.WriteLine("other WaitOne(10): " + heldByMain.WaitOne(10));
            Console.WriteLine("other WaitAny: " + WaitHandle.WaitAny(new WaitHandle[] { heldByMain, semaphore }, 10));
            semaphore.Release();
            Console.WriteLine("other WaitAll: " + WaitHandle.WaitAll(new WaitHandle[] { heldByMain, semaphore }, 10));
            Console.WriteLine("semaphore still there: " + semaphore.WaitOne(0));
        });
        other.Start();
        other.Join();

        // Dispose and Close: later use throws ObjectDisposedException; disposing again is fine.
        var disposed = new Mutex();
        disposed.Dispose();
        Try("disposed WaitOne", () => disposed.WaitOne());
        Try("disposed ReleaseMutex", () => disposed.ReleaseMutex());
        disposed.Dispose();
        disposed.Close();
        using (var scoped = new Mutex()) { scoped.WaitOne(); scoped.ReleaseMutex(); }
        IDisposable viaInterface = new Mutex();
        viaInterface.Dispose();
        Try("disposed through IDisposable", () => ((Mutex)viaInterface).WaitOne(0));

        // Argument checks.
        Try("WaitOne(-2)", () => m.WaitOne(-2));
        Try("WaitOne(TimeSpan -2 ms)", () => m.WaitOne(TimeSpan.FromMilliseconds(-2)));
        Console.WriteLine(m.WaitOne(10, false));
        m.ReleaseMutex();
        Try("WaitAny null", () => WaitHandle.WaitAny(null));
        Try("WaitAll empty", () => WaitHandle.WaitAll(new WaitHandle[0]));
        Try("WaitAll duplicate", () => WaitHandle.WaitAll(new WaitHandle[] { m, m }));

        // The exception types.
        Console.WriteLine(new AbandonedMutexException().Message);
        Console.WriteLine(new AbandonedMutexException(3, m).MutexIndex + " " + (new AbandonedMutexException(3, m).Mutex == m));
        Console.WriteLine(new AbandonedMutexException("custom").MutexIndex);
        Console.WriteLine(new AbandonedMutexException("custom", 2, m).Message);
        Console.WriteLine((object)new AbandonedMutexException() is SystemException);
        Console.WriteLine(new AbandonedMutexException().HResult.ToString("X8"));
        Console.WriteLine(new ApplicationException().Message);
        Console.WriteLine((object)new ApplicationException("x") is Exception);
        Console.WriteLine(new SemaphoreFullException().Message);
        Console.WriteLine(new WaitHandleCannotBeOpenedException().Message);
        Console.WriteLine((object)new WaitHandleCannotBeOpenedException() is ApplicationException);

        // Several threads taking turns: the mutex serializes a read-modify-write.
        var turn = new Mutex();
        int counter = 0;
        var threads = new Thread[4];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                for (int k = 0; k < 2000; k++)
                {
                    turn.WaitOne();
                    int v = counter;
                    counter = v + 1;
                    turn.ReleaseMutex();
                }
            });
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();
        Console.WriteLine("counter: " + counter);
    }
}
