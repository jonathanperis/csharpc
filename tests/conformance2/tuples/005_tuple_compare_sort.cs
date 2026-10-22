var list = new List<(string, int)> { ("b", 2), ("a", 2), ("c", 1) };
list.Sort((p, q) => p.Item2 != q.Item2 ? p.Item2.CompareTo(q.Item2) : string.CompareOrdinal(p.Item1, q.Item1));
Console.WriteLine(string.Join(" ", list));
Console.WriteLine((1, "a").CompareTo((1, "b")) < 0);
