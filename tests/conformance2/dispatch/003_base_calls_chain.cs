Console.WriteLine(new C().Name());
class A { public virtual string Name() => "A"; }
class B : A { public override string Name() => "B>" + base.Name(); }
class C : B { public override string Name() => "C>" + base.Name(); }
