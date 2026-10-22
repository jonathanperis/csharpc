Ov.M(() => 1);
Ov.M(() => 1.5);
Ov.N(() => Console.WriteLine("side"));
Ov.N(() => "val");
static class Ov
{
    public static void M(Func<int> f) => Console.WriteLine("Func<int> " + f());
    public static void M(Func<double> f) => Console.WriteLine("Func<double> " + f());
    public static void N(Action a) { Console.WriteLine("Action"); a(); }
    public static void N(Func<string> f) => Console.WriteLine("Func<string> " + f());
}
