string a = "hel" + "lo";
string b = new string(new[] { 'h', 'e', 'l', 'l', 'o' });
Console.WriteLine(a == b);
Console.WriteLine(ReferenceEquals(a, "hello"));
Console.WriteLine(a.Equals((object)b));
Console.WriteLine(string.IsNullOrWhiteSpace("  \t"));
