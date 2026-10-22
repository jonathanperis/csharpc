Func<int, int>? fact = null;
fact = n => n <= 1 ? 1 : n * fact!(n - 1);
Console.WriteLine(fact(10));
