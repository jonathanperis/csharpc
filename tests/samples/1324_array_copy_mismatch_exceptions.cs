// Array copies between arrays of different element types (issue #363): Array.Copy's rules decide the
// exception. Incompatible element types throw ArrayTypeMismatchException("Source array type cannot be
// assigned to destination array type."); compatible ones whose element downcast fails throw
// InvalidCastException at the first misfit, after copying the elements before it. Queue<T>.CopyTo
// copies through Array.Copy, so an object queue copied into a string[] seen as object[] casts too.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;

class Customer { public override string ToString() => "Customer"; }
sealed class Vip : Customer { public override string ToString() => "Vip"; }

static class Program
{
    static void Try(string what, Action action)
    {
        try { action(); Console.WriteLine(what + ": ok"); }
        catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + ": " + e.Message); }
    }

    static string Show(Array a)
    {
        var parts = new List<string>();
        foreach (var o in a) parts.Add(o == null ? "null" : o.ToString());
        return "[" + string.Join(", ", parts) + "]";
    }

    static Queue<object> Wrapped()
    {
        // _head != 0 and the items wrap around the end of the queue's buffer: two Array.Copy calls.
        var q = new Queue<object>(4);
        q.Enqueue("x"); q.Enqueue("y"); q.Enqueue("z");
        q.Dequeue(); q.Dequeue();
        q.Enqueue("a"); q.Enqueue(7); q.Enqueue("b");
        return q;
    }

    static void Main()
    {
        // Queue<object>.CopyTo(object[]) whose runtime type is string[].
        Try("Queue int", () => { var q = new Queue<object>(); q.Enqueue(1); object[] d = new string[1]; q.CopyTo(d, 0); });
        Try("Queue strings", () =>
        {
            var q = new Queue<object>(); q.Enqueue("p"); q.Enqueue("q");
            object[] d = new string[3]; q.CopyTo(d, 1); Console.WriteLine("  " + Show(d));
        });
        object[] partial = new string[5];
        Try("Queue wrapped", () => Wrapped().CopyTo(partial, 1));
        Console.WriteLine("  " + Show(partial));
        Try("Queue null", () => new Queue<object>().CopyTo(null, 0));
        Try("Queue index", () => new Queue<object>().CopyTo(new object[1], 2));
        Try("Queue negative", () => new Queue<object>().CopyTo(new object[1], -1));
        Try("Queue short", () => { var q = new Queue<object>(); q.Enqueue(1); q.Enqueue(2); q.CopyTo(new object[2], 1); });
        Try("Queue<int>", () => { var q = new Queue<int>(); q.Enqueue(4); q.Enqueue(5); var d = new int[2]; q.CopyTo(d, 0); Console.WriteLine("  " + Show(d)); });

        // Array.CopyTo(Array, int) and Array.Copy with reference element types.
        Try("CopyTo unrelated", () => { Customer[] a = { new Vip() }; a.CopyTo((Array)new string[1], 0); });
        Try("Copy unrelated", () => { Customer[] a = { new Vip() }; Array.Copy(a, (Array)new string[1], 1); });
        object[] mixed = { "s", 1, "t" };
        var strings = new string[3];
        Try("CopyTo downcast", () => mixed.CopyTo((Array)strings, 0));
        Console.WriteLine("  " + Show(strings));
        Try("Copy downcast", () => Array.Copy(mixed, (Array)new string[3], 3));
        Try("CopyTo downcast ok", () => { object[] a = { "u", null, "v" }; var d = new string[3]; a.CopyTo((Array)d, 0); Console.WriteLine("  " + Show(d)); });
        Try("CopyTo base to sealed", () => { Customer[] a = { new Vip(), new Customer() }; var d = new Vip[2]; a.CopyTo((Array)d, 0); });
        Try("CopyTo base to sealed ok", () => { Customer[] a = { new Vip() }; var d = new Vip[1]; a.CopyTo((Array)d, 0); Console.WriteLine("  " + Show(d)); });
        Try("CopyTo upcast", () => { string[] a = { "w" }; var d = new object[1]; a.CopyTo((Array)d, 0); Console.WriteLine("  " + Show(d)); });
        Try("CopyTo interface", () => { object[] a = { "c", 2 }; var d = new IComparable[2]; a.CopyTo((Array)d, 0); Console.WriteLine("  " + Show(d)); });
        Try("CopyTo interface miss", () => { object[] a = { new Customer() }; var d = new IComparable[1]; a.CopyTo((Array)d, 0); });
        Try("CopyTo covariant source", () => { object[] a = new string[] { "m" }; var d = new string[1]; a.CopyTo((Array)d, 0); Console.WriteLine("  " + Show(d)); });
    }
}
