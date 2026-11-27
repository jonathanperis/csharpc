// foreach over an array cast to an enumerable interface in the foreach header (#516): Roslyn disposes the enumerator in a finally,
// which for an array's enumerator does nothing. Invariant, covariant and jagged casts, the non-generic one, break and an empty array.
using System;
using System.Collections;
using System.Collections.Generic;

string[] words = { "a", "b", "c" };
foreach (var w in (IEnumerable<string>)words) Console.Write(w + " ");
Console.WriteLine();
foreach (object o in (IEnumerable<object>)words) Console.Write(o + " ");
Console.WriteLine();
foreach (object o in (IEnumerable)words) Console.Write(o + " ");
Console.WriteLine();

string[][] jagged = { new[] { "x", "y" }, new[] { "z" } };
foreach (IEnumerable<string> row in (IEnumerable<IEnumerable<string>>)jagged) Console.Write(string.Join(",", row) + " ");
Console.WriteLine();

int[] numbers = { 1, 2, 3, 4 };
var sum = 0;
foreach (var n in (IReadOnlyCollection<int>)numbers)
{
    if (n == 3) break;
    sum += n;
}
Console.WriteLine(sum);

foreach (var n in (IEnumerable<int>)Array.Empty<int>()) Console.WriteLine(n);
Console.WriteLine("done");
