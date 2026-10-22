{
    using var a = new D("a");
    using var b = new D("b");
    Console.WriteLine("body");
}
class D(string n) : IDisposable { public void Dispose() => Console.WriteLine("dispose " + n); }
