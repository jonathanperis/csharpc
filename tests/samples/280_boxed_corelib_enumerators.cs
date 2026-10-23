using System;
using System.Collections;
using System.Collections.Generic;

// Collection whose non-generic GetEnumerator hands out a boxed List<T>.Enumerator.
class Bag : IEnumerable
{
    private readonly List<int> items = new() { 1, 2, 3 };
    public IEnumerator GetEnumerator() => items.GetEnumerator();
}

class Program
{
    static void Drain(string label, IEnumerator e)
    {
        Console.Write(label + ":");
        try { _ = e.Current; Console.Write(" current-before"); }
        catch (InvalidOperationException ex) { Console.Write(" [" + ex.Message + "]"); }
        while (e.MoveNext()) Console.Write(" " + e.Current);
        try { _ = e.Current; Console.Write(" current-after"); }
        catch (InvalidOperationException ex) { Console.Write(" [" + ex.Message + "]"); }
        e.Reset();
        Console.Write(" reset:");
        while (e.MoveNext()) Console.Write(" " + e.Current);
        Console.WriteLine();
        if (e is IDisposable d) d.Dispose();
    }

    static int SumGeneric<T>(IEnumerator<T> e, Func<T, int> f)
    {
        int sum = 0;
        using (e) while (e.MoveNext()) sum += f(e.Current);
        return sum;
    }

    static void Main()
    {
        foreach (var x in new Bag()) Console.Write(x + " ");
        Console.WriteLine();

        var list = new List<int> { 10, 20, 30 };
        var set = new HashSet<string> { "a", "b" };
        var dict = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 };
        var queue = new Queue<int>(new[] { 4, 5 });
        var stack = new Stack<int>(new[] { 6, 7 });
        var linked = new LinkedList<char>(new[] { 'p', 'q' });
        var sdict = new SortedDictionary<int, string> { [2] = "two", [1] = "one" };
        var sset = new SortedSet<int> { 9, 8 };

        Drain("List", list.GetEnumerator());
        Drain("HashSet", set.GetEnumerator());
        Drain("Dictionary", dict.GetEnumerator());
        Drain("Keys", dict.Keys.GetEnumerator());
        Drain("Values", dict.Values.GetEnumerator());
        Drain("Queue", queue.GetEnumerator());
        Drain("Stack", stack.GetEnumerator());
        Drain("LinkedList", linked.GetEnumerator());
        Drain("SortedDictionary", sdict.GetEnumerator());
        Drain("SortedKeys", sdict.Keys.GetEnumerator());
        Drain("SortedValues", sdict.Values.GetEnumerator());
        Drain("SortedSet", sset.GetEnumerator());

        Console.WriteLine(SumGeneric(list.GetEnumerator(), v => v));
        Console.WriteLine(SumGeneric(dict.GetEnumerator(), kv => kv.Value));
        Console.WriteLine(SumGeneric(sset.GetEnumerator(), v => v));

        object boxed = list.GetEnumerator();
        Console.WriteLine(boxed is IEnumerator);
        Console.WriteLine(boxed is IEnumerator<int>);
        Console.WriteLine(boxed is IEnumerator<long>);
        Console.WriteLine(boxed is IDisposable);

        // Modification after the enumerator was created: Reset and MoveNext both check the version.
        IEnumerator stale = list.GetEnumerator();
        list.Add(40);
        try { stale.Reset(); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        try { stale.MoveNext(); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }

        // Generic Current of Queue/Stack enumerators checks the state too.
        var qe = queue.GetEnumerator();
        try { Console.WriteLine(qe.Current); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        var se = stack.GetEnumerator();
        while (se.MoveNext()) { }
        try { Console.WriteLine(se.Current); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        var sse = sset.GetEnumerator();
        while (sse.MoveNext()) { }
        Console.WriteLine(sse.Current);

        // Dictionary enumerators are IDictionaryEnumerators too.
        IDictionaryEnumerator de = dict.GetEnumerator();
        try { Console.WriteLine(de.Key); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        while (de.MoveNext()) Console.WriteLine(de.Key + "=" + de.Value + " " + de.Entry.Key + " " + de.Entry);
        IDictionaryEnumerator sde = sdict.GetEnumerator();
        while (sde.MoveNext()) Console.WriteLine(sde.Entry.Key + "=" + sde.Entry.Value);
        var entry = new DictionaryEntry("k", 5);
        entry.Value = null;
        var (ek, ev) = entry;
        Console.WriteLine(entry + " " + ek + "|" + (ev == null));

        // The boxed enumerator reports the CLR name of the constructed nested type.
        Console.WriteLine(boxed.GetType());
        Console.WriteLine(boxed.GetType() == typeof(List<int>.Enumerator));
        Console.WriteLine(dict.Keys.GetEnumerator().GetType());
    }
}
