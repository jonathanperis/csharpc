Span<int> s = stackalloc int[4];
for (int i = 0; i < s.Length; i++) s[i] = i + 1;
int sum = 0;
foreach (var x in s) sum += x;
Console.WriteLine(sum);
Span<int> slice = s.Slice(1, 2);
Console.WriteLine(slice[0] + slice[1]);
