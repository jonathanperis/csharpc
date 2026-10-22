ReadOnlySpan<int> a = new[] { 1, 2, 3, 2 };
Console.WriteLine($"{a.IndexOf(2)} {a.LastIndexOf(2)} {a.Contains(3)} {a.SequenceEqual(new[] { 1, 2, 3, 2 })} {a.StartsWith(new[] { 1, 2 })}");
Span<int> b = new int[4];
a.CopyTo(b);
b.Reverse();
Console.WriteLine(string.Join(",", b.ToArray()));
