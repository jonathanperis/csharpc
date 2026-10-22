var l = new List<int> { 5, 3, 8 };
l.Add(1); l.Insert(0, 9); l.Remove(3); l.RemoveAt(1);
Console.WriteLine(string.Join(",", l) + " count=" + l.Count);
l.Sort();
Console.WriteLine(string.Join(",", l) + " " + l.Contains(8) + " " + l.IndexOf(8));
l.Reverse();
Console.WriteLine(l[0]);
l.Sort((a, b) => b.CompareTo(a));
Console.WriteLine(string.Join(",", l));
