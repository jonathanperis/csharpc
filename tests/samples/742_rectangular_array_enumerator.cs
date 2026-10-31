// Rectangular arrays through the non-generic IEnumerable (System.Array's enumerator): row-major order,
// boxed elements read from the live array, the enumerator's state errors, Reset, LINQ's Cast/OfType, and
// the interfaces they implement (System.Array's non-generic ones, no IEnumerable<T>).
using System;
using System.Collections;
using System.Linq;

IEnumerable grid = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
foreach (object o in grid) Console.Write(o + " ");
Console.WriteLine();

var names = new string[2, 2] { { "a", null }, { "c", "d" } };
foreach (var o in (IEnumerable)names) Console.Write((o ?? "null") + ",");
Console.WriteLine();

object cube = new double[2, 1, 2] { { { 0.5, 1.5 } }, { { 2.5, 3.5 } } };
double sum = 0;
foreach (double d in (IEnumerable)cube) sum += d;
Console.WriteLine(sum);

var points = new Point[1, 2] { { new(1, 2), new(3, 4) } };
foreach (Point p in (IEnumerable)points) Console.Write(p + ";");
Console.WriteLine();

// The enumerator reads the array as it is now.
var live = new int[2, 2];
var e = ((IEnumerable)live).GetEnumerator();
try { Console.WriteLine(e.Current); } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
e.MoveNext();
live[0, 0] = 7;
live[1, 1] = 9;
Console.Write(e.Current + " ");
while (e.MoveNext()) Console.Write(e.Current + " ");
Console.WriteLine();
try { Console.WriteLine(e.Current); } catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
Console.WriteLine(e.MoveNext());
e.Reset();
Console.WriteLine(e.MoveNext() + " " + e.Current);
Console.WriteLine(e is ICloneable);

Console.WriteLine(string.Join(",", ((IEnumerable)new long[2, 2] { { 1, 2 }, { 3, 4 } }).Cast<long>().Select(x => x * 10)));
Console.WriteLine(new int[0, 4].Cast<int>().Count() + " " + new byte[3, 3].OfType<byte>().Count());

object md = new long[2, 2];
Console.WriteLine((md is System.Collections.Generic.IEnumerable<long>) + " " + (md is System.Collections.Generic.IList<long>) + " " + (md is IList) + " " + (md is ICollection) + " " + (md is IEnumerable));
Console.WriteLine(((ICollection)md).Count + " " + (md as System.Collections.Generic.IEnumerable<object> == null));

record struct Point(int X, int Y);
