var a = new[] { 5, 2, 8, 2 };
Array.Sort(a); Console.WriteLine(string.Join(",", a) + " " + Array.IndexOf(a, 2) + " " + Array.BinarySearch(a, 8));
var b = new int[6]; Array.Copy(a, 0, b, 1, 4); Console.WriteLine(string.Join(",", b));
Array.Reverse(b); Array.Fill(b, 7, 0, 2); Console.WriteLine(string.Join(",", b));
Array.Resize(ref a, 2); Console.WriteLine(a.Length + " " + a[1]);
var s = new[] { "b", "A", "a", "B" }; Array.Sort(s, StringComparer.Ordinal); Console.WriteLine(string.Join(",", s));
