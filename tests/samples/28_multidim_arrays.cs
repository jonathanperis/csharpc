// Rectangular arrays: creation, initializers, indexing with bounds checks, GetLength, foreach.
int[,] grid = new int[3, 4];
for (int r = 0; r < grid.GetLength(0); r++)
    for (int c = 0; c < grid.GetLength(1); c++)
        grid[r, c] = r * 10 + c;
Console.WriteLine($"{grid.Length} {grid.Rank} {grid.GetUpperBound(1)} {grid[2, 3]}");

double[,] identity = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
double[,] m = { { 2, 1, 0 }, { 1, 3, 1 }, { 0, 1, 4 } };
var product = Multiply(m, identity);
for (int i = 0; i < 3; i++)
    Console.WriteLine($"{product[i, 0]} {product[i, 1]} {product[i, 2]}");

int sum = 0;
foreach (int v in grid) sum += v;
Console.WriteLine(sum);

char[,,] cube = new char[2, 2, 2];
cube[1, 0, 1] = 'x';
int filled = 0;
foreach (char ch in cube) if (ch == 'x') filled++;
Console.WriteLine($"{cube.Length} {cube.Rank} {filled}");

string[,] names = { { "a", "b" }, { "c", "d" } };
Console.WriteLine(names[1, 0] + names[0, 1]);
Console.WriteLine(grid[3, 0]);   // IndexOutOfRangeException

static double[,] Multiply(double[,] a, double[,] b)
{
    int n = a.GetLength(0), k = a.GetLength(1), p = b.GetLength(1);
    var result = new double[n, p];
    for (int i = 0; i < n; i++)
        for (int j = 0; j < p; j++)
        {
            double s = 0;
            for (int x = 0; x < k; x++) s += a[i, x] * b[x, j];
            result[i, j] = s;
        }
    return result;
}
