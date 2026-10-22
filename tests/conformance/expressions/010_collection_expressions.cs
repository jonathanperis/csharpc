int[] a = [1, 2, 3];
List<int> l = [4, 5];
int[] all = [.. a, .. l, 6];
Console.WriteLine(string.Join(",", all));
Span<int> s = [7, 8];
Console.WriteLine(s[1]);
IEnumerable<string> e = ["x", "y"];
Console.WriteLine(string.Join("", e));
