double n = double.NaN;
Console.WriteLine($"{n < 1} {n > 1} {n == n} {n != n} {n.Equals(n)} {n.CompareTo(1.0)} {(1.0).CompareTo(n)}");
Console.WriteLine(Math.Max(n, 1) + " " + Math.Min(1, n));
Console.WriteLine(double.IsNaN(0.0 / 0.0));
