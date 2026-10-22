object o = 7;
int i = (int)o;
Console.WriteLine(i + 1);
object[] items = { 1, 2.5, 'c', "s", true };
foreach (var item in items) Console.WriteLine(item);
Console.WriteLine(o is int);
