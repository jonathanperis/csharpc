int n = 42; double pi = Math.PI;
Console.WriteLine($"n={n}, pi={pi:F3}, hex={n:X}, pad=[{n,6}] [{n,-6}]");
Console.WriteLine($"{{braces}} {n}");
Console.WriteLine($"{(n > 40 ? "big" : "small")}");
