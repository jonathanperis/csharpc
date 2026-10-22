int[][] j = { new[] { 1 }, new[] { 2, 3 }, [] };
int[,] m = { { 1, 2, 3 }, { 4, 5, 6 } };
Console.WriteLine($"{j[1][1]} {j[2].Length} {m[1, 2]} {m.GetLength(0)}x{m.GetLength(1)} {m.Length}");
var sum = 0; foreach (var v in m) sum += v;
Console.WriteLine(sum);
