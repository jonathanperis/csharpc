IG g = new D();
g.Hello();
((IG)new B()).Hello();
interface IG { void Hello(); }
class B : IG { public void Hello() => Console.WriteLine("B"); }
class D : B, IG { public new void Hello() => Console.WriteLine("D"); }
