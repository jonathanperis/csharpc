Func<int, int> sq = static x => x * x;
Console.WriteLine(sq(9));
Func<int, long>? fact = null;
fact = n => n <= 1 ? 1 : n * fact!(n - 1);
Console.WriteLine(fact(15));
