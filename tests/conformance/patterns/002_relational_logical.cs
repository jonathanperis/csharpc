foreach (var t in new[] { -5, 0, 15, 25, 40 })
    Console.WriteLine(t switch { < 0 => "cold", 0 => "zero", > 0 and <= 20 => "mild", not > 30 => "warm", _ => "hot" });
Console.WriteLine(7 is > 5 or < 0);
