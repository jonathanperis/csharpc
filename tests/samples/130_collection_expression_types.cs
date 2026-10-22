// Collection expressions targeting collection classes and interfaces (incl. spreads).
List<int> list = [1, 2, 3];
int[] more = [4, 5];
List<int> all = [0, .. list, .. more, 6];
HashSet<string> set = ["a", "b", "a"];
IEnumerable<int> seq = [10, 20];
IReadOnlyList<string> names = ["x", "y", "z"];
IList<double> doubles = [1.5];
Console.WriteLine(string.Join(",", all) + " " + all.Count);
Console.WriteLine(set.Count + " " + set.Contains("b"));
Console.WriteLine(seq.Sum() + " " + names[2] + " " + names.Count + " " + doubles[0]);
List<int> empty = [];
Console.WriteLine(empty.Count);
