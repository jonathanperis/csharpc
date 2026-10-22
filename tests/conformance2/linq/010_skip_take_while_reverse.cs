var xs = Enumerable.Range(1, 10);
Console.WriteLine(string.Join(",", xs.Skip(2).Take(3)) + " " + string.Join(",", xs.TakeWhile(x => x < 4)) + " " + string.Join(",", xs.SkipWhile(x => x < 8)));
Console.WriteLine(string.Join(",", xs.Reverse().Take(2)) + " " + string.Join(",", xs.TakeLast(2)) + " " + string.Join(",", xs.SkipLast(8)));
