long[] ls = { -1, 0, long.MaxValue, long.MinValue };
foreach (var l in ls) Console.WriteLine($"{l:D} {l:N0} {l:X} {l:E3}");
double[] ds = { -1234.5, 1e21, 1.0 / 3, -0.0 };
foreach (var d in ds) Console.WriteLine($"{d:R} {d:G17} {d:E} {d:F3} {d:N1}");
