// Delegates bound to library methods (method groups): static, instance on library objects, virtual,
// intrinsics and corelib members alike (csharpc's own code passes Path.GetFullPath, set.Contains ...).
using System.Text;

string[] paths = ["/tmp/a/b.txt", "/usr/lib/c.so", "d"];
Console.WriteLine(string.Join(",", paths.Select(Path.GetFileName)));
Console.WriteLine(string.Join(",", paths.Select(Path.GetExtension)));
Console.WriteLine(new string("a1b2c3".Where(char.IsDigit).ToArray()));
Console.WriteLine(string.Join(",", new[] { "x", "", null, "y" }.Where(string.IsNullOrEmpty).Select(s => s ?? "null")));

var set = new HashSet<string> { "x", "z" };
Func<string, bool> inSet = set.Contains;
Console.WriteLine($"{inSet("x")} {inSet("y")} {string.Join(",", new[] { "x", "y", "z" }.Where(set.Contains))}");

var list = new List<int>();
Action<int> add = list.Add;
add(3);
add(4);
Console.WriteLine(string.Join(",", list));

var sb = new StringBuilder();
Func<string, StringBuilder> append = sb.Append;
append("hello ").Append("world");
Console.WriteLine(sb);

Func<int, int, int> max = Math.Max;
Func<string, int> parse = int.Parse;
Func<double, double> sqrt = Math.Sqrt;
Console.WriteLine($"{max(3, 9)} {parse("42") + 1} {sqrt(16)}");

object boxed = 12.5;
Func<string> describe = boxed.ToString;
Console.WriteLine(describe());

Comparison<string> ordinal = string.CompareOrdinal;
var words = new List<string> { "b", "a", "C" };
words.Sort(ordinal);
Console.WriteLine(string.Join(",", words));

Converter<int, string> hex = Convert.ToString;
Console.WriteLine(hex(255));
Console.WriteLine($"{Convert.ToString((object)1.5, System.Globalization.CultureInfo.InvariantCulture)} [{Convert.ToString((object)null)}] {Convert.ToString((object)"s")} {Convert.ToString(true)} {Convert.ToString(new List<int>())}");
try
{
    Func<string, int> nullTarget = ((string)null!).IndexOf;
}
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
