var v = new Ver(1, 9);
v++;
Console.WriteLine(v);
var w = v++;
Console.WriteLine($"{w} {v}");
++v;
Console.WriteLine(v);
readonly record struct Ver(int Major, int Minor) { public static Ver operator ++(Ver x) => x.Minor == 9 ? new(x.Major + 1, 0) : x with { Minor = x.Minor + 1 }; }
