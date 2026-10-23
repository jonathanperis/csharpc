// Layout computation with self-references that must not recurse: a class deriving from a generic
// base over itself (CRTP), static fields of the containing class/struct type, structs holding
// arrays/lists/pairs of themselves, generic types referring to deeper instantiations of themselves
// through reference-typed fields, and a record referring to itself.
#nullable disable
#pragma warning disable CS0649 // fields only read through default values
using System;
using System.Collections.Generic;

abstract class Shape<TSelf> where TSelf : Shape<TSelf>
{
    public TSelf Next;
    public List<TSelf> Children = new();
    public abstract string Name { get; }
}
sealed class Circle : Shape<Circle>, IComparable<Circle>
{
    public static readonly Circle Unit = new Circle { R = 1 };
    public static Circle Last;
    public double R;
    public override string Name => "circle " + R;
    public int CompareTo(Circle other) => R.CompareTo(other.R);
}

struct Vec : IEquatable<Vec>
{
    public static readonly Vec Zero = new Vec(0, 0);
    public static Vec[] Cache = { new Vec(1, 1) };
    public int X, Y;
    public Vec(int x, int y) { X = x; Y = y; }
    public bool Equals(Vec o) => X == o.X && Y == o.Y;
    public override string ToString() => $"({X}, {Y})";
}

struct Tree
{
    public int Value;
    public Tree[] Kids;
    public List<Tree> More;
    public KeyValuePair<int, Tree[]> Pair;
}

struct Box<T> { public T Item; public Box<T>[] Siblings; }

class Node<T>
{
    public T Value;
    public Node<T> Next;
    public Node<Node<T>> Wrapped;
    public static Node<T> Empty;
}

record Person(string Name, Person Parent);

class Program
{
    static void Main()
    {
        var c = new Circle { R = 2, Next = Circle.Unit };
        c.Children.Add(new Circle { R = 3 });
        Circle.Last = c;
        Console.WriteLine(c.Name + " " + c.Next.Name + " " + c.Children[0].Name + " " + c.CompareTo(Circle.Unit));
        Console.WriteLine(Vec.Zero + " " + Vec.Cache[0] + " " + Vec.Zero.Equals(new Vec()));
        var t = new Tree { Value = 1, Kids = new[] { new Tree { Value = 2 } }, More = new List<Tree> { new Tree { Value = 3 } } };
        t.Pair = new KeyValuePair<int, Tree[]>(4, t.Kids);
        Console.WriteLine(t.Value + t.Kids[0].Value + t.More[0].Value + t.Pair.Key);
        var b = new Box<int> { Item = 5, Siblings = new[] { new Box<int> { Item = 6 } } };
        Console.WriteLine(b.Item + b.Siblings[0].Item);
        var n = new Node<int> { Value = 7, Next = Node<int>.Empty ?? new Node<int>(), Wrapped = new Node<Node<int>>() };
        n.Wrapped.Value = n;
        Console.WriteLine(n.Value + " " + n.Wrapped.Value.Value + " " + (n.Next.Next == null));
        var p = new Person("Ada", new Person("Byron", null));
        Console.WriteLine(p);
        Console.WriteLine(p == new Person("Ada", new Person("Byron", null)));
    }
}
