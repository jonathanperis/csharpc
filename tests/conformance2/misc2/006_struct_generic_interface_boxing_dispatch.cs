IComparable c = 5;
Console.WriteLine(c.CompareTo(3) + " " + c.CompareTo(5));
IEquatable<int> e = 7;
Console.WriteLine(e.Equals(7));
object o = new V(2);
Console.WriteLine(((IFormattable)(object)3.5).ToString("F2", null));
Console.WriteLine(o is IComparable<V> cv ? cv.CompareTo(new V(1)) : -99);
readonly record struct V(int N) : IComparable<V> { public int CompareTo(V other) => N.CompareTo(other.N); }
