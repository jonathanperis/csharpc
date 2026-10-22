double? d = null;
Console.WriteLine($"[{d}]");
d = 2.5;
Console.WriteLine(d.Value * 2);
if (d is double v) Console.WriteLine(v);
object o = d;
Console.WriteLine(o);
