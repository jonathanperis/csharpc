var a = new V(1); var b = new V(2);
Console.WriteLine(a + b);
Console.WriteLine(a + 10);
Console.WriteLine(5 + a);
readonly struct V(int x) {
    public int X => x;
    public static V operator +(V l, V r) => new(l.X + r.X);
    public static V operator +(V l, int r) => new(l.X + r * 100);
    public static V operator +(int l, V r) => new(l * 1000 + r.X);
    public override string ToString() => $"V({X})";
}
