// Higher-order functions: closures outliving their frame, composition, recursion via delegates.
Func<int> counter = MakeCounter();
counter(); counter();
Console.WriteLine(counter());

Func<int> other = MakeCounter();
Console.WriteLine(other());                          // independent state

Func<int, int> inc = x => x + 1;
Func<int, int> dbl = x => x * 2;
Console.WriteLine(Compose(inc, dbl)(5));
Console.WriteLine(Compose(dbl, inc)(5));
Console.WriteLine(Twice(dbl)(3));

int[] numbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
Print(Map(numbers, x => x * x));
Print(Filter(numbers, x => x % 3 == 0));
Console.WriteLine(Fold(numbers, 0, (acc, x) => acc + x));
Console.WriteLine(Fold(numbers, 1, (acc, x) => acc * x));

Func<int, int> fib = null!;
fib = n => n < 2 ? n : fib(n - 1) + fib(n - 2);      // recursion through a captured delegate
Console.WriteLine(fib(20));

var memo = new long[60];
Func<int, long> fastFib = null!;
fastFib = n =>
{
    if (n < 2) return n;
    if (memo[n] != 0) return memo[n];
    return memo[n] = fastFib(n - 1) + fastFib(n - 2);
};
Console.WriteLine(fastFib(55));

static Func<int> MakeCounter()
{
    int count = 0;
    return () => ++count;
}

static Func<int, int> Compose(Func<int, int> f, Func<int, int> g) => x => g(f(x));
static Func<int, int> Twice(Func<int, int> f) => Compose(f, f);

static int[] Map(int[] items, Func<int, int> f)
{
    var result = new int[items.Length];
    for (int i = 0; i < items.Length; i++) result[i] = f(items[i]);
    return result;
}

static int[] Filter(int[] items, Func<int, bool> keep)
{
    int count = 0;
    foreach (var x in items) if (keep(x)) count++;
    var result = new int[count];
    int j = 0;
    foreach (var x in items) if (keep(x)) result[j++] = x;
    return result;
}

static int Fold(int[] items, int seed, Func<int, int, int> f)
{
    int acc = seed;
    foreach (var x in items) acc = f(acc, x);
    return acc;
}

static void Print(int[] items)
{
    string line = "";
    foreach (var x in items) line += x + " ";
    Console.WriteLine(line);
}
