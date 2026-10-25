// Delegates bound to interface and virtual methods: the implementation comes from the receiver's
// interface map / vtable when the delegate is created, and a null receiver throws
// NullReferenceException (ldvirtftn), unlike a non-virtual instance method (ArgumentException).
using System;

interface IFoo { int Get(); string Name(string p); }
interface IBar<T> { T Echo(T v); }
class Foo : IFoo, IBar<string>
{
    public int Get() => 42;
    public string Name(string p) => "Foo:" + p;
    string IBar<string>.Echo(string v) => "bar " + v;
}
class Base { public virtual int V() => 1; public int Plain() => 0; }
class Derived : Base { public override int V() => 2; }
sealed class Last : Base { public override int V() => 3; }
struct S : IFoo { public int X; public int Get() => X; public string Name(string p) => "S:" + p + X; }

class Program
{
    static string Try(Func<Func<int>> bind)
    {
        try { return bind()().ToString(); }
        catch (Exception e) { return e.GetType().Name; }
    }

    static void Main()
    {
        IFoo f = new Foo();
        Func<int> d = f.Get;
        Func<string, string> n = f.Name;
        Console.WriteLine(d() + " " + n("x"));
        IBar<string> b = new Foo();
        Func<string, string> e = b.Echo;
        Console.WriteLine(e("y"));
        IFoo s = new S { X = 7 };
        Func<int> sd = s.Get;
        Console.WriteLine(sd() + " " + ((Func<string, string>)s.Name)("z"));
        Base bd = new Derived();
        Func<int> v = bd.V;
        Console.WriteLine(v() + " " + ((Func<int>)new Last().V)());

        IFoo noFoo = null!;
        Base noBase = null!;
        Last noLast = null!;
        Console.WriteLine(Try(() => noFoo.Get));
        Console.WriteLine(Try(() => noBase.V));
        Console.WriteLine(Try(() => noLast.V));
        Console.WriteLine(Try(() => noBase.Plain));
    }
}
