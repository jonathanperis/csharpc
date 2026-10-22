// System.Index / System.Range on arrays, spans and strings.
int[] data = [10, 20, 30, 40, 50];
Console.WriteLine(data[^1] + " " + data[^5]);
int[] middle = data[1..^1];
Console.WriteLine(string.Join(",", middle) + " " + middle.Length);
Console.WriteLine(string.Join(",", data[..2]) + " | " + string.Join(",", data[3..]));
data[^2] = 99;
Console.WriteLine(data[3]);

Span<int> span = data;
Console.WriteLine(span[^1]);
span[^1] = 7;
Console.WriteLine(data[4]);
Span<int> tail = span[2..];
Console.WriteLine(tail.Length + " " + tail[0]);

string word = "abcdefgh";
Console.WriteLine(word[^1]);
Console.WriteLine(word[2..5]);
Console.WriteLine(word[..^3]);
Console.WriteLine(word[^4..]);

Index fromEnd = ^3;
Range window = 1..4;
Console.WriteLine($"{fromEnd.Value} {fromEnd.IsFromEnd} {fromEnd.GetOffset(10)} {window.Start.Value} {window.End.Value}");
Console.WriteLine(string.Join(",", data[window]));
int n = 2;
Console.WriteLine(word[n..^n]);
