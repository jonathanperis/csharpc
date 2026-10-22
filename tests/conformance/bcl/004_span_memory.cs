int[] arr = { 1, 2, 3, 4, 5 };
Span<int> s = arr.AsSpan(1, 3);
s[0] = 20;
Console.WriteLine(arr[1]);
ReadOnlySpan<char> text = "hello world".AsSpan();
Console.WriteLine(text.IndexOf('w'));
Console.WriteLine(text[..5].ToString());
Memory<int> m = arr;
Console.WriteLine(m.Slice(3).Span[0]);
