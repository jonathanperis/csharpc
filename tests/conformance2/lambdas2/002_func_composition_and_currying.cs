Func<int, int> inc = x => x + 1, dbl = x => x * 2;
Func<int, int> Compose(Func<int, int> f, Func<int, int> g) => x => g(f(x));
Console.WriteLine(Compose(inc, dbl)(5) + " " + Compose(dbl, inc)(5));
Func<int, Func<int, int>> add = a => b => a + b;
Console.WriteLine(add(3)(4));
