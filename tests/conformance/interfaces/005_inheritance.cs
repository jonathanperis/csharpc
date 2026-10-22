IDerived d = new Impl();
Console.WriteLine(d.A() + d.B());
IBase b = d;
Console.WriteLine(b.A());
interface IBase { int A(); }
interface IDerived : IBase { int B(); }
class Impl : IDerived { public int A() => 1; public int B() => 2; }
