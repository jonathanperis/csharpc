A x = new C();
Console.WriteLine(x.M());
class A { public virtual string M() => "A"; }
class B : A { public sealed override string M() => "B(sealed)"; }
class C : B { public new string M() => "C"; }
