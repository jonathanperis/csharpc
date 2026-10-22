int i = default; string? s = default; (int, string?) t = default;
Console.WriteLine($"{i} [{s}] {t}");
List<int> l = new() { 1, 2 };
Point p = new(3, 4);
Console.WriteLine($"{l.Count} {p}");
static int M(int x = default) => x;
Console.WriteLine(M());
record Point(int X, int Y);
