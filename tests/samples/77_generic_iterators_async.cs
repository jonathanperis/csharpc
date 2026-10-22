// Iterators and async methods under generics: monomorphized coroutines + interface dispatch.
var squares = Select(Where(Range(1, 10), n => n % 2 == 0), n => n * n);
foreach (var s in squares) Console.Write(s + " ");
Console.WriteLine();

IEnumerable<string> names = Select(Range(1, 3), n => "item" + n);
foreach (var name in Take(Concat(names, Repeat("x", 5)), 5)) Console.Write(name + " ");
Console.WriteLine();

var bag = new Bag<double>(new[] { 1.5, 2.5, 3.5 });
double sum = 0;
foreach (var d in bag) sum += d;
Console.WriteLine(sum);
Console.WriteLine(Count(bag));

Console.WriteLine(await Identity(42));
Console.WriteLine(await Identity("generic async"));
var pair = await Both(Identity(1), Identity(2));
Console.WriteLine(pair[0] + pair[1]);

static IEnumerable<int> Range(int start, int count) { for (int i = 0; i < count; i++) yield return start + i; }
static IEnumerable<T> Where<T>(IEnumerable<T> source, Func<T, bool> predicate)
{
    foreach (var item in source) if (predicate(item)) yield return item;
}
static IEnumerable<R> Select<T, R>(IEnumerable<T> source, Func<T, R> selector)
{
    foreach (var item in source) yield return selector(item);
}
static IEnumerable<T> Concat<T>(IEnumerable<T> first, IEnumerable<T> second)
{
    foreach (var a in first) yield return a;
    foreach (var b in second) yield return b;
}
static IEnumerable<T> Repeat<T>(T value, int count) { for (int i = 0; i < count; i++) yield return value; }
static IEnumerable<T> Take<T>(IEnumerable<T> source, int count)
{
    if (count <= 0) yield break;
    foreach (var item in source)
    {
        yield return item;
        if (--count == 0) yield break;
    }
}
static int Count<T>(IEnumerable<T> source) { int n = 0; foreach (var _ in source) n++; return n; }
static async Task<T> Identity<T>(T value) { await Task.Yield(); return value; }
static async Task<T[]> Both<T>(Task<T> a, Task<T> b) => new[] { await a, await b };

class Bag<T>(T[] items) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator()
    {
        foreach (var item in items) yield return item;
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
