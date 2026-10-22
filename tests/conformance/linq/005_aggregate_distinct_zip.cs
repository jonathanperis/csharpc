int[] xs = { 3, 1, 3, 2, 1 };
Console.WriteLine(string.Join(",", xs.Distinct()));
Console.WriteLine(xs.Aggregate((acc, x) => acc * 10 + x));
Console.WriteLine(string.Join(",", xs.Zip(xs.Skip(1), (a, b) => a + b)));
Console.WriteLine(string.Join(",", xs.Order()));
Console.WriteLine(string.Join(",", xs.Chunk(2).Select(c => c.Sum())));
