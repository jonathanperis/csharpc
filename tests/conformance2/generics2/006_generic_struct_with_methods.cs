var p = new Pair<int, string>(1, "one");
var q = p.Swap();
Console.WriteLine($"{p} {q} {q.First.Length}");
readonly struct Pair<A, B>(A first, B second) { public A First => first; public B Second => second; public Pair<B, A> Swap() => new(second, first); public override string ToString() => $"({first}, {second})"; }
