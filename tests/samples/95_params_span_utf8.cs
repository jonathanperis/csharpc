// C# 13 params ReadOnlySpan<T> overloads in the BCL, user params spans, and UTF-8 literals.
Console.WriteLine("{0}-{1}-{2}-{3}", 1, "two", 3.5, 'c');
Console.WriteLine(string.Format("{0}{1}{2}{3}{4}", 'a', 'b', 'c', 'd', 'e'));
Console.WriteLine(string.Join(", ", "alpha", "beta", "gamma", "delta"));
Console.WriteLine(string.Join("|", 1, 2, 3, 4, 5));
Console.WriteLine(string.Concat("con", "cat", "en", "ation", "!"));
Console.WriteLine(Sum(1, 2, 3, 4, 5, 6));
Console.WriteLine(Sum());
Console.WriteLine(Longest("a", "abc", "ab"));

ReadOnlySpan<byte> utf8 = "héllo"u8;
Console.WriteLine(utf8.Length);
int total = 0;
foreach (byte b in utf8) total += b;
Console.WriteLine(total);
Console.WriteLine(utf8[1].ToString("X2"));

static int Sum(params ReadOnlySpan<int> values)
{
    int s = 0;
    foreach (var v in values) s += v;
    return s;
}

static string Longest(params ReadOnlySpan<string> words)
{
    string best = "";
    foreach (var w in words) if (w.Length > best.Length) best = w;
    return best;
}
