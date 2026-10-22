Func<object> fo = () => "str";
Func<string> fs = () => "s2";
fo = fs;
Console.WriteLine(fo());
Action<object> ao = o => Console.WriteLine("got " + o);
Action<string> astr = ao;
astr("x");
IEnumerable<object> objs = new List<string> { "a", "b" };
Console.WriteLine(objs.Count());
