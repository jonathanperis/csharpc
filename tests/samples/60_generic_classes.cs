// Generic classes, compiled by monomorphization: Stack<int> and Stack<string> are separate code.
var ints = new Stack<int>();
for (int i = 1; i <= 5; i++) ints.Push(i * i);
Console.WriteLine($"count={ints.Count} peek={ints.Peek()}");
while (!ints.IsEmpty) Console.Write(ints.Pop() + " ");
Console.WriteLine();

var words = new Stack<string>();
words.Push("generic");
words.Push("are");
words.Push("templates");
Console.WriteLine(words.Pop() + " " + words.Pop() + " " + words.Pop());

var list = new LinkedList<double>();
list.Add(1.5); list.Add(2.5); list.Add(4.0);
Console.WriteLine($"sum={list.Sum()} length={list.Length}");

var pair = new Pair<string, int>("answer", 42);
Console.WriteLine($"{pair.Key} -> {pair.Value}");
var swapped = pair.Swap();
Console.WriteLine($"{swapped.Key} -> {swapped.Value}");

var boxes = new Stack<Box<int>>();
boxes.Push(new Box<int>(7));
boxes.Push(new Box<int>(8));
Console.WriteLine(boxes.Pop().Value + boxes.Pop().Value);
Console.WriteLine(new Box<string>("hi"));
Console.WriteLine(new Box<int>(3));

var q = new Queue<char>(2);
foreach (var c in "queue!") q.Enqueue(c);
string drained = "";
while (q.Count > 0) drained += q.Dequeue();
Console.WriteLine(drained);

class Stack<T>
{
    private T[] _items = new T[4];
    private int _count;
    public int Count => _count;
    public bool IsEmpty => _count == 0;
    public void Push(T item)
    {
        if (_count == _items.Length)
        {
            var bigger = new T[_items.Length * 2];
            for (int i = 0; i < _count; i++) bigger[i] = _items[i];
            _items = bigger;
        }
        _items[_count++] = item;
    }
    public T Pop() => _items[--_count];
    public T Peek() => _items[_count - 1];
}

class Node<T>(T value)
{
    public T Value = value;
    public Node<T>? Next;
}

class LinkedList<T>
{
    private Node<T>? _head, _tail;
    public int Length { get; private set; }
    public void Add(T value)
    {
        var node = new Node<T>(value);
        if (_tail is null) _head = _tail = node;
        else { _tail.Next = node; _tail = node; }
        Length++;
    }
    public Node<T>? First => _head;
}

static class ListExtensions
{
    public static double Sum(this LinkedList<double> list)
    {
        double total = 0;
        for (var n = list.First; n != null; n = n.Next) total += n.Value;
        return total;
    }
}

class Pair<TKey, TValue>(TKey key, TValue value)
{
    public TKey Key { get; } = key;
    public TValue Value { get; } = value;
    public Pair<TValue, TKey> Swap() => new Pair<TValue, TKey>(Value, Key);
}

class Box<T>(T value)
{
    public T Value => value;
    public override string ToString() => "Box(" + value + ")";
}

class Queue<T>
{
    private T[] _buffer;
    private int _head, _count;
    public Queue(int capacity) => _buffer = new T[capacity];
    public int Count => _count;
    public void Enqueue(T item)
    {
        if (_count == _buffer.Length)
        {
            var bigger = new T[_buffer.Length * 2];
            for (int i = 0; i < _count; i++) bigger[i] = _buffer[(_head + i) % _buffer.Length];
            _buffer = bigger;
            _head = 0;
        }
        _buffer[(_head + _count++) % _buffer.Length] = item;
    }
    public T Dequeue()
    {
        var item = _buffer[_head];
        _head = (_head + 1) % _buffer.Length;
        _count--;
        return item;
    }
}
