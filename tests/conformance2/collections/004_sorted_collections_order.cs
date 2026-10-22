var sd = new SortedDictionary<string, int> { ["pear"] = 1, ["apple"] = 2, ["Zebra"] = 3, ["banana"] = 4 };
Console.WriteLine(string.Join(",", sd.Keys));
var ss = new SortedSet<int> { 5, 1, 9, 1, 3 };
Console.WriteLine(string.Join(",", ss) + " min=" + ss.Min + " max=" + ss.Max);
var sl = new SortedList<int, string> { [3] = "c", [1] = "a" };
Console.WriteLine(sl.Keys[0] + sl.Values[1]);
