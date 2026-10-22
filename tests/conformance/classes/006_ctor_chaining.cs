Console.WriteLine(new P().Describe());
Console.WriteLine(new P(3).Describe());
class P
{
    int _x, _y;
    public P() : this(1) { }
    public P(int x) : this(x, x * 2) { }
    public P(int x, int y) { _x = x; _y = y; }
    public string Describe() => _x + "," + _y;
}
