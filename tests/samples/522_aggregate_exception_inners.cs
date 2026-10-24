// AggregateException keeps every inner exception: ToString's "(Inner Exception #i)" parts,
// InnerExceptions, Handle, and the aggregates of Task.WaitAll / Task.WhenAll.
using System;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var ae = new AggregateException("multi", new InvalidOperationException("a"), new ArgumentException("b"));
        Console.WriteLine(ae.Message);
        Console.WriteLine(ae.ToString());
        Console.WriteLine(new AggregateException(new InvalidOperationException("only")).ToString());
        Console.WriteLine(new AggregateException("none").ToString());
        Console.WriteLine($"{ae.InnerExceptions.Count} {string.Join(",", ae.InnerExceptions.Select(e => e.Message))} {new AggregateException().InnerExceptions.Count}");
        var same = new InvalidOperationException("twice");
        Console.WriteLine(new AggregateException(same, same, new FormatException("f")).ToString());

        try { ae.Handle(e => e is InvalidOperationException); }
        catch (AggregateException rest) { Console.WriteLine($"{rest.InnerExceptions.Count} {rest.InnerExceptions[0].GetType().Name} | {rest.Message}"); }
        ae.Handle(e => true);
        Console.WriteLine("all handled");

        var t1 = Task.Run(() => throw new InvalidOperationException("t1"));
        var t2 = Task.Run(() => throw new ArgumentException("t2"));
        try { Task.WaitAll(t1, t2); }
        catch (AggregateException e) { Console.WriteLine($"{e.InnerExceptions.Count} {string.Join(",", e.InnerExceptions.Select(x => x.Message))}"); }
        var all = Task.WhenAll(t1, t2);
        try { await all; } catch (InvalidOperationException e) { Console.WriteLine("await: " + e.Message); }
        Console.WriteLine($"{all.Exception!.InnerExceptions.Count} {all.Exception.Message}");
        Console.WriteLine(t1.Exception!.InnerExceptions.Count + " " + t1.Exception.InnerException!.Message);
    }
}
