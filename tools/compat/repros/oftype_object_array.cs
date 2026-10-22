// Arrays don't implement the non-generic System.Collections.IEnumerable at run time in csharpc:
// `is IEnumerable` is silently False (wrong output) and a cast throws InvalidCastException, which
// breaks Enumerable.OfType<T>/Cast<T> on arrays.
// .NET: True True True 1 a 1 7      csharpc: False False False, then unhandled InvalidCastException
// Found in dotnet/samples core/linq/csharp/conversion (OfType-Sample-1.cs).
using System;
using System.Collections;
using System.Linq;

object o = new object[] { 1, "a" };
Console.WriteLine(o is IEnumerable);
object s = new string[] { "b" };
Console.WriteLine(s is IEnumerable);
object i = new int[] { 3 };
Console.WriteLine(i is IEnumerable);
foreach (var x in (IEnumerable)o) Console.WriteLine(x);

object[] numbers = { null, 1.0, "two", 3, "four", 5, "six", 7.0 };
foreach (var d in numbers.OfType<double>()) Console.WriteLine(d);
