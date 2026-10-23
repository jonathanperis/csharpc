// Rectangular arrays carry their own type ("System.Int32[,]") and rank at run time: GetType(), ToString(),
// and Rank/GetLength/GetLowerBound through an `Array` reference all match .NET (they used to report
// System.Array, rank 1 and the total length).
using System.Collections;
object a = new int[2, 3];
object b = new int[4];
object s = new string[2, 2, 2];
Console.WriteLine(a.GetType());
Console.WriteLine(a.GetType() == typeof(int[,]));
Console.WriteLine(a.GetType() == typeof(int[]));
Console.WriteLine(b.GetType() == typeof(int[]));
Console.WriteLine(a is int[]);
Console.WriteLine(a is Array);
Console.WriteLine(a.GetType().Name);
Console.WriteLine(a);
Console.WriteLine(s.GetType());
Console.WriteLine(s.GetType() == typeof(string[,,]));
Console.WriteLine(((Array)a).Rank + " " + ((Array)a).Length);
var m = new int[2, 2] { { 1, 2 }, { 3, 4 } };
int sum = 0;
foreach (int x in (IEnumerable)m) sum += x;
Console.WriteLine(sum);
Console.WriteLine(m is IEnumerable);
Console.WriteLine(Gen<double>());

Array arr = new long[3, 4, 5];
Console.WriteLine(arr.Rank + " " + arr.GetLength(0) + " " + arr.GetLength(1) + " " + arr.GetLength(2) + " " + arr.GetUpperBound(2));
Array one = new byte[7];
Console.WriteLine(one.Rank + " " + one.GetLength(0));
try { one.GetLength(1); } catch (IndexOutOfRangeException e) { Console.WriteLine(e.Message); }
try { arr.GetLength(3); } catch (IndexOutOfRangeException e) { Console.WriteLine(e.Message); }
try { new int[3].GetLength(1); } catch (IndexOutOfRangeException e) { Console.WriteLine(e.Message); }
try { arr.GetLowerBound(3); } catch (IndexOutOfRangeException e) { Console.WriteLine("lower: " + e.Message); }
Console.WriteLine(arr.GetLowerBound(1) + " " + one.GetLowerBound(0));
Array? none = null;
try { Console.WriteLine(none!.Rank); } catch (NullReferenceException) { Console.WriteLine("NRE rank"); }
try { Console.WriteLine(none!.GetLength(0)); } catch (NullReferenceException) { Console.WriteLine("NRE length"); }
var jag = new int[2][,];
jag[0] = new int[1, 2];
Console.WriteLine(jag.GetType() + " " + jag[0].GetType());
Console.WriteLine(new int[2, 2].Equals(new int[2, 2]));
static object Gen<T>() => new T[1, 1].GetType();
