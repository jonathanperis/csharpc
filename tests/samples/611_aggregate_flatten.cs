// AggregateException.Flatten: nested aggregates' inner exceptions breadth first, in a new aggregate
// with the original message.
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nested = new AggregateException("outer",
            new InvalidOperationException("a"),
            new AggregateException("mid", new ArgumentException("b"), new AggregateException(new FormatException("c"))),
            new NotSupportedException("d"));
        var flat = nested.Flatten();
        Console.WriteLine(flat.Message);
        Console.WriteLine(string.Join(",", flat.InnerExceptions.Select(e => e.GetType().Name + ":" + e.Message)));
        Console.WriteLine($"{flat.InnerExceptions.Count} {ReferenceEquals(flat, nested)} {flat.InnerException!.Message}");
        var plain = new AggregateException().Flatten();
        Console.WriteLine($"{plain.Message} | {plain.InnerExceptions.Count}");
        var single = new AggregateException(new AggregateException("inner only")).Flatten();
        Console.WriteLine($"{single.Message} | {single.InnerExceptions.Count}");
    }
}
