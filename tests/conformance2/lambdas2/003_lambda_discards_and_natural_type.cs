var f = (int x, int _) => x * 2;
Console.WriteLine(f(4, 99));
var g = () => "natural";
Console.WriteLine(g());
var h = static (string s) => s.Length;
Console.WriteLine(h("four"));
