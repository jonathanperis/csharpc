using (var r = new Res("a")) Console.WriteLine("using a");
using var b = new Res("b");
Console.WriteLine("end of scope");
class Res(string n) : IDisposable { public void Dispose() => Console.WriteLine("dispose " + n); }
