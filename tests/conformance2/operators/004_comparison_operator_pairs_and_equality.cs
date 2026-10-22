var a = new Ver(1); var b = new Ver(2);
Console.WriteLine($"{a < b} {a > b} {a <= b} {a >= b} {a == new Ver(1)} {a != b}");
class Ver(int n) : IComparable<Ver> {
    public int N => n;
    public int CompareTo(Ver? o) => n.CompareTo(o!.N);
    public static bool operator <(Ver x, Ver y) => x.CompareTo(y) < 0;
    public static bool operator >(Ver x, Ver y) => x.CompareTo(y) > 0;
    public static bool operator <=(Ver x, Ver y) => x.CompareTo(y) <= 0;
    public static bool operator >=(Ver x, Ver y) => x.CompareTo(y) >= 0;
    public static bool operator ==(Ver? x, Ver? y) => x?.N == y?.N;
    public static bool operator !=(Ver? x, Ver? y) => !(x == y);
    public override bool Equals(object? o) => o is Ver v && v.N == n;
    public override int GetHashCode() => n;
}
