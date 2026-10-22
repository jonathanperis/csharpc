var t = new Tri(1); var f = new Tri(-1); var u = new Tri(0);
Console.WriteLine((t && f).V + " " + (t || u).V + " " + (u && t).V);
if (t) Console.WriteLine("t is true");
if (!u) Console.WriteLine("u is falsy?");
readonly struct Tri(int v) {
    public int V => v;
    public static bool operator true(Tri x) => x.V > 0;
    public static bool operator false(Tri x) => x.V < 0;
    public static Tri operator &(Tri a, Tri b) => new(Math.Min(a.V, b.V));
    public static Tri operator |(Tri a, Tri b) => new(Math.Max(a.V, b.V));
    public static Tri operator !(Tri a) => new(-a.V);
}
