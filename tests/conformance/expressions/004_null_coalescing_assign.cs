List<int>? list = null;
list ??= new List<int>();
list.Add(1);
Console.WriteLine(list.Count);
string? name = null;
Console.WriteLine(name ?? "anon");
name ??= "set";
name ??= "ignored";
Console.WriteLine(name);
