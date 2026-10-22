static int L(int v) { Console.Write(v); return v; }
var a = new[] { L(3), L(1), L(2) };
Console.WriteLine();
var b = new int[2] { L(5), L(6) };
var c = new int[L(4)];
Console.WriteLine($" {a.Length} {b[1]} {c.Length}");
