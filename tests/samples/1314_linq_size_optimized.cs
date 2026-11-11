#:property UseSizeOptimizedLinq=true
// System.Linq with UseSizeOptimizedLinq=true (NativeAOT's default, and csharpc's): the size-optimized iterators. Sample 1313 is the same program
// with the switch false (the speed-optimized iterators): both print what .NET prints.
using System.Collections.ObjectModel;

int[] array = [5, 3, 8, 1, 9, 2, 7, 4, 6, 10];
List<int> list = [.. array];
IList<int> ilist = new Collection<int>(list);
IEnumerable<int> lazy = array.Where(n => n > 0);

foreach (var (name, source) in new (string, IEnumerable<int>)[] { ("array", array), ("list", list), ("ilist", ilist), ("lazy", lazy) })
{
    var selected = source.Select(n => n * 3);
    var filtered = source.Where(n => n % 2 == 1).Select(n => n + 100);
    var window = source.Skip(2).Take(5).Select(n => -n);
    Console.WriteLine($"{name}: [{string.Join(",", selected)}] count={selected.Count()} first={selected.First()} last={selected.Last()} at3={selected.ElementAt(3)}");
    Console.WriteLine($"  where.select [{string.Join(",", filtered.ToArray())}] {filtered.ToList().Count} {filtered.FirstOrDefault()} {filtered.LastOrDefault()}");
    Console.WriteLine($"  skip.take.select [{string.Join(",", window)}] {window.Count()} {window.ElementAtOrDefault(10)} {window.Skip(1).First()} {window.Take(2).Last()}");
    Console.WriteLine($"  chains {source.Select(n => n * 2).Select(n => n + 1).Where(n => n > 10).Sum()} {source.Where(n => n > 3).Where(n => n < 9).Max()}");
    Console.WriteLine($"  order {string.Join(",", source.OrderBy(n => -n).Skip(1).Take(3))} {source.OrderBy(n => n).ElementAt(4)} {source.Order().Last()}");
    Console.WriteLine($"  sets {string.Join(",", source.Concat(array.Take(3)).Distinct().Select(n => n % 4).Distinct())} {source.Append(0).Prepend(11).Count()}");
}
try { Console.WriteLine(array.Select(n => n).ElementAt(20)); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { Console.WriteLine(list.Where(n => n > 100).Select(n => n).First()); }
catch (InvalidOperationException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
