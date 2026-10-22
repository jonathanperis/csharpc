Meters m = 5.5;
double d = (double)m;
Console.WriteLine(d);
Console.WriteLine(((Meters)2.0).Value);
class Meters(double v)
{
    public double Value = v;
    public static implicit operator Meters(double v) => new(v);
    public static explicit operator double(Meters m) => m.Value;
}
