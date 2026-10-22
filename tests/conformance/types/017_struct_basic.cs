var v = new Vec(3, 4);
Console.WriteLine(v.Length());
var w = v;
w.X = 10;
Console.WriteLine(v.X + " " + w.X);
struct Vec
{
    public double X, Y;
    public Vec(double x, double y) { X = x; Y = y; }
    public double Length() => Math.Sqrt(X * X + Y * Y);
}
