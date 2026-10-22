var p = new Pair<int, string>(1, "x");
Console.WriteLine(p);
struct Pair<A, B>(A first, B second) { public override string ToString() => $"<{first}, {second}>"; }
