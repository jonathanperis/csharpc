static int L(string s, int v) { Console.WriteLine(s); return v; }
var p = new P(L("ctor-arg", 1)) { B = L("B", 2), A = L("A", 3) };
Console.WriteLine($"{p.A} {p.B}");
class P { public P(int x) { Console.WriteLine("ctor"); A = x; } public int A { get; set; } public int B { get; set; } }
