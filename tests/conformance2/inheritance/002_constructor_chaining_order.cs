new C(5);
class A { public A() => Console.WriteLine("A()"); public A(int x) : this() => Console.WriteLine($"A({x})"); }
class B : A { protected int f = Log("B.field"); public B(int x) : base(x * 2) => Console.WriteLine($"B({x})"); protected static int Log(string s) { Console.WriteLine(s); return 0; } }
class C : B { int g = Log("C.field"); public C(int x) : base(x + 1) => Console.WriteLine($"C({x})"); }
