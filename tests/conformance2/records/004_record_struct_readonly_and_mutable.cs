var m = new Mut(1, 2);
m.X = 10;
Console.WriteLine(m);
var r = new Ro(3, 4);
var r2 = r with { Y = 9 };
Console.WriteLine($"{r} {r2} {r == new Ro(3, 4)}");
record struct Mut(int X, int Y);
readonly record struct Ro(int X, int Y);
