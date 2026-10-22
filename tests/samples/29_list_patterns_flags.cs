// List patterns on arrays/strings/spans, and [Flags] enum formatting.
int[][] inputs = [[], [1], [1, 2], [1, 2, 3], [7, 8, 9, 10], [5, 0, 5]];
foreach (var a in inputs) Console.WriteLine(Describe(a));
Console.WriteLine(Classify("hi") + " " + Classify("a") + " " + Classify("abc!"));
ReadOnlySpan<int> span = [3, 1, 4, 1, 5];
Console.WriteLine(span is [3, .., 5] ? "span ok" : "span no");
Console.WriteLine(new[] { "x", "y" } is [var first, var second] ? first + second : "?");

var p = Perm.Read | Perm.Write;
Console.WriteLine(p);
Console.WriteLine(Perm.All);
Console.WriteLine(Perm.None);
Console.WriteLine(Perm.Read | Perm.Execute);
Console.WriteLine((Perm)64);
Console.WriteLine((Perm)(1 | 64));
Console.WriteLine($"{Perm.Write | Perm.Execute} {(p & Perm.Write) != 0}");

static string Describe(int[] a) => a switch
{
    [] => "empty",
    [var only] => $"one: {only}",
    [1, 2] => "exactly 1,2",
    [var head, .., var tail] when head == tail => $"bookends {head}",
    [_, _, ..] and { Length: > 3 } => $"long ({a.Length}) ending {a[^1]}",
    [var h, ..] => $"starts with {h}",
};

static string Classify(string s) => s switch
{
    [_, _] => "two-char",
    [var c] => $"single '{c}'",
    [.., '!'] => "exclaims",
    _ => "other",
};

[Flags]
enum Perm { None = 0, Read = 1, Write = 2, Execute = 4, ReadWrite = Read | Write, All = Read | Write | Execute }
