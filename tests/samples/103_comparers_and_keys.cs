// Comparer<T> subclasses, Comparison/Comparer.Create, IComparable<T>, custom IEqualityComparer, struct/tuple keys.
var words = new List<string> { "kiwi", "fig", "banana", "apple", "date", "cherry" };
words.Sort(new ByLength());
Console.WriteLine(string.Join(",", words));
words.Sort((a, b) => string.CompareOrdinal(b, a));
Console.WriteLine(string.Join(",", words));
words.Sort(Comparer<string>.Create((a, b) => a[a.Length - 1].CompareTo(b[b.Length - 1])));
Console.WriteLine(string.Join(",", words));
var people = new List<Person> { new("Zed", 30), new("Amy", 25), new("Bob", 30) };
people.Sort();
Console.WriteLine(string.Join(" ", people));
var set = new HashSet<Person>(new NameEq()) { new("amy", 1), new("AMY", 2), new("bob", 3) };
Console.WriteLine(set.Count);
var byAge = new Dictionary<Point, string> { [new Point(1, 2)] = "a", [new Point(3, 4)] = "b" };
Console.WriteLine(byAge[new Point(3, 4)] + byAge.ContainsKey(new Point(1, 2)));
var grid = new Dictionary<(int, int), char> { [(0, 0)] = '#', [(1, 2)] = '.' };
Console.WriteLine(grid[(1, 2)] + " " + grid.ContainsKey((2, 1)));

class ByLength : Comparer<string>
{
    public override int Compare(string? x, string? y) => x!.Length != y!.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
}
class NameEq : IEqualityComparer<Person>
{
    public bool Equals(Person? a, Person? b) => a!.Name.ToUpper() == b!.Name.ToUpper();
    public int GetHashCode(Person p) => p.Name.ToUpper().GetHashCode();
}
class Person(string name, int age) : IComparable<Person>
{
    public string Name => name;
    public int CompareTo(Person? other) => age != other!.Age ? age.CompareTo(other.Age) : string.CompareOrdinal(name, other.Name);
    public int Age => age;
    public override string ToString() => $"{name}({age})";
}
struct Point : IEquatable<Point>
{
    public int X, Y;
    public Point(int x, int y) { X = x; Y = y; }
    public bool Equals(Point o) => X == o.X && Y == o.Y;
    public override int GetHashCode() => X * 31 + Y;
}
