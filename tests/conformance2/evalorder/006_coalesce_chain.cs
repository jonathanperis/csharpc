static string? N(string tag) { Console.Write(tag); return null; }
static string V(string tag) { Console.Write(tag); return tag; }
Console.WriteLine(" " + (N("a") ?? N("b") ?? V("c") ?? V("d")));
string? x = null;
x ??= V("e");
x ??= V("f");
Console.WriteLine(" " + x);
