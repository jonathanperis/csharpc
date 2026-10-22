Console.WriteLine(O.F(1));
Console.WriteLine(O.F(1L));
Console.WriteLine(O.F(1.0));
Console.WriteLine(O.F("s"));
Console.WriteLine(O.F((object)1));
Console.WriteLine(O.F((short)1));
static class O
{
    public static string F(int x) => "int";
    public static string F(long x) => "long";
    public static string F(double x) => "double";
    public static string F(string x) => "string";
    public static string F(object x) => "object";
}
