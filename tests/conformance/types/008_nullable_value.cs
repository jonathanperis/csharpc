int? a = 5;
int? b = null;
Console.WriteLine(a.HasValue + " " + b.HasValue);
Console.WriteLine(a + 1);
Console.WriteLine((a + b) == null);
Console.WriteLine(b ?? 42);
Console.WriteLine(a.GetValueOrDefault() + b.GetValueOrDefault());
Console.WriteLine(a > 3);
