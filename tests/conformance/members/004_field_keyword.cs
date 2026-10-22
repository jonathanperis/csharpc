var p = new P();
p.Name = "  ada  ";
Console.WriteLine($"[{p.Name}]");
class P { public string Name { get; set => field = value.Trim(); } = ""; }
