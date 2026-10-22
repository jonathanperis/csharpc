int v = 1;
Ov.M(v); Ov.M(ref v); Ov.N(v); Ov.N(in v); Ov.N(5);
Ov.O(out var w);
Console.WriteLine(w);
static class Ov
{
    public static void M(int x) => Console.WriteLine("value");
    public static void M(ref int x) => Console.WriteLine("ref");
    public static void N(in int x) => Console.WriteLine("in " + x);
    public static void O(out int x) { x = 42; }
}
