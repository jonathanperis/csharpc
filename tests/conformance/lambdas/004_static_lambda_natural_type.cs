var parse = static (string s) => int.Parse(s);
Console.WriteLine(parse("41") + 1);
var withDefault = (int x = 3) => x * 2;
Console.WriteLine(withDefault());
