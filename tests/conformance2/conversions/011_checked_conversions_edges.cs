static string T(Func<object> f) { try { return f()!.ToString()!; } catch (OverflowException) { return "OVF"; } }
double a = 2147483647.9, b = 2147483648.0, c = -0.9, d = -1.0, e = 255.5, nan = double.NaN;
Console.WriteLine(T(() => checked((int)a)));
Console.WriteLine(T(() => checked((int)b)));
Console.WriteLine(T(() => checked((uint)c)));
Console.WriteLine(T(() => checked((uint)d)));
Console.WriteLine(T(() => checked((byte)e)));
Console.WriteLine(T(() => checked((long)nan)));
