Console.WriteLine("hello".Shout());
Console.WriteLine(5.Squared());
static class Ext
{
    public static string Shout(this string s) => s.ToUpper() + "!";
    public static int Squared(this int x) => x * x;
}
