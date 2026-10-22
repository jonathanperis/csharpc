// An in-memory database: typed tables with primary keys and secondary indexes, an LRU cache in front
// of a slow "disk", a token-bucket rate limiter, a query builder, and IComparer/IEquatable plumbing.
var db = new Database();
var people = db.CreateTable<int, Person>("people", p => p.Id);
people.AddIndex("city", p => p.City);
people.AddIndex("decade", p => p.BirthYear / 10 * 10);

string[] first = ["Ada", "Alan", "Grace", "Edsger", "Barbara", "Donald", "Frances", "Ken", "Dennis", "Margaret"];
string[] cities = ["London", "Paris", "Boston", "Delft", "Zurich"];
for (int i = 0; i < 40; i++)
    people.Insert(new Person(i + 1, first[i % first.Length] + " " + (char)('A' + i % 26), cities[(i * 7) % cities.Length], 1900 + (i * 37) % 100, i % 3 == 0));

Console.WriteLine($"rows: {people.Count}, indexes: {string.Join(", ", people.IndexNames)}");
Console.WriteLine($"Boston: {string.Join(", ", people.Lookup("city", "Boston").Select(p => p.Id))}");
Console.WriteLine($"1950s: {people.Lookup("decade", 1950).Count()} people");

var query = people.Query()
    .Where(p => p.Active)
    .Where(p => p.BirthYear >= 1930)
    .OrderBy(new ByCityThenName())
    .Take(6);
foreach (var p in query.Run()) Console.WriteLine($"  {p}");

people.Update(5, p => p with { City = "Tokyo", Active = !p.Active });
Console.WriteLine($"after update: {people.Get(5)}; Tokyo index: {string.Join(",", people.Lookup("city", "Tokyo").Select(p => p.Id))}");
Console.WriteLine($"delete 7: {people.Delete(7)}, delete 7 again: {people.Delete(7)}, count {people.Count}");
try { people.Insert(new Person(1, "Dup", "X", 2000, true)); }
catch (DuplicateKeyException e) { Console.WriteLine($"insert failed: {e.Message}"); }

var byCity = people.All().GroupBy(p => p.City).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
    .Select(g => $"{g.Key}={g.Count()}");
Console.WriteLine("by city: " + string.Join(" ", byCity));

// LRU cache in front of a slow store
var disk = new SlowStore();
var cache = new LruCache<int, string>(capacity: 4);
int[] reads = [1, 2, 3, 1, 4, 5, 2, 1, 6, 3, 1, 1, 7, 2];
foreach (var key in reads)
{
    if (!cache.TryGet(key, out var value)) { value = disk.Read(key); cache.Put(key, value); }
}
Console.WriteLine($"cache: hits {cache.Hits}, misses {cache.Misses}, disk reads {disk.Reads}, contents [{string.Join(",", cache.Keys)}]");

// Token bucket rate limiter on a simulated clock
var limiter = new TokenBucket(capacity: 5, refillPerSecond: 2);
var t = 0.0;
var accepted = new List<double>();
for (int i = 0; i < 30; i++)
{
    t += i % 5 == 0 ? 1.5 : 0.1;
    if (limiter.TryAcquire(t)) accepted.Add(Math.Round(t, 1));
}
Console.WriteLine($"rate limiter accepted {accepted.Count}/30: {string.Join(" ", accepted)}");

// Value equality on keys
var composite = new Dictionary<CompositeKey, string>
{
    [new CompositeKey("eu", 1)] = "first",
    [new CompositeKey("us", 1)] = "second",
};
Console.WriteLine(composite[new CompositeKey("EU", 1)] + " " + composite.ContainsKey(new CompositeKey("us", 2)));
var set = new SortedSet<CompositeKey>(new[] { new CompositeKey("b", 2), new CompositeKey("a", 9), new CompositeKey("B", 1) }, CompositeKey.Comparer);
Console.WriteLine(string.Join(" ", set));

// ------------------------------------------------------------------ model
record Person(int Id, string Name, string City, int BirthYear, bool Active)
{
    public override string ToString() => $"#{Id} {Name} ({City}, {BirthYear}){(Active ? "" : " [inactive]")}";
}

sealed class ByCityThenName : IComparer<Person>
{
    public int Compare(Person? x, Person? y)
    {
        var c = string.CompareOrdinal(x!.City, y!.City);
        return c != 0 ? c : string.CompareOrdinal(x.Name, y.Name);
    }
}

