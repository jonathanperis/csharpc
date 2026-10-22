Ov.M(null); Ov.N(null); Ov.N(5);
static class Ov
{
    public static void M(string s) => Console.WriteLine("string");
    public static void M(object o) => Console.WriteLine("object");
    public static void N(int? x) => Console.WriteLine("int?");
    public static void N(object o) => Console.WriteLine("object");
}
