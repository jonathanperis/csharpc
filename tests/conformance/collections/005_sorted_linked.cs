var sd = new SortedDictionary<string, int> { ["c"] = 3, ["a"] = 1, ["b"] = 2 };
Console.WriteLine(string.Join(",", sd.Keys));
var ll = new LinkedList<int>();
ll.AddLast(2); ll.AddFirst(1); ll.AddLast(3);
Console.WriteLine(string.Join(",", ll) + " first=" + ll.First!.Value);
var ss = new SortedSet<int> { 5, 1, 3 };
Console.WriteLine(ss.Min + " " + ss.Max);
