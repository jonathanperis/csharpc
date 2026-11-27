// What generic code asks its own type arguments for at run time, per instantiation: casts and type tests against T and against types built
// from T, typeof, new T[], new of another generic class over T, and array covariance on a T[]. Shared generic code (ADR 0026) reads each of
// these from the generic dictionary of the object it runs on, so every instantiation must still see its own exact types: a class with two
// type parameters is used with its arguments swapped (Pair<string, Customer> against Pair<Customer, string>), where List<TKey> and
// List<TValue> are different types that a single canonical form has to keep apart.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

class Customer
{
    public string Name;
    public Customer(string name) { Name = name; }
    public override string ToString() => "Customer(" + Name + ")";
}

sealed class Vip : Customer
{
    public Vip(string name) : base(name) { }
    public override string ToString() => "Vip(" + Name + ")";
}

interface IShape { string Describe(); }
sealed class Circle : IShape { public string Describe() => "circle"; }
sealed class Square : IShape { public string Describe() => "square"; }

class Node<T>
{
    public T Value;
    public Node<T> Next;
    public Node(T value, Node<T> next) { Value = value; Next = next; }
    public override string ToString() => "Node<" + typeof(T).Name + ">(" + Value + ")";
}

class Pair<TKey, TValue>
{
    private readonly List<TKey> _keys = new List<TKey>();
    private readonly List<TValue> _values = new List<TValue>();
    private TKey[] _keyArray = new TKey[2];
    private Node<TValue> _chain;

    public void Add(TKey key, TValue value)
    {
        _keys.Add(key);
        _values.Add(value);
        if (_keys.Count > _keyArray.Length)
        {
            var grown = new TKey[_keyArray.Length * 2];
            Array.Copy(_keyArray, grown, _keyArray.Length);
            _keyArray = grown;
        }
        _keyArray[_keys.Count - 1] = key;
        _chain = new Node<TValue>(value, _chain);
    }

    // Casts and type tests against the type parameters and against types built from them.
    public bool IsKey(object o) => o is TKey;
    public bool IsValue(object o) => o is TValue;
    public TKey AsKey(object o) => (TKey)o;
    public TValue AsValue(object o) => (TValue)o;
    public bool IsKeyList(object o) => o is List<TKey>;
    public bool IsValueList(object o) => o is List<TValue>;
    public bool IsKeyArray(object o) => o is TKey[];
    public bool IsValueNode(object o) => o is Node<TValue>;
    public List<TKey> AsKeyList(object o) => (List<TKey>)o;

    public object Keys => _keys;
    public object Values => _values;
    public object KeyArray => _keyArray;
    public object Chain => _chain;

    public object NewKeyArray(int n) => new TKey[n];
    public object NewValueArray(int n) => new TValue[n];
    public object NewKeyList() => new List<TKey>();
    public object NewValueList() => new List<TValue>();
    public object NewNestedArray() => new List<TValue>[1];
    public object NewSwapped() => new Pair<TValue, TKey>();

    public string Describe()
    {
        var chain = "";
        for (var n = _chain; n != null; n = n.Next) chain += n + ";";
        return string.Join(",", _keys) + " | " + string.Join(",", _values) + " | " + _keyArray.GetType() + " | " + chain;
    }
}

// A T[] can be a covariant array whatever T is.
class Slots<T>
{
    public void Store(T[] array, int index, T value) { array[index] = value; }
    public ref T Slot(T[] array, int index) => ref array[index];
    public T[] Fill(int n, T value)
    {
        var array = new T[n];
        for (var i = 0; i < n; i++) array[i] = value;
        return array;
    }
}

class Base<T>
{
    public virtual object Make() => new List<T>();
    public bool Accepts(object o) => o is T;
}

class Derived<T, U> : Base<U>
{
    public object MakeOwn() => new List<T>();
    public override object Make() => new Node<U>(default, null);
}

sealed class Strings : Base<string> { }

class Outer<T>
{
    public class Inner<U>
    {
        public object Both() => new Pair<T, U>();
        public bool IsOuter(object o) => o is T;
        public bool IsInner(object o) => o is U;
    }
}

// [DynamicallyAccessedMembers] on a type parameter: each exact instantiation keeps its argument's members for reflection.
class Described<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
{
    public string Properties() => string.Join(",", typeof(T).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    public bool Is(object o) => o is T;
}

class Point { public int X { get; set; } public int Y { get; set; } }
class Person { public string Name { get; set; } = ""; }

static class Program
{
    static void Try(string what, Action action)
    {
        try { action(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name); }
    }

