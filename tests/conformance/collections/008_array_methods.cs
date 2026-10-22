int[] a = { 5, 2, 9, 1 };
Array.Reverse(a);
Console.WriteLine(string.Join(",", a));
Console.WriteLine(Array.Exists(a, x => x > 8));
int[] copy = new int[4];
Array.Copy(a, copy, 4);
Array.Fill(a, 0);
Console.WriteLine(copy[0] + " " + a[0]);
Console.WriteLine(Array.BinarySearch(new[] { 1, 3, 5, 7 }, 5));
