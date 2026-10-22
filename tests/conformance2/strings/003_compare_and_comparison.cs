Console.WriteLine(string.Compare("a", "B") < 0);
Console.WriteLine(string.Compare("a", "B", StringComparison.Ordinal) > 0);
Console.WriteLine(string.Equals("Hello", "hello", StringComparison.OrdinalIgnoreCase));
Console.WriteLine("Hello".Equals("hello", StringComparison.Ordinal));
Console.WriteLine(string.CompareOrdinal("abc", "abd"));
Console.WriteLine("abc".IndexOf("C", StringComparison.OrdinalIgnoreCase));
Console.WriteLine("hello world".IndexOf("o", 5));
