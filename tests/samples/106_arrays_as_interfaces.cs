// T[] and string as IEnumerable<T>/IList<T>/IReadOnlyList<T> (corelib SZArrayHelper / StringInterfaces).
int[] arr = { 4, 8, 15, 16, 23, 42 };
IList<int> list = arr;
Console.WriteLine(list.Count + " " + list[2] + " " + list.IndexOf(23) + " " + list.Contains(7));
list[0] = 5;
Console.WriteLine(arr[0]);
IReadOnlyList<int> ro = arr;
Console.WriteLine(ro[ro.Count - 1]);
IEnumerable<int> seq = arr;
int total = 0;
foreach (var x in seq) total += x;
Console.WriteLine(total + " " + Sum(arr) + " " + Sum(new List<int>(arr)));
string word = "mississippi";
IEnumerable<char> chars = word;
Console.WriteLine(string.Join("", chars.Distinct()) + " " + chars.Count(c => c == 's'));
var copy = new List<int>(arr);
copy.Add(99);
Console.WriteLine(string.Join(",", copy));
object boxed = arr;
Console.WriteLine(boxed is IEnumerable<int>);
string[] names = { "x", "y" };
IEnumerable<object> covariant = names;
foreach (var o in covariant) Console.Write(o);
Console.WriteLine();

static int Sum(IEnumerable<int> values)
{
    int s = 0;
    foreach (var v in values) s += v;
    return s;
}
