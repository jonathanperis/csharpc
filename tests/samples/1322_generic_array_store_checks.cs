// Array covariance inside generic code: a T[] can be an array of a more derived element type whatever T is (a string[] behind an object[]
// when T is object), so a store, a writable element reference and a copy check the array's exact element type at run time, per
// instantiation. Shared generic code (ADR 0026) must keep every one of these checks: with a canonical form in place of T, an Int32 was
// stored into a string[] (issue #362).
using System;
using System.Collections.Generic;

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

class Slots<T>
{
    private T[] _items = new T[2];
    private int _count;

    public void Add(T item)
    {
        if (_count == _items.Length)
        {
            var grown = new T[_count * 2];
            Array.Copy(_items, grown, _count);
            _items = grown;
        }
        _items[_count++] = item;
    }

    // stelem into an array the caller chose
    public void Store(T[] array, int index, T value) { array[index] = value; }
    // ldelema: a writable reference needs the array to be exactly a T[]
    public ref T Slot(T[] array, int index) => ref array[index];
    public void StoreThroughRef(T[] array, int index, T value) { Slot(array, index) = value; }
    // Array.Copy and CopyTo out of the T[] this instance owns
    public void CopyTo(T[] destination) { Array.Copy(_items, 0, destination, 0, _count); }
    public void CopyToArray(Array destination) { _items.CopyTo(destination, 0); }
    public void CopyFrom(T[] source) { Array.Copy(source, _items, Math.Min(source.Length, _items.Length)); }
    public void SetValue(Array array, T value) { array.SetValue(value, 0); }
    public T[] Fill(int n, T value)
    {
        var array = new T[n];
        for (var i = 0; i < n; i++) array[i] = value;
        return array;
    }
    public string Describe() => _items.GetType() + ":" + string.Join(",", _items);
}

// catch (T): the handler tests the exception against the exact T.
class Catcher<T> where T : Exception
{
    public string Run(Action action)
    {
        try { action(); return "nothing thrown"; }
        catch (T e) { return "caught " + e.GetType().Name; }
    }
}

