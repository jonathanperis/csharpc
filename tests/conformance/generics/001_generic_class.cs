var s = new Stack<int>();
s.Push(1); s.Push(2); s.Push(3);
Console.WriteLine(s.Pop() + " " + s.Count);
var b = new Box<string>("hi");
Console.WriteLine(b.Value);
class Box<T>(T value) { public T Value = value; }
class Stack<T>
{
    T[] _items = new T[4]; int _n;
    public int Count => _n;
    public void Push(T x) { if (_n == _items.Length) Array.Resize(ref _items, _n * 2); _items[_n++] = x; }
    public T Pop() => _items[--_n];
}
