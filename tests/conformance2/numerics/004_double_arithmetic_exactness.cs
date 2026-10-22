double sum = 0;
for (int i = 0; i < 10; i++) sum += 0.1;
Console.WriteLine($"{sum} {sum == 1.0} {Math.Abs(sum - 1.0) < 1e-9}");
Console.WriteLine($"{1e308 * 10} {-1e308 * 10} {5e-324 / 2} {0.1f + 0.2f}");
Console.WriteLine($"{(float)0.1 == 0.1} {(double)(float)0.1}");
