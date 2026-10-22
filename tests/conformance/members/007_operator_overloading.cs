var a = new V(1, 2); var b = new V(3, 4);
Console.WriteLine(a + b);
Console.WriteLine(a * 3);
Console.WriteLine(-a);
Console.WriteLine(a == new V(1, 2));
class V(int x, int y)
{
    public int X = x, Y = y;
    public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y);
    public static V operator *(V a, int k) => new(a.X * k, a.Y * k);
    public static V operator -(V a) => new(-a.X, -a.Y);
    public static bool operator ==(V a, V b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(V a, V b) => !(a == b);
    public override bool Equals(object? o) => o is V v && v == this;
    public override int GetHashCode() => X ^ Y;
    public override string ToString() => $"({X}, {Y})";
}
