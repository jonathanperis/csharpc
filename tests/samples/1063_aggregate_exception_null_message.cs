// AggregateException constructed with a null message: .NET substitutes its default text, with the inner messages
// appended as for any other message (not Exception's "Exception of type ... was thrown.").
using System;
using System.Collections.Generic;
using System.Linq;

static void Show(string label, Func<AggregateException> make)
{
    var e = make();
    Console.WriteLine($"{label}: [{e.Message}] {e.InnerExceptions.Count}");
}

Show("null message only", () => new AggregateException((string)null!));
Show("null message and inner", () => new AggregateException((string)null!, new Exception("z")));
Show("null message and inners", () => new AggregateException((string)null!, new Exception("a"), new InvalidOperationException("b")));
Show("null message and enumerable", () => new AggregateException((string)null!, new List<Exception> { new Exception("c") }.AsEnumerable()));
Show("empty message", () => new AggregateException("", new Exception("z")));
Show("message", () => new AggregateException("custom", new Exception("z")));
Show("no arguments", () => new AggregateException());
Show("inner only", () => new AggregateException(new Exception("only")));
Show("inners only", () => new AggregateException(new Exception("one"), new Exception("two")));
Show("flattened null message", () => new AggregateException((string)null!, new AggregateException("nested", new Exception("deep"))).Flatten());

Show("derived null message", () => new Derived(null, new Exception("d")));
Show("derived message", () => new Derived("derived", new Exception("d")));

try { throw new AggregateException((string)null!, new Exception("thrown")); }
catch (AggregateException e) { Console.WriteLine("caught: " + e.Message); }

class Derived : AggregateException
{
    public Derived(string? message, params Exception[] inner) : base(message, inner) { }
}
