// Generic edge cases: static fields per instantiation, generic base classes, generic interfaces,
// nested generic types, 'is T', CLR-style generic type names.
var c = new Counter<string>();
c.Add("a"); c.Add("b");
Console.WriteLine(Counter<string>.Instances + " " + Counter<int>.Instances);
Container<int> box = new IntContainer(5);
Console.WriteLine(box.Describe());
IRepository<Item> repo = new MemoryRepo<Item>();
repo.Save(new Item("one"));
repo.Save(new Item("two"));
Console.WriteLine(repo.Count + " " + repo.Get(1).Name);
var outer = new Outer<double>.Inner(2.5);
Console.WriteLine(outer.Twice());
Console.WriteLine(IsOfType<string>("s") + " " + IsOfType<Item>("s"));
Console.WriteLine(new Container<string>("z"));
Console.WriteLine(Wrap(Wrap(3)).Value.Value);
static bool IsOfType<T>(object o) => o is T;
static Container<T> Wrap<T>(T v) => new Container<T>(v);

class Counter<T> { public static int Instances; public Counter() { Instances++; } public void Add(T item) { } }
class Container<T>(T value)
{
    public T Value => value;
    public virtual string Describe() => "container of " + value;
}
class IntContainer(int v) : Container<int>(v) { public override string Describe() => "int " + base.Describe(); }
interface IRepository<T> where T : class { void Save(T item); T Get(int index); int Count { get; } }
class MemoryRepo<T> : IRepository<T> where T : class
{
    private T[] _items = new T[4]; private int _n;
    public void Save(T item) => _items[_n++] = item;
    public T Get(int index) => _items[index];
    public int Count => _n;
}
class Item(string name) { public string Name => name; }
class Outer<T> where T : System.Numerics.INumber<T> { public class Inner(T v) { public T Twice() => v + v; } }
