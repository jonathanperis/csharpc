// Boxed Memory<T> / ReadOnlyMemory<T>: object conversions, type tests and unboxing, Equals(object)
// (which accepts the other memory type of the same T), and the non-generic comparer and enumerator
// paths that box them (Roslyn's segmented collections keyed by ReadOnlyMemory<char>).
using System.Collections;

var text = "alpha beta";
ReadOnlyMemory<char> alpha = text.AsMemory(0, 5);
object boxed = alpha;
Console.WriteLine($"{boxed} {boxed is ReadOnlyMemory<char>} {boxed is Memory<char>} {boxed is ReadOnlyMemory<int>} {boxed is string}");
Console.WriteLine(((ReadOnlyMemory<char>)boxed).Length);
Console.WriteLine(boxed.Equals(text.AsMemory(0, 5)) + " " + boxed.Equals(text.AsMemory(6)) + " " + boxed.Equals(null) + " " + boxed.Equals("alpha"));
Console.WriteLine(boxed.GetHashCode() == alpha.GetHashCode());
object boxedDefault = default(ReadOnlyMemory<char>);
Console.WriteLine($"[{boxedDefault}] {boxedDefault.GetHashCode()}");

var array = new[] { 1, 2, 3 };
Memory<int> writable = array;
object boxedWritable = writable;
ReadOnlyMemory<int> readOnly = array;
Console.WriteLine($"{boxedWritable} {boxedWritable.Equals(readOnly)} {((object)readOnly).Equals(writable)} {readOnly.Equals(writable)}");
Console.WriteLine(boxedWritable is Memory<int> { Length: 3 } m && m.Span[2] == 3);

var comparer = (IEqualityComparer)EqualityComparer<ReadOnlyMemory<char>>.Default;
Console.WriteLine($"{comparer.Equals(boxed, (object)text.AsMemory(0, 5))} {comparer.Equals(boxed, (object)text.AsMemory(6))} {comparer.GetHashCode(boxed) == alpha.GetHashCode()}");

var words = new List<ReadOnlyMemory<char>> { alpha, text.AsMemory(6) };
IEnumerator untyped = ((IEnumerable)words).GetEnumerator();
while (untyped.MoveNext()) Console.WriteLine(untyped.Current);
var objects = new List<object> { alpha, readOnly, "s" };
Console.WriteLine(objects.IndexOf(text.AsMemory(0, 5)));
Console.WriteLine(string.Join(";", objects));
