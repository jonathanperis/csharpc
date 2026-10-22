new D();
class B { public B() { Console.WriteLine("B ctor"); } }
class D : B
{
    int f = Log("D field");
    public D() { Console.WriteLine("D ctor"); }
    static int Log(string s) { Console.WriteLine(s); return 0; }
}
