// Reading a property through a type whose override has only a setter calls the nearest getter, virtually.
using System;

var d = new Derived { Value = 5 };
Console.WriteLine(d.Value);
Base b = d;
Console.WriteLine(b.Value);
var e = new MoreDerived { Value = 2 };
Console.WriteLine(e.Value);
Console.WriteLine(((Derived)e).Value);

// Compound assignments read and write through the nearest accessors, both called virtually.
d.Value += 1;
Console.WriteLine(d.Value);
e.Value += 3;
Console.WriteLine(e.Value);
Derived viaBase = new MoreDerived { Value = 1 };
viaBase.Value++;
Console.WriteLine(viaBase.Value);

class Base
{
    protected int stored;
    public virtual int Value { get => stored; set => stored = value; }
}

class Derived : Base
{
    public override int Value { set => stored = value * 10; }
}

class MoreDerived : Derived
{
    public override int Value { get => stored + 1; }
}
