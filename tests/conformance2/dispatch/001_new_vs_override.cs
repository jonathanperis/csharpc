B b = new D();
A a = b;
Console.WriteLine(a.M() + " " + b.M() + " " + ((D)b).M());
Console.WriteLine(a.V() + " " + b.V());
class A { public virtual string M() => "A.M"; public virtual string V() => "A.V"; }
class B : A { public override string M() => "B.M"; public new virtual string V() => "B.V"; }
class D : B { public new string M() => "D.M"; public override string V() => "D.V"; }
