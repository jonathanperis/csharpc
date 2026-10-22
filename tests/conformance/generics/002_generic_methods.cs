Console.WriteLine(Max(3, 7));
Console.WriteLine(Max("apple", "banana"));
Console.WriteLine(Identity<double>(2.5));
int a = 1, b = 2;
Swap(ref a, ref b);
Console.WriteLine($"{a} {b}");
static T Max<T>(T x, T y) where T : IComparable<T> => x.CompareTo(y) >= 0 ? x : y;
static T Identity<T>(T x) => x;
static void Swap<T>(ref T x, ref T y) { T t = x; x = y; y = t; }
