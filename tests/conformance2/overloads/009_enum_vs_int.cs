Ov.M(0); Ov.M(Color.Red); Ov.M((int)Color.Green); Ov.M(1);
static class Ov
{
    public static void M(int x) => Console.WriteLine("int");
    public static void M(Color c) => Console.WriteLine("Color");
}
enum Color { Red, Green }
