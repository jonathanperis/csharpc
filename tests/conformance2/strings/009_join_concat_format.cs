Console.WriteLine(string.Join(", ", new object?[] { 1, null, "x", 2.5 }));
Console.WriteLine(string.Join("-", new List<int> { 1, 2, 3 }));
Console.WriteLine(string.Concat("a", 1, 'c', null, true));
Console.WriteLine(string.Format("{0}-{1,4}-{2:X2}-{0}", "r", 7, 255));
