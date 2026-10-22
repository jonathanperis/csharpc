static string M(string? s) => s switch { null => "null", "" => "empty", "a" or "b" => "ab", { Length: > 3 } => "long", _ => "other" };
foreach (var s in new[] { null, "", "a", "b", "abcd", "xy" }) Console.WriteLine(M(s));
