ICounter c = new Ctr();
c.Inc(); c.Inc();
Console.WriteLine(c.Value);
var s = new Ctr();
ICounter boxed = s;
boxed.Inc();
Console.WriteLine($"{s.Value} {boxed.Value}");
interface ICounter { void Inc(); int Value { get; } }
struct Ctr : ICounter { int v; public void Inc() => v++; public int Value => v; }
