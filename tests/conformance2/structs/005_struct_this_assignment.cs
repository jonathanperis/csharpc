var v = new V(1, 2);
v.Reset();
Console.WriteLine($"{v.X} {v.Y}");
v.Swap();
Console.WriteLine($"{v.X} {v.Y}");
struct V(int x, int y) {
    public int X = x, Y = y;
    public void Reset() => this = new V(7, 8);
    public void Swap() => (X, Y) = (Y, X);
}
