int a = 1, b = 2;
Swap(ref a, ref b);
Console.WriteLine(a + " " + b);
Split(17, out int q, out int r);
Console.WriteLine(q + " " + r);
Console.WriteLine(Twice(in a));
static void Swap(ref int x, ref int y) { (x, y) = (y, x); }
static void Split(int n, out int q, out int r) { q = n / 5; r = n % 5; }
static int Twice(in int x) => x * 2;
