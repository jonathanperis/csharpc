var p = new Printer("x");
Func<int, string> f = p.Format;
Func<string, int> parse = int.Parse;
Action<string> w = Console.WriteLine;
w(f(parse("41") + 1));
Func<object, string> toStr = Convert.ToString!;
w(toStr(3.5)!);
class Printer(string tag) { public string Format(int v) => $"{tag}{v}"; }
