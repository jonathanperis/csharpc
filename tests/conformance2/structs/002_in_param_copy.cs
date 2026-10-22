var p = new P { X = 1 };
Touch(in p);
Console.WriteLine(p.X);
static void Touch(in P p) { p.Bump(); Console.WriteLine(p.X); }
struct P { public int X; public void Bump() => X++; }
