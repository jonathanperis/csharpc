var d = new D();
d.M(1);
B b = d;
b.M(1);
class B { public virtual void M(int x) => Console.WriteLine("B.M(int)"); }
class D : B { public override void M(int x) => Console.WriteLine("D.M(int)"); public void M(double x) => Console.WriteLine("D.M(double)"); }
