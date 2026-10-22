var a = new HashSet<int> { 1, 2, 3, 4 };
var b = new HashSet<int> { 3, 4, 5 };
var u = new HashSet<int>(a); u.UnionWith(b);
var i = new HashSet<int>(a); i.IntersectWith(b);
var e = new HashSet<int>(a); e.ExceptWith(b);
var x = new HashSet<int>(a); x.SymmetricExceptWith(b);
Console.WriteLine($"{string.Join(",", u)} | {string.Join(",", i)} | {string.Join(",", e)} | {string.Join(",", x)}");
Console.WriteLine($"{a.IsSupersetOf(i)} {i.IsSubsetOf(b)} {a.Overlaps(b)} {a.SetEquals(new[] { 4, 3, 2, 1 })} {a.Add(1)}");
