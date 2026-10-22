var set = new HashSet<P> { new(1, 2), new(1, 2), new(2, 1) };
Console.WriteLine(set.Count);
var dict = new Dictionary<P, string> { [new(0, 0)] = "origin" };
Console.WriteLine(dict[new P(0, 0)]);
record P(int X, int Y);
