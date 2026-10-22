var s = "Hello, World";
Console.WriteLine($"{s.Contains("World")} {s.Contains("world")} {s.Contains("world", StringComparison.OrdinalIgnoreCase)} {s.StartsWith('H')} {s.EndsWith("ld")} {s.StartsWith("")}");
Console.WriteLine($"{s.LastIndexOf('o')} {s.IndexOfAny(new[] { ',', 'W' })} {s.LastIndexOf("o", StringComparison.Ordinal)}");
