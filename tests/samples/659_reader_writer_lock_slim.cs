// ReaderWriterLockSlim (dotnet/runtime's, ported): read/write/upgradeable locks, recursion policies and
// their exceptions, the Is*Held/count properties, timeouts, dispose rules, and a deterministic
// multi-threaded run.
using System;
using System.Threading;

class Program
{
    static void Try(string what, Action a)
    {
        try { a(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var rw = new ReaderWriterLockSlim();
        Console.WriteLine(rw.RecursionPolicy);
        rw.EnterReadLock();
        Console.WriteLine($"{rw.IsReadLockHeld} {rw.IsWriteLockHeld} {rw.IsUpgradeableReadLockHeld} {rw.CurrentReadCount} {rw.RecursiveReadCount}");
        Try("read again", () => rw.EnterReadLock());
        Try("write after read", () => rw.EnterWriteLock());
        Try("upgrade after read", () => rw.EnterUpgradeableReadLock());
        rw.ExitReadLock();
        Try("exit read unheld", () => rw.ExitReadLock());
        Try("exit write unheld", () => rw.ExitWriteLock());
        Try("exit upgrade unheld", () => rw.ExitUpgradeableReadLock());

        rw.EnterUpgradeableReadLock();
        rw.EnterWriteLock();
        Console.WriteLine($"{rw.IsUpgradeableReadLockHeld} {rw.IsWriteLockHeld} {rw.RecursiveWriteCount}");
        Try("read after write", () => rw.EnterReadLock());
        rw.ExitWriteLock();
        rw.ExitUpgradeableReadLock();
        Console.WriteLine(rw.TryEnterWriteLock(0) + " " + rw.IsWriteLockHeld);
        rw.ExitWriteLock();

        var rec = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        rec.EnterWriteLock(); rec.EnterWriteLock(); rec.EnterReadLock();
        Console.WriteLine($"{rec.RecursiveWriteCount} {rec.RecursiveReadCount}");
        rec.ExitReadLock(); rec.ExitWriteLock(); rec.ExitWriteLock();

        rw.EnterReadLock();
        Try("dispose while held", () => rw.Dispose());
        rw.ExitReadLock();
        rw.Dispose();
        Try("use after dispose", () => rw.EnterReadLock());

        // Writers serialize; readers see consistent pairs.
        var shared = new ReaderWriterLockSlim();
        int a = 0, b = 0, inconsistent = 0;
        var threads = new Thread[6];
        for (int t = 0; t < threads.Length; t++)
        {
            bool writer = t % 2 == 0;
            threads[t] = new Thread(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    if (writer) { shared.EnterWriteLock(); a++; b++; shared.ExitWriteLock(); }
                    else { shared.EnterReadLock(); if (a != b) Interlocked.Increment(ref inconsistent); shared.ExitReadLock(); }
                }
            });
            threads[t].Start();
        }
        foreach (var t in threads) t.Join();
        Console.WriteLine($"{a} {b} {inconsistent} {shared.WaitingReadCount} {shared.WaitingWriteCount}");
    }
}
