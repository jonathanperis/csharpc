int[] a = { 0, 1, 2, 3, 4, 5 };
Console.WriteLine(a[^1]);
Console.WriteLine(string.Join(",", a[1..3]));
Console.WriteLine(string.Join(",", a[^2..]));
Console.WriteLine("hello world"[6..]);
Range r = ..2;
Console.WriteLine(a[r].Length);
