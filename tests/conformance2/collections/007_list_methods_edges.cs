var l = new List<int> { 5, 3, 8, 3, 1 };
Console.WriteLine($"{l.IndexOf(3)} {l.LastIndexOf(3)} {l.Remove(3)} {string.Join(",", l)}");
l.Insert(0, 9); l.RemoveAt(l.Count - 1); l.RemoveAll(x => x > 7);
Console.WriteLine(string.Join(",", l));
l.Reverse(); Console.WriteLine(string.Join(",", l) + " " + l.Contains(5) + " " + l.BinarySearch(5));
try { l.RemoveAt(10); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.ParamName); }
