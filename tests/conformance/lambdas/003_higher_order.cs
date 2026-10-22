Console.WriteLine(Apply(x => x + 1, 5));
Func<int, int> compose = Compose(x => x * 2, x => x + 3);
Console.WriteLine(compose(4));
static int Apply(Func<int, int> f, int v) => f(v);
static Func<int, int> Compose(Func<int, int> f, Func<int, int> g) => x => g(f(x));
