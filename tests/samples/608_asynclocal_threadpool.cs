// AsyncLocal<T> flows into awaits, Task.Run, threads and QueueUserWorkItem (not into
// UnsafeQueueUserWorkItem); changes inside an async method don't leak to its caller; ThreadPool
// work items, generic state and IThreadPoolWorkItem; and the current culture alongside AsyncLocals.
#nullable disable
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

sealed class WorkItem : IThreadPoolWorkItem
{
    private readonly ManualResetEventSlim _done;
    public int Seen = -1;
    public WorkItem(ManualResetEventSlim done) { _done = done; }
    public void Execute() { Seen = Program.Local.Value; _done.Set(); }
}

class Program
{
    public static readonly AsyncLocal<int> Local = new AsyncLocal<int>();
    static readonly AsyncLocal<string> Name = new AsyncLocal<string>();

    static async Task Inner()
    {
        Console.WriteLine($"inner sees {Local.Value} {Name.Value ?? "null"}");
        Local.Value = 99;
        Name.Value = "inner";
        await Task.Yield();
        Console.WriteLine($"inner after await {Local.Value} {Name.Value}");
    }

    static async Task Main()
    {
        Console.WriteLine($"{Local.Value} {Name.Value ?? "null"}");
        Local.Value = 1;
        Name.Value = "main";
        await Inner();
        Console.WriteLine($"caller keeps {Local.Value} {Name.Value}");

        int seen = await Task.Run(() => Local.Value);
        Console.WriteLine($"Task.Run sees {seen}");

        int threadSeen = -1;
        var thread = new Thread(() => { threadSeen = Local.Value; Local.Value = 5; });
        thread.Start();
        thread.Join();
        Console.WriteLine($"thread sees {threadSeen}, main still {Local.Value}");

        Local.Value = 2;
        using var done = new ManualResetEventSlim();
        int poolSeen = -1;
        ThreadPool.QueueUserWorkItem(_ => { poolSeen = Local.Value; done.Set(); });
        done.Wait();
        Console.WriteLine($"QueueUserWorkItem sees {poolSeen}");

        done.Reset();
        int unsafeSeen = -1;
        ThreadPool.UnsafeQueueUserWorkItem(_ => { unsafeSeen = Local.Value; done.Set(); }, null);
        done.Wait();
        Console.WriteLine($"UnsafeQueueUserWorkItem sees {unsafeSeen}");

        done.Reset();
        string state = null;
        ThreadPool.QueueUserWorkItem(s => { state = s + Local.Value; done.Set(); }, "state-", preferLocal: false);
        done.Wait();
        Console.WriteLine(state);

        done.Reset();
        var item = new WorkItem(done);
        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: true);
        done.Wait();
        Console.WriteLine($"work item sees {item.Seen}");

        try { ThreadPool.UnsafeQueueUserWorkItem((IThreadPoolWorkItem)null, false); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        try { ThreadPool.QueueUserWorkItem(null); }
        catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }

        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        Local.Value = 3;
        var (culture, value) = await Task.Run(() => (CultureInfo.CurrentCulture.Name, Local.Value));
        Console.WriteLine($"{culture} {value} {1.5.ToString(CultureInfo.InvariantCulture)}");
        Local.Value = 0;
        Console.WriteLine($"{Local.Value} {CultureInfo.CurrentCulture.Name}");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
