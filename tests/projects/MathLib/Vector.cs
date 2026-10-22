namespace MathLib.Geometry;

public class Vector(double x, double y)
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vector Add(Vector other) => new(X + other.X, Y + other.Y);
    public Vector Scale(double k) => new(X * k, Y * k);
    public override string ToString() => $"({X}, {Y})";
}
