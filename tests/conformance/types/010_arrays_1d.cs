int[] a = new int[5];
for (int i = 0; i < a.Length; i++) a[i] = i * i;
Console.WriteLine(string.Join(",", a));
int[] b = { 3, 1, 2 };
Array.Sort(b);
Console.WriteLine(string.Join(",", b));
Console.WriteLine(Array.IndexOf(b, 2));
