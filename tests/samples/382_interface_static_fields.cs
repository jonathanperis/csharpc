// Static fields of interfaces with non-constant initializers (C# 8+) initialize like a class's: on
// first static field access. They used to stay zero (found by the compatibility corpus).
Console.WriteLine(IShape.Describe());
IShape.SetScale(3);
Console.WriteLine(IShape.Describe());
Console.WriteLine(((IShape)new Square()).Area());
interface IShape
{
    private static TimeSpan window = new TimeSpan(2, 0, 0);
    private static decimal factor = 0.10m;
    static int scale = Compute();
    static int Compute() { Console.WriteLine("init IShape"); return 2; }
    public static void SetScale(int s) => scale = s;
    public static string Describe() => $"{window.TotalHours} {factor} {scale}";
    double Area() => scale * 1.5;
}
class Square : IShape { }
