IEnumerable<string> strings = new List<string> { "a", "b" };
IEnumerable<object> objects = strings;
foreach (var o in objects) Console.WriteLine(o);
Func<object, string> f = o => o.ToString()!;
Func<string, object> g = f;
Console.WriteLine(g("z"));
