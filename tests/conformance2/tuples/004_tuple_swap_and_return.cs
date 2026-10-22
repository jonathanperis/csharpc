var (min, max) = MinMax(new[] { 4, -2, 9, 0 });
Console.WriteLine($"{min} {max}");
int x = 1, y = 2;
(x, y) = (y, x);
Console.WriteLine($"{x} {y}");
static (int, int) MinMax(int[] xs) => (xs.Min(), xs.Max());