    static void Show<TKey, TValue>(string name, Pair<TKey, TValue> pair, object key, object value)
    {
        Console.WriteLine("== " + name);
        Console.WriteLine(pair.Describe());
        Console.WriteLine("is key/value: " + pair.IsKey(key) + " " + pair.IsKey(value) + " " + pair.IsValue(key) + " " + pair.IsValue(value) + " " + pair.IsKey(null));
        Console.WriteLine("lists: " + pair.IsKeyList(pair.Keys) + " " + pair.IsKeyList(pair.Values) + " " + pair.IsValueList(pair.Keys) + " " + pair.IsValueList(pair.Values));
        Console.WriteLine("arrays: " + pair.IsKeyArray(pair.KeyArray) + " " + pair.IsKeyArray(new object[0]) + " " + pair.IsKeyArray(new string[0]) + " " + pair.IsKeyArray(new Customer[0]) + " " + pair.IsKeyArray(new Vip[0]));
        Console.WriteLine("nodes: " + pair.IsValueNode(pair.Chain) + " " + pair.IsValueNode(new Node<string>("x", null)) + " " + pair.IsValueNode(new Node<Customer>(null, null)) + " " + pair.IsValueNode(new Node<object>(null, null)));
        Console.WriteLine("new: " + pair.NewKeyArray(1).GetType() + " " + pair.NewValueArray(1).GetType() + " " + pair.NewKeyList().GetType() + " " + pair.NewValueList().GetType());
        Console.WriteLine("new: " + pair.NewNestedArray().GetType() + " " + pair.NewSwapped().GetType());
        Try("cast key", () => Console.WriteLine(pair.AsKey(key)));
        Try("cast value as key", () => Console.WriteLine(pair.AsKey(value)));
        Try("cast value", () => Console.WriteLine(pair.AsValue(value)));
        Try("cast key as value", () => Console.WriteLine(pair.AsValue(key)));
        Try("cast null", () => Console.WriteLine(pair.AsKey(null) == null));
        Try("cast keys", () => Console.WriteLine(pair.AsKeyList(pair.Keys).Count));
        Try("cast values as keys", () => Console.WriteLine(pair.AsKeyList(pair.Values).Count));
    }

