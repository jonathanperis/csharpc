// Virtual calls whose receiver's class is known from its allocation bind directly (#370), and every way a local can be
// written other than by `new C(...)` keeps the call virtual: reassignment from other classes, capture by a lambda or local
// function, ref/out/in arguments (Interlocked.Exchange, Unsafe.AsRef), ref locals, deconstruction, ??=, patterns,
// catch and foreach variables, async methods across an await and iterators across a yield, try/catch/finally.
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

interface IShape { string Name(); }

class Shape : IShape
{
    public virtual string Name() => "Shape";
    public virtual string Label => "shape-label";
    public int Radius { get; set; }
}

class Circle : Shape
{
    public override string Name() => "Circle(" + Radius + ")";
    public override string Label => "circle-label";
}

sealed class Square : Shape
{
    public override string Name() => "Square";
}

class Base
{
    public string Seen;
    // The constructor makes a virtual call: on a Derived it reaches Derived's override.
    public Base() { Seen = Describe(); }
    public virtual string Describe() => "Base";
}

class Derived : Base
{
    public override string Describe() => "Derived";
}

class Box<T>
{
    public virtual string Describe() => "Box<" + typeof(T).Name + ">";
}

class BigBox<T> : Box<T>
{
    public override string Describe() => "BigBox<" + typeof(T).Name + ">";
}

class Failure : Exception
{
    public virtual string Kind => "Failure";
}

class OtherFailure : Failure
{
    public override string Kind => "OtherFailure";
}

static class Program
{
    static void Show(string what, string value) => Console.WriteLine(what + ": " + value);

    static void Replace(ref Shape shape) => shape = new Circle();
    static void Make(out Shape shape) => shape = new Square();
    static void Throw() => throw new InvalidOperationException("boom");

    static async Task<string> AcrossAwait()
    {
        Shape exact = new Circle { Radius = 3 };
        Shape mixed = new Shape();
        await Task.Yield();
        mixed = new Square();
        await Task.Yield();
        return exact.Name() + " " + mixed.Name();
    }

    static IEnumerable<string> AcrossYield()
    {
        Shape exact = new Circle { Radius = 4 };
        Shape mixed = new Shape();
        yield return exact.Name() + " " + mixed.Name();
        mixed = new Circle { Radius = 5 };
        yield return exact.Name() + " " + mixed.Name();
    }

    static void Main()
    {
        // Exact from the allocation, even though Circle overrides Shape.Name (class hierarchy analysis can't decide).
        var plain = new Shape();
        Show("plain", plain.Name() + " " + plain.Label);
        Shape declaredBase = new Circle { Radius = 1 };
        Show("declared base, created derived", declaredBase.Name() + " " + declaredBase.Label);
        Base viaCtor = new Derived();
        Show("constructor's virtual call", viaCtor.Seen + " " + viaCtor.Describe());
        IShape viaInterface = new Circle { Radius = 2 };
        Show("interface-typed local", viaInterface.Name());
        var copied = new Circle { Radius = 6 };
        Shape copy = copied;
        Show("copy of an exact local", copy.Name());
        Shape same = DateTime.Now.Year > 2000 ? new Circle { Radius = 7 } : new Circle { Radius = 8 };
        Show("both branches the same class", same.Name());
        var generic = new BigBox<int>();
        Show("generic class", generic.Describe());

        // Assigned other classes: still dispatched.
        Shape changing = new Shape();
        var names = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            names.Add(changing.Name());
            changing = i % 2 == 0 ? new Circle { Radius = i } : new Square();
        }
        Show("reassigned in a loop", string.Join(",", names));

        // Written by a lambda or a local function.
        Shape captured = new Shape();
        Action change = () => captured = new Circle { Radius = 9 };
        change();
        Show("captured by a lambda", captured.Name());
        Shape byLocalFunction = new Shape();
        void Swap() => byLocalFunction = new Square();
        Swap();
        Show("captured by a local function", byLocalFunction.Name());

        // By reference.
        Shape byRef = new Shape();
        Replace(ref byRef);
        Show("ref argument", byRef.Name());
        Make(out Shape byOut);
        Show("out argument", byOut.Name());
        Shape exchanged = new Shape();
        var previous = Interlocked.Exchange(ref exchanged, new Square());
        Show("Interlocked.Exchange", previous.Name() + " " + exchanged.Name());
        Shape asRef = new Shape();
        var replacement = new Circle { Radius = 10 };
        Unsafe.AsRef(in asRef) = replacement;
        Show("Unsafe.AsRef", asRef.Name());
        Shape aliased = new Shape();
        ref Shape alias = ref aliased;
        alias = new Square();
        Show("ref local", aliased.Name());

        // Deconstruction, ??=, patterns, foreach, null.
        Shape first = new Shape();
        var eleven = new Circle { Radius = 11 };
        (first, _) = (eleven, 0);
        Show("deconstruction", first.Name());
        Shape coalesced = null;
        coalesced ??= new Square();
        Show("??=", coalesced.Name());
        object boxed = new Circle { Radius = 12 };
        if (boxed is Shape pattern) Show("pattern", pattern.Name());
        foreach (Shape each in new Shape[] { new Square(), new Circle { Radius = 13 } }) Show("foreach", each.Name());
        Shape maybe = null;
        if (DateTime.Now.Year > 2000) maybe = new Circle { Radius = 14 };
        Show("null, then a creation", maybe.Name());
        Shape never = null;
        if (DateTime.Now.Year < 2000) never = new Circle();
        try { Show("null receiver", never.Name()); }
        catch (NullReferenceException) { Show("null receiver", "NullReferenceException"); }

        // try / catch / finally.
        Shape guarded = new Shape();
        try
        {
            guarded = new Circle { Radius = 15 };
            Throw();
        }
        catch (InvalidOperationException) { Show("catch", guarded.Name()); }
        finally { Show("finally", guarded.Name()); }
        Shape onlyCircles = new Circle { Radius = 16 };
        try
        {
            onlyCircles = new Circle { Radius = 17 };
            Throw();
        }
        catch (InvalidOperationException) { Show("catch, one class", onlyCircles.Name()); }
        finally { Show("finally, one class", onlyCircles.Name()); }
        try { throw new OtherFailure(); }
        catch (Failure failure)
        {
            Show("catch variable", failure.Kind);
            failure = new Failure();
            Show("catch variable, reassigned", failure.Kind);
        }

        // Async methods and iterators keep their locals across suspensions.
        Show("async", AcrossAwait().GetAwaiter().GetResult());
        foreach (var line in AcrossYield()) Show("iterator", line);
    }
}
