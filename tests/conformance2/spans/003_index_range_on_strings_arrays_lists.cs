var a = new[] { 0, 1, 2, 3, 4, 5 };
Console.WriteLine($"{a[^1]} {a[^2]} {string.Join(",", a[1..3])} {string.Join(",", a[^2..])} {string.Join(",", a[..^4])}");
var s = "abcdef";
Console.WriteLine($"{s[^1]} {s[2..4]} {s[..]}");
var l = new List<int> { 7, 8, 9 };
Console.WriteLine(l[^1]);
Range r = 1..^1;
Console.WriteLine(string.Join(",", a[r]));
