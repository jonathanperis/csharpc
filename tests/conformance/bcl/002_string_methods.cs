var s = "Hello, World";
Console.WriteLine(s.ToUpper() + s.ToLower());
Console.WriteLine(s.Substring(7) + s.IndexOf("World") + s.Replace("World", "There"));
Console.WriteLine(string.Join("|", s.Split(", ")));
Console.WriteLine(s.StartsWith("Hell") + " " + s.EndsWith("d") + " " + s.Contains("lo, W"));
Console.WriteLine("  x ".Trim().Length + "abc".PadLeft(5, '*'));
Console.WriteLine(string.Equals("A", "a", StringComparison.OrdinalIgnoreCase));
