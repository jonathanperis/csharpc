Console.WriteLine(MathUtil.Square(7));
Console.WriteLine(MathUtil.Calls);
static class MathUtil
{
    public static int Calls;
    public static int Square(int x) { Calls++; return x * x; }
}
