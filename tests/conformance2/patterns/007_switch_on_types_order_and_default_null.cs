static int Rank(object? o) => o switch { null => 0, int => 1, long => 2, IFormattable => 3, string => 4, _ => 5 };
foreach (var o in new object?[] { null, 1, 2L, 2.5, "s", new object() }) Console.Write(Rank(o));
Console.WriteLine();
