// async iterators (IAsyncEnumerable<T>) and await foreach: coroutines that both await and yield.
await foreach (var x in Gen(3)) Console.WriteLine(x);
await foreach (var w in Words())
{
    Console.WriteLine(w);
    if (w == "b") break;
}
int total = 0;
await foreach (var v in Gen(5)) total += v;
Console.WriteLine(total);
static async IAsyncEnumerable<int> Gen(int n) { for (int i = 0; i < n; i++) { await Task.Delay(1); yield return i * 10; } }
static async IAsyncEnumerable<string> Words()
{
    try { yield return "a"; await Task.Yield(); yield return "b"; yield return "c"; }
    finally { Console.WriteLine("async cleanup"); }
}
