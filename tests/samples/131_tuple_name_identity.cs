// Tuple element names are not part of runtime type identity.
var named = new List<(int Key, string Value)> { (1, "one"), (2, "two") };
IEnumerable<(int, string)> unnamed = named;
Console.WriteLine(unnamed.Count());
object o = named;
Console.WriteLine(o is List<(int, string)>);
Console.WriteLine(o is IEnumerable<(int A, string B)> e ? e.First().B : "no");
var dict = named.ToDictionary(t => t.Key, t => t.Value);
Console.WriteLine(dict[2]);
