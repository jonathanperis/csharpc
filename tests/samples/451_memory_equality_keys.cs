// Memory<T> / ReadOnlyMemory<T> as values: Equals compares owner, start and length (not contents),
// default memory hashes to 0, and memories work as keys of the default and custom comparers
// (Roslyn keys tables by ReadOnlyMemory<char> with its own comparer).
var text = "hello world hello";
var a = text.AsMemory(0, 5);
var b = text.AsMemory(0, 5);
var c = text.AsMemory(12, 5);
Console.WriteLine($"{a.Equals(b)} {a.Equals(c)} {a.Span.SequenceEqual(c.Span)}");
Console.WriteLine($"{default(ReadOnlyMemory<char>).GetHashCode()} {default(Memory<int>).GetHashCode()}");
Console.WriteLine(a.GetHashCode() == b.GetHashCode());
Console.WriteLine(EqualityComparer<ReadOnlyMemory<char>>.Default.Equals(a, b));
Console.WriteLine(EqualityComparer<ReadOnlyMemory<char>>.Default.Equals(a, c));
Console.WriteLine(Same(a, b) + " " + Same(a, c) + " " + (Hash(a) == Hash(b)));

var array = new[] { 1, 2, 3, 4 };
var m1 = new Memory<int>(array, 1, 2);
var m2 = array.AsMemory(1, 2);
var m3 = array.AsMemory(1, 3);
Console.WriteLine($"{m1.Equals(m2)} {m1.Equals(m3)} {m1.Equals(m1.Slice(0, 2))}");
var empty1 = new Memory<int>(array, 0, 0);
var empty2 = new Memory<int>(array, 1, 0);
Console.WriteLine($"{empty1.Equals(empty2)} {empty1.Equals(default)}");

var byIdentity = new Dictionary<ReadOnlyMemory<char>, int>();
byIdentity[a] = 1;
byIdentity[b] = 2;
byIdentity[c] = 3;
Console.WriteLine(byIdentity.Count);

var byContent = new Dictionary<ReadOnlyMemory<char>, int>(new ContentComparer());
foreach (var word in text.Split(' '))
{
    var key = word.AsMemory();
    byContent[key] = byContent.TryGetValue(key, out var n) ? n + 1 : 1;
}
foreach (var (key, count) in byContent) Console.WriteLine($"{key}: {count}");

var set = new HashSet<Memory<int>> { m1, m2, m3 };
Console.WriteLine(set.Count);
Console.WriteLine(Array.IndexOf(new[] { c, a }, b));

static bool Same<T>(T x, T y) => EqualityComparer<T>.Default.Equals(x, y);
static int Hash<T>(T x) => EqualityComparer<T>.Default.GetHashCode(x!);

sealed class ContentComparer : IEqualityComparer<ReadOnlyMemory<char>>
{
    public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y) => x.Span.SequenceEqual(y.Span);
    public int GetHashCode(ReadOnlyMemory<char> obj)
    {
        var hash = 17;
        foreach (var ch in obj.Span) hash = hash * 31 + ch;
        return hash;
    }
}
