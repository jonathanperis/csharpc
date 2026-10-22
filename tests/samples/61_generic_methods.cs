// Generic methods: each distinct type argument list is a separate instantiation.
Console.WriteLine(Max(3, 7));
Console.WriteLine(Max(2.5, -1.0));
Console.WriteLine(Max("apple", "banana"));
int a = 1, b = 2;
Swap(ref a, ref b);
Console.WriteLine($"{a} {b}");
string s1 = "x", s2 = "y";
Swap(ref s1, ref s2);
Console.WriteLine(s1 + s2);
Console.WriteLine(Describe(42));
Console.WriteLine(Describe("text"));
Console.WriteLine(Describe(true));
int[] numbers = [5, 1, 4];
string[] names = ["c", "a", "b"];
Console.WriteLine(First(numbers) + " " + First(names));
Console.WriteLine(Count(names, "b"));
Console.WriteLine(DefaultOf<int>() + " [" + DefaultOf<string>() + "]");
var made = Make<Widget>();
Console.WriteLine(made.Name);
Console.WriteLine(Identity(Identity(12.5)));
Console.WriteLine(Mapper.Twice<long>(21L));
Console.WriteLine(Mapper.Twice(1.25));
Console.WriteLine(Mapper.SumAll(new[] { 1, 2, 3, 4 }));

static T Max<T>(T x, T y) where T : IComparable<T> => x.CompareTo(y) >= 0 ? x : y;
static void Swap<T>(ref T x, ref T y) { T tmp = x; x = y; y = tmp; }
static string Describe<T>(T value) => $"{value} ({value!.ToString()!.Length} chars)";
static T First<T>(T[] items) => items[0];
static int Count<T>(T[] items, T wanted) where T : class
{
    int n = 0;
    foreach (var item in items) if (item == wanted) n++;
    return n;
}
static T? DefaultOf<T>() => default;
static T Make<T>() where T : new() => new T();
static T Identity<T>(T value) => value;

class Widget { public string Name = "widget"; }

static class Mapper
{
    // Generic math (static abstract interface members): after monomorphization, plain arithmetic.
    public static T Twice<T>(T value) where T : System.Numerics.INumber<T> => value + value;
    public static T SumAll<T>(T[] values) where T : System.Numerics.INumber<T>
    {
        T total = T.Zero;
        foreach (var v in values) total += v;
        return total;
    }
}
