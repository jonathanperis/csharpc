// System.Math maps onto LLVM intrinsics and libm.
Console.WriteLine(Math.Sqrt(2));
Console.WriteLine(Math.Abs(-42));
Console.WriteLine(Math.Abs(-2.5));
Console.WriteLine(Math.Max(3, 9));
Console.WriteLine(Math.Min(-1L, 4L));
Console.WriteLine(Math.Max(1.5, double.NaN));
Console.WriteLine(Math.Pow(2, 10));
Console.WriteLine(Math.Floor(-2.5));
Console.WriteLine(Math.Ceiling(-2.5));
Console.WriteLine(Math.Round(2.5));          // banker's rounding
Console.WriteLine(Math.Round(3.5));
Console.WriteLine(Math.Truncate(-7.9));
Console.WriteLine(Math.Sin(Math.PI / 6));
Console.WriteLine(Math.Cos(0));
Console.WriteLine(Math.Atan2(1, 1) * 4);
Console.WriteLine(Math.Exp(1));
Console.WriteLine(Math.Log(Math.E));
Console.WriteLine(Math.Log10(1000));
Console.WriteLine(Math.Clamp(15, 0, 10));
Console.WriteLine(Math.PI);

// Newton's method, by hand
double x = 2;
for (int i = 0; i < 6; i++) x = x - (x * x - 2) / (2 * x);
Console.WriteLine(x == Math.Sqrt(2));
