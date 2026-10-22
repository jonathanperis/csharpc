int[] arr = { 1, 2, 3, 4, 5 };
Span<int> s = arr.AsSpan(1, 3);
s[0] = 20;
s.Slice(1).Fill(9);
Console.WriteLine(string.Join(",", arr));
ReadOnlySpan<char> text = "hello world".AsSpan();
Console.WriteLine(text.Slice(6).ToString() + " " + text.IndexOf('o') + " " + text[..5].ToString());
