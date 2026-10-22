int[] a = [1, 2];
List<int> l = [.. a, 3, .. new[] { 4, 5 }];
IEnumerable<int> e = [.. l, 6];
int[] empty = [];
Console.WriteLine($"{string.Join(",", l)} | {string.Join(",", e)} | {empty.Length}");
HashSet<string> hs = ["x", "y", "x"];
Console.WriteLine(hs.Count);
