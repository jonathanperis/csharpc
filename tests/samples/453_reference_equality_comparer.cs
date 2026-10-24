// RuntimeHelpers.GetHashCode and ReferenceEqualityComparer: identity hashing even for types that
// override Equals/GetHashCode (Roslyn keys caches by node identity this way).
using System.Collections;
using System.Runtime.CompilerServices;

var a = new Point(1, 2);
var b = new Point(1, 2);
Console.WriteLine($"{a == b} {a.Equals(b)} {ReferenceEquals(a, b)}");
Console.WriteLine(RuntimeHelpers.GetHashCode(a) == RuntimeHelpers.GetHashCode(a));
Console.WriteLine(RuntimeHelpers.GetHashCode(null));
var plain = new object();
Console.WriteLine(plain.GetHashCode() == RuntimeHelpers.GetHashCode(plain));

var comparer = ReferenceEqualityComparer.Instance;
Console.WriteLine(ReferenceEquals(comparer, ReferenceEqualityComparer.Instance));
Console.WriteLine($"{comparer.Equals(a, b)} {comparer.Equals(a, a)} {comparer.Equals(null, null)} {comparer.Equals(a, null)}");
Console.WriteLine(comparer.GetHashCode(a) == RuntimeHelpers.GetHashCode(a));

var byValue = new HashSet<Point> { a, b };
var byIdentity = new HashSet<object>(ReferenceEqualityComparer.Instance) { a, b, a };
Console.WriteLine($"{byValue.Count} {byIdentity.Count}");

var visits = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
foreach (var p in new[] { a, b, a, a })
    visits[p] = visits.TryGetValue(p, out var n) ? n + 1 : 1;
Console.WriteLine(string.Join(",", visits.Values));

IEqualityComparer nonGeneric = ReferenceEqualityComparer.Instance;
Console.WriteLine($"{nonGeneric.Equals("x", "x")} {nonGeneric.Equals(a, b)}");
IEqualityComparer<Point> contravariant = ReferenceEqualityComparer.Instance;
Console.WriteLine(contravariant.Equals(a, b));

record Point(int X, int Y);
