{
    using var a = new R("a");
    using var b = new R("b");
    Console.WriteLine("body");
}
using (var c = new R("c")) using (var d = new R("d")) Console.WriteLine("nested body");
class R(string n) : IDisposable { public void Dispose() => Console.WriteLine("dispose " + n); }
