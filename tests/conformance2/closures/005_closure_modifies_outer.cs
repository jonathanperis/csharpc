int count = 0;
Action inc = () => count++;
inc(); inc(); inc();
Console.WriteLine(count);
var adders = Enumerable.Range(1, 3).Select(n => (Func<int, int>)(x => x + n * count)).ToList();
count = 10;
Console.WriteLine(string.Join(" ", adders.Select(f => f(1))));
