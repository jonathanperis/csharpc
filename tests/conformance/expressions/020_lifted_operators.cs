int? a = 3, b = null;
Console.WriteLine(a * 2);
Console.WriteLine((a * b).HasValue);
Console.WriteLine(a < b);
Console.WriteLine(a == b);
bool? t = true, n = null;
Console.WriteLine(t & n);
Console.WriteLine((t | n) == true);
