Console.WriteLine(Math.Sqrt(16) + " " + Math.Pow(2, 8) + " " + Math.Abs(-3));
Console.WriteLine(Math.Round(2.5) + " " + Math.Round(2.5, MidpointRounding.AwayFromZero));
Console.WriteLine(Math.Floor(-1.5) + " " + Math.Ceiling(-1.5) + " " + Math.Max(3, 4));
Console.WriteLine(Math.Sign(-7) + " " + Math.Clamp(15, 0, 10) + " " + Math.DivRem(17, 5, out int r) + " " + r);
Console.WriteLine(Math.Log2(1024) + " " + double.Round(1.23456, 2));
