int calls = 0;
string? s = null;
int Arg() { calls++; return 1; }
var r = s?.Substring(Arg());
Console.WriteLine($"{r ?? "null"} {calls}");
s = "hello";
r = s?.Substring(Arg());
Console.WriteLine($"{r} {calls}");
