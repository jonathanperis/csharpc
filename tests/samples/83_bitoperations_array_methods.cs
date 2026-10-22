// BitOperations (LLVM ctpop/ctlz/cttz/fshl) and System.Array helpers.
using System.Numerics;

uint u = 0b1011_0000;
ulong big = 0x8000_0000_0000_0001UL;
Console.WriteLine($"{BitOperations.PopCount(u)} {BitOperations.LeadingZeroCount(u)} {BitOperations.TrailingZeroCount(u)} {BitOperations.Log2(u)}");
Console.WriteLine($"{BitOperations.PopCount(big)} {BitOperations.LeadingZeroCount(big)} {BitOperations.TrailingZeroCount(big)} {BitOperations.Log2(big)}");
Console.WriteLine($"{BitOperations.RotateLeft(0x80000001u, 1)} {BitOperations.RotateRight(1u, 1)} {BitOperations.RotateLeft(1UL, 63)}");
Console.WriteLine($"{BitOperations.IsPow2(64)} {BitOperations.IsPow2(0)} {BitOperations.IsPow2(-8)} {BitOperations.IsPow2(96u)} {BitOperations.LeadingZeroCount(0u)}");
Console.WriteLine($"{int.PopCount(255)} {uint.LeadingZeroCount(1u)} {long.TrailingZeroCount(1024L)}");

int[] a = { 5, 2, 9, 1, 7 };
Array.Reverse(a);
Console.WriteLine(string.Join(",", a));
Array.Sort(a);
Console.WriteLine(string.Join(",", a) + " idx(7)=" + Array.IndexOf(a, 7) + " idx(4)=" + Array.IndexOf(a, 4));
Console.WriteLine(Array.BinarySearch(a, 7) + " " + Array.BinarySearch(a, 6) + " " + Array.BinarySearch(new[] { 1, 3, 5, 7 }, 5));
Console.WriteLine(Array.Exists(a, x => x > 8) + " " + Array.Exists(a, x => x > 9) + " " + Array.Find(a, x => x % 2 == 0) + " " + Array.FindIndex(a, x => x > 4) + " " + Array.TrueForAll(a, x => x > 0));
int[] copy = new int[a.Length];
Array.Copy(a, copy, a.Length);
Array.Fill(a, 0);
Console.WriteLine(copy[0] + " " + a[0] + " " + string.Join(" ", copy));
Array.Copy(copy, 1, a, 0, 3);
Console.WriteLine(string.Join(" ", a));
Array.Clear(a);
Console.WriteLine(string.Join(" ", a));
Array.Resize(ref copy, 7);
Console.WriteLine(copy.Length + " " + string.Join("|", copy));
Array.Resize(ref copy, 2);
Console.WriteLine(string.Join("|", copy));
string[] words = ["pear", "apple", "fig", "Banana", "cherry"];
Array.Sort(words);
Console.WriteLine(string.Join(" ", words) + " " + Array.IndexOf(words, "fig"));
double[] ds = [3.5, double.NaN, -1, 0.25, double.NegativeInfinity];
Array.Sort(ds);
Console.WriteLine(string.Join(" ", ds));
char[] cs = "hello".ToCharArray();
Array.Reverse(cs);
Console.WriteLine(new string(cs) + " " + string.Join("-", cs));
long[] ls = [30, 10, 20];
Array.Sort(ls);
Console.WriteLine(string.Join(",", ls) + " " + string.Concat(words));
Array.ForEach(ls, x => Console.Write(x / 10));
Console.WriteLine();
Point[] pts = [new(1, 2), new(3, 4)];
Array.Reverse(pts);
Console.WriteLine(pts[0].X + " " + string.Join(";", pts));
Console.WriteLine(Array.Empty<int>().Length + " " + new int[2].GetType().Name + " " + words.GetType());

struct Point(int x, int y)
{
    public int X = x, Y = y;
    public override string ToString() => $"({X},{Y})";
}
