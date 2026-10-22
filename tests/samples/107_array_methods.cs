// Generic System.Array helpers from the corelib (ArrayExtras.cs).
int[] a = { 5, 2, 9, 1, 7, 3 };
Array.Sort(a);
Console.WriteLine(string.Join(",", a) + " " + Array.IndexOf(a, 7) + " " + Array.BinarySearch(a, 5));
Array.Reverse(a);
Console.WriteLine(string.Join(",", a));
string[] s = { "pear", "Apple", "fig", "banana" };
Array.Sort(s);
Console.WriteLine(string.Join(",", s));
Array.Sort(s, (x, y) => x.Length - y.Length);
Console.WriteLine(string.Join(",", s));
Array.Resize(ref a, 8);
Console.WriteLine(a.Length + " " + a[7]);
var filled = new char[5]; Array.Fill(filled, '*');
Console.WriteLine(new string(filled) + " " + Array.Exists(a, x => x == 9) + " " + Array.FindIndex(a, x => x < 3) + " " + string.Join(",", Array.FindAll(a, x => x > 4)));
var keys = new[] { 3, 1, 2 }; var vals = new[] { "c", "a", "b" };
Array.Sort(keys, vals);
Console.WriteLine(string.Join(",", keys) + string.Join(",", vals) + Array.Empty<int>().Length);
