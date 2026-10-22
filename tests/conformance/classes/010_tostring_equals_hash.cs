var a = new K(1); var b = new K(1);
Console.WriteLine(a.Equals(b) + " " + (a == b) + " " + (a.GetHashCode() == b.GetHashCode()));
class K(int v)
{
    public int V = v;
    public override bool Equals(object? o) => o is K k && k.V == V;
    public override int GetHashCode() => V;
}
