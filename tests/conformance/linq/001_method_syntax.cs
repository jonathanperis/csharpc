var nums = Enumerable.Range(1, 10);
Console.WriteLine(string.Join(",", nums.Where(n => n % 2 == 0).Select(n => n * n)));
Console.WriteLine(nums.Sum() + " " + nums.Max() + " " + nums.Average());
Console.WriteLine(nums.Any(n => n > 9) + " " + nums.All(n => n > 0) + " " + nums.Count(n => n > 5));
Console.WriteLine(nums.First() + " " + nums.Last() + " " + nums.Skip(8).First());
