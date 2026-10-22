Console.WriteLine($"{0.1m + 0.2m} {1m / 3 * 3} {decimal.MaxValue} {decimal.MinValue / 2}");
decimal max = decimal.MaxValue, zero = 0;
try { var x = max + 1; Console.WriteLine(x); } catch (OverflowException e) { Console.WriteLine(e.Message); }
try { var y = 1m / zero; Console.WriteLine(y); } catch (DivideByZeroException e) { Console.WriteLine(e.Message); }
Console.WriteLine($"{1.000m} {1.000m == 1m} {(1.000m).ToString("F1")} {decimal.Round(1.005m, 2)} {decimal.Truncate(-2.7m)}");
