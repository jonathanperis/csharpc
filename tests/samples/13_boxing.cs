// Boxing: primitives become { mt, payload } heap objects with value-formatting vtables.
object o = 42;
object d = 2.5;
Console.WriteLine(o);
Console.WriteLine(o.ToString() + d);
int back = (int)o;
Console.WriteLine(back + 1);
Console.WriteLine(o is int);
Console.WriteLine(o is double);
if (d is double dd) Console.WriteLine(dd * 2);
object[] things = [1, "two", 3.0, 'c', true, null];
foreach (var t in things) Console.WriteLine(t);
Console.WriteLine(o.Equals(42));
long bad = (long)o;
