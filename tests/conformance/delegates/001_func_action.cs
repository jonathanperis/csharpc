Func<int, int> sq = x => x * x;
Action<string> print = s => Console.WriteLine(s);
print(sq(9).ToString());
Func<int, int, int> add = (a, b) => a + b;
Console.WriteLine(add(2, 3));
Predicate<int> even = n => n % 2 == 0;
Console.WriteLine(even(4));