    static void Main()
    {
        var byName = new Pair<string, Customer>();
        byName.Add("ada", new Customer("Ada"));
        byName.Add("bob", new Vip("Bob"));
        byName.Add("cy", new Customer("Cy"));
        Show("Pair<string, Customer>", byName, "ada", new Customer("Zed"));

        var byCustomer = new Pair<Customer, string>();
        byCustomer.Add(new Customer("Ada"), "ada");
        byCustomer.Add(new Vip("Bob"), "bob");
        Show("Pair<Customer, string>", byCustomer, new Vip("Zed"), "zed");

        var same = new Pair<string, string>();
        same.Add("k", "v");
        Show("Pair<string, string>", same, "k", "v");

        var shapes = new Pair<IShape, object>();
        shapes.Add(new Circle(), 1);
        shapes.Add(new Square(), "two");
        Show("Pair<IShape, object>", shapes, new Square(), 3);

        var arrays = new Pair<int[], string[]>();
        arrays.Add(new[] { 1 }, new[] { "a" });
        Show("Pair<int[], string[]>", arrays, new int[0], new string[0]);

        // A value-type argument keeps the instantiation apart from the reference ones.
        var counted = new Pair<int, string>();
        counted.Add(1, "one");
        Show("Pair<int, string>", counted, 1, "one");
        var named = new Pair<string, int>();
        named.Add("one", 1);
        Show("Pair<string, int>", named, "one", 1);

        Console.WriteLine("== array covariance");
        object[] strings = new string[2];
        var objects = new Slots<object>();
        Try("Slots<object>.Store string into string[]", () => objects.Store(strings, 0, "s"));
        Try("Slots<object>.Store int into string[]", () => objects.Store(strings, 1, 1));
        Try("Slots<object>.Store null into string[]", () => objects.Store(strings, 1, null));
        Try("Slots<object>.Store object into object[]", () => objects.Store(new object[1], 0, new object()));
        Try("Slots<object>.Slot of string[]", () => objects.Slot(strings, 0) = "t");
        Try("Slots<object>.Slot of object[]", () => objects.Slot(new object[1], 0) = 1);
        Customer[] vips = new Vip[1];
        var customers = new Slots<Customer>();
        Try("Slots<Customer>.Store Vip into Vip[]", () => customers.Store(vips, 0, new Vip("v")));
        Try("Slots<Customer>.Store Customer into Vip[]", () => customers.Store(vips, 0, new Customer("c")));
        Try("Slots<Customer>.Slot of Vip[]", () => customers.Slot(vips, 0) = null);
        Try("Slots<string>.Store", () => new Slots<string>().Store(new string[1], 0, "s"));
        Console.WriteLine(strings[0] + " " + (strings[1] == null) + " " + vips[0]);
        Console.WriteLine(objects.Fill(2, "x").GetType() + " " + customers.Fill(1, null).GetType() + " " + new Slots<string>().Fill(1, "s").GetType() + " " + new Slots<IShape>().Fill(3, null).Length);

        Console.WriteLine("== inheritance");
        var derived = new Derived<Customer, string>();
        Console.WriteLine(derived.Make() + " " + derived.MakeOwn().GetType() + " " + derived.Accepts("s") + " " + derived.Accepts(new Customer("c")));
        var flipped = new Derived<string, Customer>();
        Console.WriteLine(flipped.Make() + " " + flipped.MakeOwn().GetType() + " " + flipped.Accepts("s") + " " + flipped.Accepts(new Vip("v")));
        Base<string> asBase = new Derived<object, string>();
        Console.WriteLine(asBase.Make() + " " + asBase.Accepts("s") + " " + asBase.Accepts(1));
        var strs = new Strings();
        Console.WriteLine(strs.Make().GetType() + " " + strs.Accepts("s") + " " + strs.Accepts(new object()));

        Console.WriteLine("== nested");
        var inner = new Outer<string>.Inner<Customer>();
        Console.WriteLine(inner.Both().GetType() + " " + inner.IsOuter("s") + " " + inner.IsOuter(new Customer("c")) + " " + inner.IsInner("s") + " " + inner.IsInner(new Vip("v")));
        var swappedInner = new Outer<Customer>.Inner<string>();
        Console.WriteLine(swappedInner.Both().GetType() + " " + swappedInner.IsOuter("s") + " " + swappedInner.IsOuter(new Customer("c")) + " " + swappedInner.IsInner("s") + " " + swappedInner.IsInner(new Vip("v")));

        Console.WriteLine("== reflection over T");
        Console.WriteLine(new Described<Point>().Properties() + " | " + new Described<Person>().Properties() + " | " + new Described<Point>().Is(new Person()) + " " + new Described<Person>().Is(new Person()));

        Console.WriteLine("== framework collections");
        // SortedDictionary's ICollection.CopyTo calls its inner TreeSet<KeyValuePair<TKey, TValue>>, a class with no canonical form.
        var sorted = new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 };
        var pairs = new KeyValuePair<string, int>[2];
        ((System.Collections.ICollection)sorted).CopyTo(pairs, 0);
        Console.WriteLine(string.Join(",", pairs) + " " + sorted.Count + " " + sorted.First().Key);
        // An iterator over a struct enumerator (`constrained.` calls into HashBucket.Enumerator), and the collection expression's
        // read-only array, whose ICollection.CopyTo goes through the array's own interface.
        var immutable = ImmutableDictionary.Create<string, string>().Add("a", "x").Add("b", "y");
        Console.WriteLine(string.Join(",", immutable.Values.OrderBy(v => v, StringComparer.Ordinal)) + " " + string.Join(",", immutable.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        IReadOnlyList<string> readOnly = ["p", "q"];
        var copied = new string[3];
        ((System.Collections.ICollection)readOnly).CopyTo(copied, 1);
        Console.WriteLine(string.Join(",", copied) + " " + readOnly.Count);
        System.Collections.IList untyped = new List<string>();
        Try("IList.Add string to List<string>", () => untyped.Add("s"));
        Try("IList.Add int to List<string>", () => untyped.Add(1));
        Try("IList.Add null to List<string>", () => untyped.Add(null));
        Console.WriteLine(untyped.Contains("s") + " " + untyped.Contains(1) + " " + untyped.IndexOf(null) + " " + untyped.Count);
        System.Collections.IList customersList = new List<Customer>();
        Try("IList.Add Vip to List<Customer>", () => customersList.Add(new Vip("v")));
        Try("IList.Add string to List<Customer>", () => customersList.Add("s"));
        Try("IList[0] = Customer", () => customersList[0] = new Customer("c"));
        Try("IList[0] = object", () => customersList[0] = new object());
        Console.WriteLine(customersList[0] + " " + new List<Customer>(4).ToArray().GetType() + " " + new List<string> { "a" }.ToArray().GetType() + " " + new List<IShape> { new Circle() }.GetRange(0, 1).GetType());
        var queue = new Queue<Customer>();
        queue.Enqueue(new Vip("q"));
        var stack = new Stack<string>();
        stack.Push("s");
        Console.WriteLine(queue.ToArray().GetType() + " " + stack.ToArray().GetType() + " " + queue.Peek() + " " + stack.Peek());
    }
}
