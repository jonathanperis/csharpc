// Iterators declared in C# 14 extension blocks (generic ones too): GetEnumerator() on an enumerable
// that was already enumerated re-invokes the method, receiver included.
var a = new[] { 1, 2, 3 };
Console.WriteLine(string.Join(",", a.Twice()));
Console.WriteLine(string.Join(",", a.Zip2(new[] { 9, 8 })));
Console.WriteLine(string.Join(",", Ext.Zip2(a, new[] { 7 })));
Console.WriteLine(a.Count2);
var twice = a.Twice();
Console.WriteLine(twice.Count() + " " + twice.Count());
static class Ext
{
    extension<T>(IEnumerable<T> seq)
    {
        public IEnumerable<T> Twice() { foreach (var x in seq) { yield return x; yield return x; } }
        public IEnumerable<T> Zip2(IEnumerable<T> other) { foreach (var x in seq) yield return x; foreach (var y in other) yield return y; }
        public int Count2 => seq.Count();
    }
}
