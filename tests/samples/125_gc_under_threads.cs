// Allocation-heavy work on 4 threads at once: binary trees per thread, stressing the GC's
// stop-the-world handshake, per-thread allocation caches and stack scanning.
using System.Threading;

var checks = new long[4];
var threads = new Thread[4];
for (int t = 0; t < threads.Length; t++)
{
    int id = t;
    threads[t] = new Thread(() =>
    {
        long check = 0;
        var keep = Tree.Build(12);              // long-lived tree on this thread's stack
        for (int round = 0; round < 40; round++)
        {
            var tree = Tree.Build(14 + id % 2);
            check += tree.Check();
        }
        checks[id] = check + keep.Check();
    });
    threads[t].Start();
}
var mainTree = Tree.Build(16);
foreach (var t in threads) t.Join();
for (int t = 0; t < checks.Length; t++) Console.WriteLine($"thread {t}: {checks[t]}");
Console.WriteLine($"main: {mainTree.Check()}");

class Tree
{
    public Tree? Left, Right;
    public static Tree Build(int depth) =>
        depth == 0 ? new Tree() : new Tree { Left = Build(depth - 1), Right = Build(depth - 1) };
    public int Check() => 1 + (Left?.Check() ?? 0) + (Right?.Check() ?? 0);
}
