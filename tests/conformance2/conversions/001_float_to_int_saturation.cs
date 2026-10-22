double[] ds = { 2.9, -2.9, 1e10, -1e10, double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0 };
foreach (var d in ds) Console.WriteLine($"{(int)d} {(long)d} {(uint)d} {(byte)d}");
