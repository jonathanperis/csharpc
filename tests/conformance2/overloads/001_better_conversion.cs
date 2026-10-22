byte b = 1; short s = 2; float f = 3;
Ov.M(b); Ov.M(s); Ov.M(f); Ov.M(5u); Ov.M('c');
static class Ov
{
    public static void M(int x) => Console.WriteLine("int");
    public static void M(long x) => Console.WriteLine("long");
    public static void M(double x) => Console.WriteLine("double");
}
