string? s = "x";
if (s is not null) Console.WriteLine("not null");
object o = 42;
if (o is int n and > 40) Console.WriteLine(n);
if (o is var anything) Console.WriteLine(anything);
