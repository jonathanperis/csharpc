// Generics meet closures: generic higher-order functions, lambdas capturing T-typed locals,
// generic local functions, delegates stored in generic classes.
int[] xs = [1, 2, 3, 4];
string[] mapped = Map(xs, x => "#" + x);
Console.WriteLine(string.Join(",", mapped));
Console.WriteLine(Sum(Map(xs, x => x * 1.5)));
var evens = Filter(xs, x => x % 2 == 0);
Console.WriteLine(evens.Length + " " + evens[1]);
int offset = 10;
Console.WriteLine(Fold(xs, 0, (acc, x) => acc + x + offset));
Console.WriteLine(Apply(Twice, 5) + " " + Apply<string>(s => s + s, "ab"));
var counter = MakeCounter<long>(100);
counter(); counter();
Console.WriteLine(counter());
Console.WriteLine(new Cache<int, string>(k => "v" + k).Get(7));
Console.WriteLine(Pair(3, "x"));

static U[] Map<T, U>(T[] items, Func<T, U> f)
{
    var result = new U[items.Length];
    for (int i = 0; i < items.Length; i++) result[i] = f(items[i]);
    return result;
}
static T[] Filter<T>(T[] items, Func<T, bool> keep)
{
    int n = 0;
    foreach (var item in items) if (keep(item)) n++;
    var result = new T[n];
    int k = 0;
    foreach (var item in items) if (keep(item)) result[k++] = item;
    return result;
}
static TAcc Fold<T, TAcc>(T[] items, TAcc seed, Func<TAcc, T, TAcc> f)
{
    var acc = seed;
    foreach (var item in items) acc = f(acc, item);
    return acc;
}
static double Sum(double[] values) { double t = 0; foreach (var v in values) t += v; return t; }
static int Twice(int x) => x * 2;
static T Apply<T>(Func<T, T> f, T value) => f(value);
static Func<T> MakeCounter<T>(T start) where T : System.Numerics.INumber<T>
{
    T current = start;
    return () => { current += T.One; return current; };
}
static string Pair<A, B>(A a, B b)
{
    string Show<C>(C c) => "<" + c + ">";
    return Show(a) + Show(b) + Show(1.5);
}
class Cache<TKey, TValue>(Func<TKey, TValue> factory)
{
    public TValue Get(TKey key) => factory(key);
}
