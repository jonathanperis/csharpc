(P? a, int b) t = (new P(1, 2), 3);
Console.WriteLine(t.a?.X + t.b);
t.a = null;
Console.WriteLine(t.a.HasValue);
var box = (object)(new P(5, 6), "s");
Console.WriteLine(box);
readonly record struct P(int X, int Y);
