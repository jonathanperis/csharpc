// Tuples of more than seven elements (ValueTuple<T1..T7, TRest> nesting): construction, Item8+, deconstruction, named
// elements, equality, ordering, boxing and its type name, as dictionary keys and list elements.
using System.Collections.Generic;
var t = (1, 2, 3, 4, 5, 6, 7, 8, 9);
Console.WriteLine(t.Item9 + " " + t + " " + t.Item8);
var (a, b, c, d, e, f, g, h, i) = t;
Console.WriteLine(a + i + h);
var named = (x1: 1, x2: "two", x3: 3.0, x4: 'd', x5: 5L, x6: true, x7: 7f, x8: "eight", x9: 9m, x10: (byte)10);
Console.WriteLine(named.x8 + named.x10 + " " + named);
Console.WriteLine(t == (1, 2, 3, 4, 5, 6, 7, 8, 9));
Console.WriteLine(t.Equals((1, 2, 3, 4, 5, 6, 7, 8, 10)));
Console.WriteLine(t.GetHashCode() == (1, 2, 3, 4, 5, 6, 7, 8, 9).GetHashCode());
Console.WriteLine(t.CompareTo((1, 2, 3, 4, 5, 6, 7, 8, 10)) + " " + t.CompareTo(t));
object boxed = t;
Console.WriteLine(boxed.GetType() + " " + boxed + " " + boxed.Equals(t) + " " + (boxed is ValueTuple<int, int, int, int, int, int, int, ValueTuple<int, int>>));
Console.WriteLine(named.GetType());
var list = new List<(int, int, int, int, int, int, int, int)> { (1, 2, 3, 4, 5, 6, 7, 8), (8, 7, 6, 5, 4, 3, 2, 1) };
list.Sort();
Console.WriteLine(string.Join(";", list));
static (int, int, int, int, int, int, int, int, int, int) Make(int n) => (n, n + 1, n + 2, n + 3, n + 4, n + 5, n + 6, n + 7, n + 8, n + 9);
var m = Make(10);
Console.WriteLine(m.Item10 + " " + m);
var d2 = new Dictionary<(int, int, int, int, int, int, int, int), string> { [(1, 2, 3, 4, 5, 6, 7, 8)] = "k" };
Console.WriteLine(d2[(1, 2, 3, 4, 5, 6, 7, 8)]);
var big = new ValueTuple<int, int, int, int, int, int, int, ValueTuple<string, string>>(1, 2, 3, 4, 5, 6, 7, new ValueTuple<string, string>("r1", "r2"));
Console.WriteLine(big);

// Eight elements (the Rest holds one), and writes to the elements past the seventh.
var eight = (1, 2, 3, 4, 5, 6, 7, 8);
eight.Item8 = 80;
Console.WriteLine(eight + " " + (eight.Item8 + eight.Item1));
var nine = (1, 2, 3, 4, 5, 6, 7, 8, 9);
nine.Item9 = 90;
nine.Item8 += 1;
Console.WriteLine(nine);
var copy = nine;
copy.Item9 = 0;
Console.WriteLine(nine.Item9 + " " + copy.Item9);
(int, int, int, int, int, int, int, int, int) swapped = (nine.Item9, nine.Item8, nine.Item7, nine.Item6, nine.Item5, nine.Item4, nine.Item3, nine.Item2, nine.Item1);
Console.WriteLine(swapped);