readonly struct CompositeKey(string region, int shard) : IEquatable<CompositeKey>, IComparable<CompositeKey>
{
    public string Region { get; } = region.ToLowerInvariant();
    public int Shard { get; } = shard;
    public bool Equals(CompositeKey other) => Region == other.Region && Shard == other.Shard;
    public override bool Equals(object? obj) => obj is CompositeKey k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(Region, Shard);
    public int CompareTo(CompositeKey other) => (Region, Shard).CompareTo((other.Region, other.Shard));
    public override string ToString() => $"{Region}/{Shard}";
    public static IComparer<CompositeKey> Comparer { get; } = Comparer<CompositeKey>.Create((a, b) => a.CompareTo(b));
}

class DuplicateKeyException(string table, object key) : Exception($"duplicate key {key} in table '{table}'");

// ------------------------------------------------------------------ storage engine
class Database
{
    private readonly Dictionary<string, object> _tables = [];
    public Table<TKey, TRow> CreateTable<TKey, TRow>(string name, Func<TRow, TKey> key) where TKey : notnull
    {
        var table = new Table<TKey, TRow>(name, key);
        _tables.Add(name, table);
        return table;
    }
}

class Table<TKey, TRow>(string name, Func<TRow, TKey> keyOf) where TKey : notnull
{
    private readonly SortedDictionary<TKey, TRow> _rows = new();
    private readonly Dictionary<string, (Func<TRow, object> Key, Dictionary<object, HashSet<TKey>> Map)> _indexes = [];

    public int Count => _rows.Count;
    public IEnumerable<string> IndexNames => _indexes.Keys;

    public void AddIndex<TIndex>(string indexName, Func<TRow, TIndex> key) where TIndex : notnull
    {
        var map = new Dictionary<object, HashSet<TKey>>();
        Func<TRow, object> boxed = r => key(r);
        foreach (var (k, row) in _rows) Add(map, boxed(row), k);
        _indexes[indexName] = (boxed, map);
    }

    private static void Add(Dictionary<object, HashSet<TKey>> map, object value, TKey key)
    {
        if (!map.TryGetValue(value, out var set)) map[value] = set = [];
        set.Add(key);
    }

    public void Insert(TRow row)
    {
        var key = keyOf(row);
        if (!_rows.TryAdd(key, row)) throw new DuplicateKeyException(name, key);
        foreach (var (k, map) in _indexes.Values) Add(map, k(row), key);
    }

    public TRow? Get(TKey key) => _rows.TryGetValue(key, out var row) ? row : default;

    public bool Delete(TKey key)
    {
        if (!_rows.Remove(key, out var row)) return false;
        foreach (var (k, map) in _indexes.Values) map[k(row)].Remove(key);
        return true;
    }

    public void Update(TKey key, Func<TRow, TRow> change)
    {
        var old = _rows[key];
        Delete(key);
        Insert(change(old));
    }

    public IEnumerable<TRow> Lookup(string indexName, object value) =>
        _indexes[indexName].Map.TryGetValue(value, out var keys) ? keys.Order().Select(k => _rows[k]) : [];

    public IEnumerable<TRow> All() => _rows.Values;
    public QueryBuilder<TRow> Query() => new(All());
}

class QueryBuilder<T>(IEnumerable<T> source)
{
    private readonly List<Func<T, bool>> _filters = [];
    private IComparer<T>? _order;
    private int? _limit;

    public QueryBuilder<T> Where(Func<T, bool> predicate) { _filters.Add(predicate); return this; }
    public QueryBuilder<T> OrderBy(IComparer<T> comparer) { _order = comparer; return this; }
    public QueryBuilder<T> Take(int n) { _limit = n; return this; }

    public IEnumerable<T> Run()
    {
        var rows = source.Where(r => _filters.All(f => f(r)));
        if (_order is not null) rows = rows.Order(_order);
        return _limit is int n ? rows.Take(n) : rows;
    }
}

class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public IEnumerable<TKey> Keys => _order.Select(e => e.Key);

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            Hits++;
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        Misses++;
        value = default!;
        return false;
    }

    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing)) _order.Remove(existing);
        else if (_map.Count == capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }
        _map[key] = _order.AddFirst((key, value));
    }
}

class SlowStore
{
    public int Reads { get; private set; }
    public string Read(int key) { Reads++; return $"value-{key * key}"; }
}

class TokenBucket(double capacity, double refillPerSecond)
{
    private double _tokens = capacity;
    private double _last;

    public bool TryAcquire(double now)
    {
        _tokens = Math.Min(capacity, _tokens + (now - _last) * refillPerSecond);
        _last = now;
        if (_tokens < 1) return false;
        _tokens -= 1;
        return true;
    }
}
