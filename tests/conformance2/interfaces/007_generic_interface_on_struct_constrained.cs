Console.WriteLine(Max(new[] { 3, 9, 2 }));
Console.WriteLine(Max(new[] { "pear", "apple", "zoo" }));
Console.WriteLine(Max(new[] { new V(2), new V(7), new V(5) }));
static T Max<T>(T[] xs) where T : IComparable<T> { var m = xs[0]; foreach (var x in xs) if (x.CompareTo(m) > 0) m = x; return m; }
readonly record struct V(int N) : IComparable<V> { public int CompareTo(V o) => N.CompareTo(o.N); }
