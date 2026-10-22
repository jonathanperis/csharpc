Console.WriteLine($"{Math.Sqrt(-1)} {Math.Log(0)} {Math.Pow(0, 0)} {Math.Pow(-8, 1.0 / 3)} {Math.Abs(-0.0)}");
Console.WriteLine($"{Math.Round(-2.5)} {Math.Round(-3.5)} {Math.Floor(-0.5)} {Math.Ceiling(-0.5)} {Math.Truncate(-1.7)}");
Console.WriteLine($"{Math.Clamp(5, 1, 3)} {Math.Clamp(-5.5, -1.0, 1.0)} {Math.Max(int.MinValue, -1)} {Math.Sign(-0.0)}");
try { Math.Abs(int.MinValue); } catch (OverflowException e) { Console.WriteLine(e.GetType().Name); }
