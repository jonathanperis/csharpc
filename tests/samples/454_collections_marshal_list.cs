// CollectionsMarshal.AsSpan / SetCount over List<T>: the span aliases the list's array, and SetCount
// exposes whatever the array holds — .NET clears removed slots only when T holds references, so a
// list of ints shows its old values again and a list of strings shows nulls.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

var numbers = new List<int> { 1, 2, 3, 4 };
var span = CollectionsMarshal.AsSpan(numbers);
span[0] = 10;
Console.WriteLine($"{span.Length} {string.Join(",", numbers)}");

CollectionsMarshal.SetCount(numbers, 2);
Console.WriteLine($"{numbers.Count} {numbers.Capacity} {string.Join(",", numbers)}");
CollectionsMarshal.SetCount(numbers, 4);
Console.WriteLine(string.Join(",", numbers));
numbers.RemoveAt(3);
numbers.RemoveRange(1, 2);
CollectionsMarshal.SetCount(numbers, 4);
Console.WriteLine(string.Join(",", numbers));
numbers.Clear();
CollectionsMarshal.SetCount(numbers, 3);
Console.WriteLine(string.Join(",", numbers));
CollectionsMarshal.SetCount(numbers, 9);
Console.WriteLine($"{numbers.Count} {numbers.Capacity} {string.Join(",", numbers)}");

var words = new List<string> { "a", "b", "c" };
CollectionsMarshal.SetCount(words, 1);
CollectionsMarshal.SetCount(words, 3);
Console.WriteLine(string.Join(",", words.Select(w => w ?? "null")));
words.RemoveAll(w => w == null);
CollectionsMarshal.SetCount(words, 2);
Console.WriteLine(string.Join(",", words.Select(w => w ?? "null")));

var points = new List<Point> { new(1, 2), new(3, 4) };
points.Clear();
CollectionsMarshal.SetCount(points, 2);
Console.WriteLine(string.Join(",", points));
var named = new List<Named> { new("x", 1), new("y", 2) };
named.Clear();
CollectionsMarshal.SetCount(named, 2);
Console.WriteLine(string.Join(",", named.Select(n => n.Name ?? "null")));

Console.WriteLine(CollectionsMarshal.AsSpan<int>(null).Length);
try { CollectionsMarshal.SetCount(numbers, -1); }
catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }

Console.WriteLine($"{RuntimeHelpers.IsReferenceOrContainsReferences<int>()} {RuntimeHelpers.IsReferenceOrContainsReferences<string>()} " +
    $"{RuntimeHelpers.IsReferenceOrContainsReferences<Point>()} {RuntimeHelpers.IsReferenceOrContainsReferences<Named>()} " +
    $"{RuntimeHelpers.IsReferenceOrContainsReferences<KeyValuePair<int, long>>()} {RuntimeHelpers.IsReferenceOrContainsReferences<KeyValuePair<int, string>>()} " +
    $"{RuntimeHelpers.IsReferenceOrContainsReferences<DateTime>()} {RuntimeHelpers.IsReferenceOrContainsReferences<CancellationToken>()} " +
    $"{RuntimeHelpers.IsReferenceOrContainsReferences<(int, double)>()} {RuntimeHelpers.IsReferenceOrContainsReferences<int?>()}");

readonly record struct Point(int X, int Y);
readonly record struct Named(string Name, int Value);
