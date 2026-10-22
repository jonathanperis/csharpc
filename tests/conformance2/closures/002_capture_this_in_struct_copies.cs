var s = new S { V = 1 };
var f = s.Getter();
s.V = 2;
Console.WriteLine($"{f()} {s.V}");
struct S { public int V; public Func<int> Getter() { var copy = this; return () => copy.V; } }
