Ov.M(); Ov.M(1); Ov.M(1, 2); Ov.M(1, 2, 3); Ov.M(new[] { 4, 5 });
static class Ov
{
    public static void M(params int[] xs) => Console.WriteLine($"params {xs.Length}");
    public static void M(int x) => Console.WriteLine("single");
    public static void M(int x, int y) => Console.WriteLine("two");
}
