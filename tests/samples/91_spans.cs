// Span<T> / ReadOnlySpan<T>: views over arrays, strings and stack memory.
int[] numbers = [5, 3, 8, 1, 9, 2];
Span<int> all = numbers;
all[0] = 50;
all[1] += 7;
Console.WriteLine(numbers[0] + " " + numbers[1]);

Span<int> middle = all.Slice(1, 3);
middle[0] = -1;
Console.WriteLine($"{middle.Length} {middle[0]} {numbers[1]} {all.Slice(4).Length}");

int sum = 0;
foreach (var n in all) sum += n;
Console.WriteLine(sum);
foreach (ref int n in all) n *= 2;
Console.WriteLine(string.Join(",", numbers));

var window = numbers.AsSpan(2, 3);
Console.WriteLine(window.ToArray().Length + " " + window[2]);
Console.WriteLine(numbers.AsSpan().IsEmpty + " " + Span<int>.Empty.IsEmpty);

Span<int> copy = new int[6];
all.CopyTo(copy);
copy.Fill(7);
Console.WriteLine(copy[5] + " " + numbers[5]);
copy.Slice(0, 2).Clear();
Console.WriteLine(copy[0] + copy[1] + copy[2]);
Console.WriteLine(all.TryCopyTo(new int[2]));

ReadOnlySpan<char> text = "  Hello, spans!  ";
ReadOnlySpan<char> trimmed = text.Trim();
Console.WriteLine($"[{trimmed.ToString()}] {trimmed.Length}");
Console.WriteLine(trimmed.IndexOf(',') + " " + trimmed.Contains('!') + " " + trimmed.StartsWith("Hello") + " " + trimmed.EndsWith("x"));
Console.WriteLine(trimmed.Slice(7).ToString());
Console.WriteLine(new string(trimmed.Slice(0, 5)));
Console.WriteLine("abcdef".AsSpan(1, 3).ToString());
ReadOnlySpan<int> ro = numbers;
Console.WriteLine(ro.SequenceEqual(all) + " " + ro.IndexOf(4) + " " + ro.LastIndexOf(18));

Span<int> fromList = [10, 20, 30];
fromList.Reverse();
Console.WriteLine(fromList[0] + " " + Sum(fromList) + " " + Sum([1, 2, 3, 4]));
Console.WriteLine(all.ToString());
Console.WriteLine(CountChar("mississippi", 's'));

static int Sum(ReadOnlySpan<int> values)
{
    int total = 0;
    for (int i = 0; i < values.Length; i++) total += values[i];
    return total;
}

static int CountChar(ReadOnlySpan<char> s, char c)
{
    int count = 0;
    foreach (char ch in s) if (ch == c) count++;
    return count;
}
