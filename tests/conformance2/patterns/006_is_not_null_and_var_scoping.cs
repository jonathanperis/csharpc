object? o = "text";
if (o is not string s) { Console.WriteLine("not string"); return; }
Console.WriteLine(s.Length);
if (o is var v && v is string { Length: 4 }) Console.WriteLine("var pattern " + v);
int? maybe = 4;
if (maybe is int m and > 3) Console.WriteLine(m);
