Console.WriteLine(Pair(1, "a"));
Console.WriteLine(First(new List<double> { 2.5 }));
Console.WriteLine(Apply(3, x => x * 1.5));
static (A, B) Pair<A, B>(A a, B b) => (a, b);
static T First<T>(IEnumerable<T> xs) { foreach (var x in xs) return x; throw new InvalidOperationException(); }
static R Apply<T, R>(T v, Func<T, R> f) => f(v);
