// Library method groups, StringComparer, Index on List, Task.WhenAll(IEnumerable), string helpers,
// AppendJoin, Int128/UInt128, CreateChecked, BigInteger generic math, generic catch, arrays as ICollection.
using System.Collections;
using System.Numerics;
using System.Text;

Console.WriteLine(new string("a1-b2!".Where(char.IsLetterOrDigit).ToArray()) + " " + string.Join(",", new[] { 4.0, 9.0 }.Select(Math.Sqrt)));
Func<string, int> parse = int.Parse;
Console.WriteLine(parse("41") + 1);

var words = new[] { "pear", "Apple", "apple", "Banana" };
Console.WriteLine(string.Join(",", words.OrderBy(w => w, StringComparer.Ordinal)) + " | " + string.Join(",", words.Distinct(StringComparer.OrdinalIgnoreCase)));
var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Key"] = 7 };
Console.WriteLine(lookup["KEY"] + " " + StringComparer.Ordinal.Equals("a", "A"));

var list = new List<int> { 10, 20, 30, 40 };
Console.WriteLine($"{list[^1]} {list[^2]} {string.Join("+", list[1..3])}");
var squares = await Task.WhenAll(list.Select(async x => { await Task.Yield(); return x / 10 * (x / 10); }));
Console.WriteLine(string.Join(",", squares));

Console.WriteLine("hello, world".IndexOfAny(['o', ',']) + " " + "xxabcxx".Trim('x') + " " + "1.500".TrimEnd('0') + " " + "--a--".TrimStart('-'));
Console.WriteLine(string.Join('/', new[] { "a", "b" }) + " " + string.Join('-', new List<int> { 1, 2, 3 }));
Console.WriteLine(new StringBuilder("[").AppendJoin(", ", new[] { 1, 2, 3 }).AppendJoin(';', new List<string> { "x", "y" }).Append(']'));

UInt128 big = ulong.MaxValue;
big *= 3;
Int128 neg = -(Int128)big;
Console.WriteLine($"{big} {neg} {(ulong)(big % 1_000_000_007)} {Int128.MaxValue}");
Console.WriteLine($"{double.CreateChecked(42L)} {int.CreateTruncating(300.9)} {Scale<BigInteger>(5)} {Scale<double>(2.5)}");
try { _ = int.CreateChecked(3_000_000_000L); } catch (OverflowException) { Console.WriteLine("overflow"); }

Console.WriteLine(Catch<FormatException>(() => int.Parse("x")) + " " + Catch<ArgumentException>(() => throw new InvalidOperationException()));
object arr = new[] { "a", "b", "c" };
Console.WriteLine(arr is ICollection c ? c.Count : -1);
ulong huge = 18_446_744_073_709_551_557;
Console.WriteLine($"{huge:N0}");

static T Scale<T>(T x) where T : INumber<T> => T.IsZero(x) ? T.Zero : x * (T.One + T.One);
static string Catch<TException>(Action a) where TException : Exception
{
    try { a(); return "none"; }
    catch (TException e) { return "caught " + e.GetType().Name; }
    catch (Exception e) { return "other " + e.GetType().Name; }
}
