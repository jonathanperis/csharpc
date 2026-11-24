// Threading and GC members from #97: Thread's critical-region/affinity no-ops, Thread.Priority (kept and validated), GCSettings,
// ThreadPool.GetMinThreads, and type tests and casts against WaitHandle / EventWaitHandle.
using System;
using System.Runtime;
using System.Threading;

static class Program
{
    static void Try(string label, Action action)
    {
        try { action(); Console.WriteLine($"{label}: ok"); }
        catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        Thread.BeginCriticalRegion(); Thread.EndCriticalRegion(); Thread.BeginThreadAffinity(); Thread.EndThreadAffinity();
        Console.WriteLine("regions: ok");

        var worker = new Thread(() => { });
        Console.WriteLine($"priority: {worker.Priority}");
        worker.Priority = ThreadPriority.Highest;
        Console.WriteLine($"priority set: {worker.Priority}");
        Try("invalid priority", () => worker.Priority = (ThreadPriority)7);
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        Console.WriteLine($"current: {Thread.CurrentThread.Priority}");
        worker.Start(); worker.Join();
        Try("dead get", () => Console.WriteLine(worker.Priority));
        Try("dead set", () => worker.Priority = ThreadPriority.Lowest);

        Console.WriteLine($"server gc: {GCSettings.IsServerGC}, latency: {GCSettings.LatencyMode}");
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        Console.WriteLine($"latency set: {GCSettings.LatencyMode}");
        Try("NoGCRegion", () => GCSettings.LatencyMode = GCLatencyMode.NoGCRegion);
        Console.WriteLine($"loh: {GCSettings.LargeObjectHeapCompactionMode}");
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        Console.WriteLine($"loh set: {GCSettings.LargeObjectHeapCompactionMode}");
        GC.Collect();
        Console.WriteLine($"loh after a full collection: {GCSettings.LargeObjectHeapCompactionMode}");
        Try("bad loh", () => GCSettings.LargeObjectHeapCompactionMode = (GCLargeObjectHeapCompactionMode)5);

        ThreadPool.GetMinThreads(out var workers, out var io);
        Console.WriteLine($"min threads: workers = processors {workers == Environment.ProcessorCount}, io {io}");

        object[] handles = { new ManualResetEvent(false), new AutoResetEvent(true), new Mutex(), new Semaphore(1, 1), new SemaphoreSlim(1), "text" };
        foreach (var h in handles)
            Console.WriteLine($"{h.GetType().Name}: WaitHandle {h is WaitHandle}, EventWaitHandle {h is EventWaitHandle}");
        WaitHandle viaCast = (WaitHandle)handles[2];
        Console.WriteLine($"cast: {viaCast.GetType().Name}");
        Try("bad cast", () => Console.WriteLine((EventWaitHandle)handles[3]));
        object none = null!;
        Console.WriteLine($"null: {none is WaitHandle} {(WaitHandle)none == null}");
    }
}
