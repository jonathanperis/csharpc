// Misc BCL: CompareTo, culture string ordering, Convert, Environment, StringBuilder, Join/Split.
using System.Text;

Console.WriteLine($"{5.CompareTo(3)} {3.CompareTo(5)} {7L.CompareTo(7L)} {'a'.CompareTo('b')} {2.5.CompareTo(double.NaN)} {double.NaN.CompareTo(double.NaN)} {true.CompareTo(false)}");
Console.WriteLine($"{"apple".CompareTo("Banana")} {"a".CompareTo("A")} {"abc".CompareTo("abd")} {string.Compare("x", "X", true)} {string.Compare("b", "B", StringComparison.Ordinal)}");
Console.WriteLine(string.Equals("Hello", "hello", StringComparison.OrdinalIgnoreCase) + " " + "Hi".Equals("HI", StringComparison.Ordinal));
string[] names = ["delta", "Alpha", "alpha", "charlie", "Bravo", "_under", "123", "a-b", "ab", "a b", "Zulu", "zebra", "éclair", "eclair", "Eclair", "!bang", "~tilde", "10", "9"];
Array.Sort(names);
Console.WriteLine(string.Join(" ", names));
Console.WriteLine(double.IsNaN(0.0 / 0.0) + " " + double.IsInfinity(1 / 0.0) + " " + double.IsFinite(1.5) + " " + 1.5.Equals(1.5) + " " + double.NaN.Equals(double.NaN));

Console.WriteLine(Convert.ToString(255, 2) + " " + Convert.ToString(-1, 16) + " " + Convert.ToInt32("ff", 16) + " " + Convert.ToInt32("-42") + " " + Convert.ToInt64("1010", 2));
Console.WriteLine(Convert.ToInt32(2.5) + " " + Convert.ToInt32(3.5) + " " + Convert.ToInt32(-2.5) + " " + Convert.ToInt32(true) + " " + Convert.ToInt32('A') + " " + Convert.ToDouble("1.25") + " " + Convert.ToChar(66));
Console.WriteLine(bool.Parse("True") + " " + bool.Parse(" false ") + " " + char.GetNumericValue('7') + " " + char.GetNumericValue('x'));
Console.WriteLine(double.Parse("2.5", System.Globalization.CultureInfo.InvariantCulture) * 2);
Console.WriteLine(Environment.ProcessorCount > 0);
Console.WriteLine(Environment.TickCount64 > 0 && Environment.TickCount != 0);
Console.WriteLine(Environment.GetCommandLineArgs().Length >= 1);
Console.WriteLine(Environment.NewLine.Length + " " + Environment.Is64BitProcess + " " + string.Empty.Length + " " + BitConverter.IsLittleEndian);

var sb = new StringBuilder("hello world");
sb.Insert(0, "[").Append(']').Replace("world", "there").Replace('l', 'L');
Console.WriteLine(sb + " " + sb.Length + " " + sb[1]);
sb.Remove(1, 6);
Console.WriteLine(sb.ToString());
sb.Insert(1, 42).Insert(0, '>');
Console.WriteLine(sb);
string[] letters = ["a", "b", "c"];
Console.WriteLine(new StringBuilder().AppendJoin("+", letters).ToString());

int[] nums = [3, 1, 2];
double[] ds = [1.5, 2.25];
Console.WriteLine(string.Join(",", nums) + " | " + string.Join("; ", ds) + " | " + string.Join('/', ["x", "y"]) + " | " + string.Concat(new[] { "a", "b", "c" }));
Console.WriteLine(string.Join(", ", new[] { true, false }) + " | " + string.Join("-", new char[] { 'q', 'r' }) + " | " + string.Join(",", new object[] { 1, "two", 3.0 }));
Console.WriteLine(string.Join("|", "a, b,c ,, d".Split(", ")));
Console.WriteLine(string.Join("|", "one two  three".Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)));
Console.WriteLine(string.Join("|", "a;b,c".Split(new[] { ';', ',' })) + " " + "k=v".Split("=").Length);
Console.WriteLine(string.Join("|", "x--y--".Split("--")) + " " + Math.BigMul(100000, 100000));
int q = Math.DivRem(17, 5, out int rem);
Console.WriteLine(q + " " + rem);
Console.WriteLine("{0}-{1}-{2}-{3:F1}", 1, "two", 'c', 4.25);
Console.WriteLine(string.Join(",", 1, "b", 2.5) + " " + string.Join(" ", "x", "y", "z") + " " + string.Concat("p", "q", "r", "s", "t") + " " + string.Format("{0}{1}{2}{3}", 'a', 'b', 'c', 'd'));
double[] vs = [2.5, 3.5, -2.5, 2.355, 2.345, 1.005, 2.675, -1.125, 0.5, 12345.6785];
foreach (var v in vs)
    Console.WriteLine($"{v}: {Math.Round(v, MidpointRounding.AwayFromZero)} {Math.Round(v, MidpointRounding.ToZero)} {Math.Round(v, 2, MidpointRounding.AwayFromZero)} {Math.Round(v, 2, MidpointRounding.ToEven)} {Math.Round(v, 1, MidpointRounding.ToPositiveInfinity)} {Math.Round(v, 3, MidpointRounding.ToNegativeInfinity)}");
Console.WriteLine(string.IsNullOrWhiteSpace("  \t") + " " + object.ReferenceEquals(names, names) + " " + object.Equals("a", "a") + " " + nint.Size);
