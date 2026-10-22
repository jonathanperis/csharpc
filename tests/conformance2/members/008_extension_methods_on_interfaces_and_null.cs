IEnumerable<int> xs = new[] { 1, 2, 3 };
Console.WriteLine(xs.SumSquares());
string? s = null;
Console.WriteLine(s.OrEmpty().Length);
Console.WriteLine("abc".Reverse2());
static class Ext {
    public static int SumSquares(this IEnumerable<int> xs) => xs.Sum(x => x * x);
    public static string OrEmpty(this string? s) => s ?? "";
    public static string Reverse2(this string s) => new string(s.Reverse().ToArray());
}
