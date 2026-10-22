Ov.M(1); Ov.M(1L); Ov.M("s"); Ov.M<int>(2); Ov.M((object)3);
static class Ov
{
    public static void M<T>(T x) => Console.WriteLine($"generic {typeof(T).Name}");
    public static void M(int x) => Console.WriteLine("int");
    public static void M(object x) => Console.WriteLine("object");
}
