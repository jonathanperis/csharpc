// [ThreadStatic] fields: one copy per thread; references stay alive across GCs.
using System.Threading;

Context.Name = "main";
Context.Depth = 1;
var results = new string[4];
var threads = new Thread[4];
for (int t = 0; t < threads.Length; t++)
{
    int id = t;
    threads[t] = new Thread(() =>
    {
        // Thread-statics start out default on every new thread.
        string before = (Context.Name ?? "null") + "/" + Context.Depth;
        Context.Name = "worker" + id;
        Context.Items = new int[1000];
        for (int i = 0; i < 1000; i++) Context.Items[i] = i * id;
        for (int i = 0; i < 20_000; i++) _ = new byte[64];   // churn: GC must keep Context.Items alive
        GC.Collect();
        long sum = 0;
        foreach (var x in Context.Items) sum += x;
        results[id] = $"{before} -> {Context.Name} {sum}";
    });
    threads[t].Start();
}
foreach (var t in threads) t.Join();
foreach (var r in results) Console.WriteLine(r);
Console.WriteLine($"main still sees {Context.Name}/{Context.Depth}");

static class Context
{
    [ThreadStatic] public static string? Name;
    [ThreadStatic] public static int Depth;
    [ThreadStatic] public static int[]? Items;
}