static class Program
{
    static void Try(string what, Action action)
    {
        try { action(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static void Main()
    {
        object[] strings = new string[2];
        var objects = new Slots<object>();
        Try("Slots<object>.Store string into string[]", () => objects.Store(strings, 0, "s"));
        Try("Slots<object>.Store int into string[]", () => objects.Store(strings, 1, 1));
        Try("Slots<object>.Store object into string[]", () => objects.Store(strings, 1, new object()));
        Try("Slots<object>.Store null into string[]", () => objects.Store(strings, 1, null));
        Try("Slots<object>.Store int into object[]", () => objects.Store(new object[1], 0, 1));
        Try("Slots<object>.Slot of string[]", () => objects.Slot(strings, 0) = "t");
        Try("Slots<object>.Slot of object[]", () => objects.Slot(new object[1], 0) = 1);
        Try("Slots<object>.StoreThroughRef string[]", () => objects.StoreThroughRef(strings, 0, 1));
        Console.WriteLine(strings[0] + " " + (strings[1] == null));

        Customer[] vips = new Vip[2];
        var customers = new Slots<Customer>();
        Try("Slots<Customer>.Store Vip into Vip[]", () => customers.Store(vips, 0, new Vip("v")));
        Try("Slots<Customer>.Store Customer into Vip[]", () => customers.Store(vips, 1, new Customer("c")));
        Try("Slots<Customer>.Slot of Vip[]", () => customers.Slot(vips, 0) = null);
        Try("Slots<Customer>.Slot of Customer[]", () => customers.Slot(new Customer[1], 0) = new Vip("w"));
        Console.WriteLine(vips[0] + " " + (vips[1] == null));

        // A sealed element type: its arrays are never covariant.
        var texts = new Slots<string>();
        Try("Slots<string>.Store", () => texts.Store(new string[1], 0, "s"));
        Try("Slots<string>.Slot", () => texts.Slot(new string[1], 0) = "s");

        // Copies out of the instance's own T[].
        objects.Add("a");
        objects.Add("b");
        Try("Slots<object> { strings }.CopyTo string[]", () => { object[] d = new string[2]; objects.CopyTo(d); Console.WriteLine(d[0] + "" + d[1]); });
        Try("Slots<object> { strings }.CopyToArray string[]", () => { var d = new string[2]; objects.CopyToArray(d); Console.WriteLine(d[0] + d[1]); });
        objects.Add(3);
        Try("Slots<object> { strings, int }.CopyTo string[]", () => { object[] d = new string[4]; objects.CopyTo(d); });
        Try("Slots<object> { strings, int }.CopyToArray string[]", () => objects.CopyToArray(new string[4]));
        Try("Slots<object> { strings, int }.CopyTo object[]", () => { var d = new object[4]; objects.CopyTo(d); Console.WriteLine(d[2]); });
        Try("Slots<object>.CopyFrom string[]", () => objects.CopyFrom(new[] { "x", "y" }));
        Try("Slots<object>.SetValue string[] int", () => objects.SetValue(new string[1], 1));
        Try("Slots<object>.SetValue string[] string", () => objects.SetValue(new string[1], "s"));
        Console.WriteLine(objects.Describe());

        customers.Add(new Vip("a"));
        Try("Slots<Customer> { Vip }.CopyTo Vip[]", () => { Customer[] d = new Vip[1]; customers.CopyTo(d); Console.WriteLine(d[0]); });
        customers.Add(new Customer("b"));
        Try("Slots<Customer> { Vip, Customer }.CopyTo Vip[]", () => { Customer[] d = new Vip[2]; customers.CopyTo(d); });
        Try("Slots<Customer> { Vip, Customer }.CopyToArray object[]", () => { var d = new object[2]; customers.CopyToArray(d); Console.WriteLine(d[1]); });
        Try("Slots<Customer>.CopyFrom Vip[]", () => customers.CopyFrom(new[] { new Vip("z") }));
        Console.WriteLine(customers.Describe());

        Console.WriteLine(objects.Fill(2, "x").GetType() + " " + customers.Fill(1, null).GetType() + " " + texts.Fill(1, "s").GetType());

        var arguments = new Catcher<ArgumentException>();
        Try("Catcher<ArgumentException> ArgumentNullException", () => Console.WriteLine(arguments.Run(() => throw new ArgumentNullException("p"))));
        Try("Catcher<ArgumentException> InvalidOperationException", () => Console.WriteLine(arguments.Run(() => throw new InvalidOperationException("no"))));
        Try("Catcher<InvalidOperationException> ObjectDisposedException", () => Console.WriteLine(new Catcher<InvalidOperationException>().Run(() => throw new ObjectDisposedException("o"))));
        Try("Catcher<InvalidOperationException> ArgumentException", () => Console.WriteLine(new Catcher<InvalidOperationException>().Run(() => throw new ArgumentException("a"))));
        Try("Catcher<Exception> nothing", () => Console.WriteLine(new Catcher<Exception>().Run(() => { })));

        // The framework's own collections over a covariant destination.
        Try("List<object> { int }.CopyTo string[]", () => { var l = new List<object> { 1 }; object[] d = new string[1]; l.CopyTo(d); });
        Try("List<object> { string }.CopyTo string[]", () => { var l = new List<object> { "s" }; object[] d = new string[1]; l.CopyTo(d); Console.WriteLine(d[0]); });
        Try("List<Customer> { Customer }.CopyTo Vip[]", () => { var l = new List<Customer> { new Customer("c") }; Customer[] d = new Vip[1]; l.CopyTo(d); });
        Try("Stack<object> { int }.CopyTo string[]", () => { var s = new Stack<object>(); s.Push(1); object[] d = new string[1]; s.CopyTo(d, 0); });
        Try("ICollection List<object> { int }.CopyTo string[]", () => ((System.Collections.ICollection)new List<object> { 1 }).CopyTo(new string[1], 0));
    }
}
