Ov.M(1); Ov.M(1, 2); Ov.M(1, c: "x"); Ov.M(b: 3, a: 4);
Ov.N("hi");
static class Ov
{
    public static void M(int a, int b = 10, string c = "dflt") => Console.WriteLine($"{a} {b} {c}");
    public static void N(object o) => Console.WriteLine("object");
    public static void N(string s, int x = 0) => Console.WriteLine("string+opt");
}
