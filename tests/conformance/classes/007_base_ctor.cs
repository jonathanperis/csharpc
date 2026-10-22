Console.WriteLine(new B(5).Info);
class A { public string Info; public A(string s) { Info = "A(" + s + ")"; } }
class B : A { public B(int n) : base("n=" + n) { Info += "+B"; } }
