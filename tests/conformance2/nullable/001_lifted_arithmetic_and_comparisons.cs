int? a = 5, n = null;
Console.WriteLine($"[{a + n}] [{a * 2}] [{-n}] [{a < n}] [{a > n}] [{n == null}] [{n <= n}] [{n == n}]");
int? z = 0;
Console.WriteLine($"{(a ?? 0) + (n ?? 10)} {z.GetValueOrDefault(7)} {n.GetValueOrDefault(7)}");
