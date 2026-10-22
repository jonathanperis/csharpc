Console.WriteLine(new B(1, 2));
Console.WriteLine(new C(3));
record A(int X);
record B(int X, int Y) : A(X);
record C(int Z) { public sealed override string ToString() => $"C!{Z}"; }
