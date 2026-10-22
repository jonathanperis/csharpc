// foreach over user collections: the GetEnumerator pattern, IEnumerable<T>/IEnumerator<T>
// implemented by user types (interface dispatch incl. IDisposable), and arrays side by side.
var range = new Range(1, 5);
foreach (var i in range) Console.Write(i + " ");
Console.WriteLine();

var bag = new Bag<string>();
bag.Add("x"); bag.Add("y"); bag.Add("z");
foreach (var s in bag) Console.Write(s);
Console.WriteLine();

IEnumerable<string> seq = bag;
foreach (var s in seq) Console.Write("[" + s + "]");
Console.WriteLine();
Console.WriteLine(Bag<string>.Disposed);

Console.WriteLine(Total(new Range(1, 100)));
Console.WriteLine(CountAll(bag));

static int Total(Range r)
{
    int t = 0;
    foreach (var x in r) t += x;
    return t;
}

static int CountAll<T>(IEnumerable<T> items)
{
    int n = 0;
    foreach (var _ in items) n++;
    return n;
}

// Pattern-based: no interfaces at all.
class Range(int from, int to)
{
    public RangeEnumerator GetEnumerator() => new RangeEnumerator(from, to);
}

class RangeEnumerator(int from, int to)
{
    private int _current = from - 1;
    public int Current => _current;
    public bool MoveNext() => ++_current <= to;
}

class Bag<T> : IEnumerable<T>
{
    public static int Disposed;
    private T[] _items = new T[8];
    private int _count;
    public void Add(T item) => _items[_count++] = item;

    public IEnumerator<T> GetEnumerator() => new Enumerator(this);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator(Bag<T> bag) : IEnumerator<T>
    {
        private int _index = -1;
        public T Current => bag._items[_index];
        object? System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < bag._count;
        public void Reset() => _index = -1;
        public void Dispose() => Disposed++;
    }
}
