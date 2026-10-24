// Enumerable.TryGetNonEnumeratedCount: collections and the LINQ operators whose count .NET knows
// without enumerating (Roslyn's EnumerableExtensions.TryGetBuilder sizes builders with it).

int[] array = [1, 2, 3, 4, 5];
var list = new List<int>(array);
Report("array", array);
Report("list", list);
Report("hashset", new HashSet<int>(array));
Report("range", Enumerable.Range(0, 7));
Report("repeat", Enumerable.Repeat("x", 3));
Report("select over array", array.Select(x => x * 2));
Report("select over list", list.Select(x => x * 2));
Report("select over range", Enumerable.Range(1, 4).Select(x => x + 1));
Report("select over hashset", new HashSet<int>(array).Select(x => x));
Report("indexed select", array.Select((x, i) => x + i));
Report("where", array.Where(x => x > 2));
Report("select over where", array.Where(x => x > 2).Select(x => x));
Report("selectmany", array.SelectMany(x => new[] { x, x }));
Report("distinct", array.Distinct());
Report("append", array.Append(6));
Report("prepend over range", Enumerable.Range(0, 2).Prepend(9));
Report("append over where", array.Where(x => x > 1).Append(0));
Report("concat", array.Concat(list));
Report("concat with where", array.Concat(list.Where(x => x > 3)));
Report("take", list.Take(3));
Report("take more", array.Take(10));
Report("skip", list.Skip(2));
Report("skip all", array.Skip(9));
Report("takewhile", array.TakeWhile(x => x < 3));
Report("zip", array.Zip(list));
Report("oftype", new object[] { 1, "a" }.OfType<int>());
Report("lazy", Lazy());
Report("queue", new Queue<int>(array));
Report("dictionary", new Dictionary<int, int> { [1] = 2 });
try { Enumerable.TryGetNonEnumeratedCount<int>(null!, out _); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

static void Report<T>(string name, IEnumerable<T> source) =>
    Console.WriteLine($"{name}: {(source.TryGetNonEnumeratedCount(out var count) ? count.ToString() : "unknown")}");

static IEnumerable<int> Lazy()
{
    yield return 1;
}
