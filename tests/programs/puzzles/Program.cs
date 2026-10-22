// Puzzles: a bitmask sudoku solver with backtracking, Conway's game of life on a torus, N-queens,
// and a word-search; exercises arrays, bit operations, recursion and multi-dimensional arrays.
using System.Numerics;
using System.Text;

const string puzzle =
    "53..7...." + "6..195..." + ".98....6." + "8...6...3" + "4..8.3..1" + "7...2...6" + ".6....28." + "...419..5" + "....8..79";
var sudoku = new Sudoku(puzzle);
Console.WriteLine(sudoku.Solve() ? $"solved in {sudoku.Guesses} guesses:" : "no solution");
Console.WriteLine(sudoku);
Console.WriteLine($"valid: {sudoku.IsValid()}");

const string hard =
    "8........" + "..36....." + ".7..9.2.." + ".5...7..." + "....457.." + "...1...3." + "..1....68" + "..85...1." + ".9....4..";
var hardOne = new Sudoku(hard);
Console.WriteLine($"hard: {(hardOne.Solve() ? "solved" : "failed")}, guesses {hardOne.Guesses}, first row {hardOne.Row(0)}");

var life = new Life(20, 12);
life.Seed([(1, 2), (2, 3), (3, 1), (3, 2), (3, 3)]);                // glider
life.Seed([(10, 5), (10, 6), (10, 7)]);                             // blinker
life.Seed([(15, 2), (16, 2), (15, 3), (16, 3)]);                    // block
for (int gen = 0; gen <= 8; gen += 4)
{
    Console.WriteLine($"generation {life.Generation}, population {life.Population}:");
    Console.WriteLine(life.Render());
    life.Step(4);
}

for (int n = 4; n <= 10; n++) Console.Write($"queens({n})={Queens.Count(n)} ");
Console.WriteLine();
Console.WriteLine("first 8-queens solution: " + string.Join(" ", Queens.First(8)));

string[] grid =
[
    "CSHARPXL",
    "LLVMQZWE",
    "OAYZNORX",
    "PNTKRSIE",
    "SGQUEUET",
    "NULLJIRA",
];
foreach (var word in new[] { "CSHARP", "LLVM", "QUEUE", "LANG", "NULL", "RISE", "MISSING" })
{
    var hit = WordSearch.Find(grid, word);
    Console.WriteLine($"{word,-8} {(hit is var (r, c, dr, dc) ? $"at ({r},{c}) dir ({dr},{dc})" : "not found")}");
}

sealed class Sudoku
{
    private readonly int[] _cells = new int[81];
    private readonly int[] _rows = new int[9], _cols = new int[9], _boxes = new int[9];
    public int Guesses { get; private set; }

    public Sudoku(string text)
    {
        for (int i = 0; i < 81; i++)
            if (text[i] is >= '1' and <= '9') Place(i, text[i] - '0');
    }

    private static int Box(int i) => i / 27 * 3 + i % 9 / 3;
    private void Place(int i, int d) { _cells[i] = d; int bit = 1 << d; _rows[i / 9] |= bit; _cols[i % 9] |= bit; _boxes[Box(i)] |= bit; }
    private void Clear(int i) { int bit = 1 << _cells[i]; _rows[i / 9] &= ~bit; _cols[i % 9] &= ~bit; _boxes[Box(i)] &= ~bit; _cells[i] = 0; }
    private int Candidates(int i) => ~(_rows[i / 9] | _cols[i % 9] | _boxes[Box(i)]) & 0x3FE;

    public bool Solve()
    {
        // Most-constrained cell first.
        int best = -1, bestCount = 10;
        for (int i = 0; i < 81; i++)
        {
            if (_cells[i] != 0) continue;
            int count = BitOperations.PopCount((uint)Candidates(i));
            if (count < bestCount) { best = i; bestCount = count; if (count <= 1) break; }
        }
        if (best < 0) return true;
        for (int mask = Candidates(best); mask != 0; mask &= mask - 1)
        {
            int d = BitOperations.TrailingZeroCount(mask);
            Guesses++;
            Place(best, d);
            if (Solve()) return true;
            Clear(best);
        }
        return false;
    }

    public bool IsValid()
    {
        for (int k = 0; k < 9; k++)
            if (_rows[k] != 0x3FE || _cols[k] != 0x3FE || _boxes[k] != 0x3FE) return false;
        return true;
    }

    public string Row(int r) => string.Concat(_cells.Skip(r * 9).Take(9));

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int r = 0; r < 9; r++)
        {
            if (r > 0 && r % 3 == 0) sb.AppendLine("------+-------+------");
            for (int c = 0; c < 9; c++)
            {
                if (c > 0 && c % 3 == 0) sb.Append("| ");
                sb.Append(_cells[r * 9 + c]).Append(c == 8 ? "" : " ");
            }
            if (r < 8) sb.AppendLine();
        }
        return sb.ToString();
    }
}

sealed class Life(int width, int height)
{
    private bool[,] _cells = new bool[height, width];
    public int Generation { get; private set; }
    public int Population { get { int n = 0; foreach (var alive in _cells) if (alive) n++; return n; } }

    public void Seed(IEnumerable<(int X, int Y)> points) { foreach (var (x, y) in points) _cells[y, x] = true; }

    public void Step(int times)
    {
        for (int t = 0; t < times; t++)
        {
            var next = new bool[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx | dy) != 0 && _cells[(y + dy + height) % height, (x + dx + width) % width]) n++;
                    next[y, x] = n == 3 || (n == 2 && _cells[y, x]);
                }
            _cells = next;
            Generation++;
        }
    }

    public string Render()
    {
        var sb = new StringBuilder();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++) sb.Append(_cells[y, x] ? '#' : '.');
            if (y < height - 1) sb.Append('\n');
        }
        return sb.ToString();
    }
}

static class Queens
{
    public static int Count(int n) => Place(n, 0, 0, 0, 0);

    private static int Place(int n, int row, int cols, int diag1, int diag2)
    {
        if (row == n) return 1;
        int total = 0;
        for (int free = ~(cols | diag1 | diag2) & ((1 << n) - 1); free != 0; free &= free - 1)
        {
            int bit = free & -free;
            total += Place(n, row + 1, cols | bit, (diag1 | bit) << 1, (diag2 | bit) >> 1);
        }
        return total;
    }

    public static int[] First(int n)
    {
        var queens = new int[n];
        bool Go(int row)
        {
            if (row == n) return true;
            for (int c = 0; c < n; c++)
            {
                bool ok = true;
                for (int r = 0; r < row && ok; r++)
                    ok = queens[r] != c && Math.Abs(queens[r] - c) != row - r;
                if (!ok) continue;
                queens[row] = c;
                if (Go(row + 1)) return true;
            }
            return false;
        }
        return Go(0) ? queens : [];
    }
}

static class WordSearch
{
    private static readonly (int, int)[] Directions = [(0, 1), (1, 0), (1, 1), (-1, 1), (0, -1), (-1, 0), (-1, -1), (1, -1)];

    public static (int Row, int Col, int DRow, int DCol)? Find(string[] grid, string word)
    {
        for (int r = 0; r < grid.Length; r++)
            for (int c = 0; c < grid[r].Length; c++)
                foreach (var (dr, dc) in Directions)
                {
                    int k = 0;
                    for (; k < word.Length; k++)
                    {
                        int rr = r + dr * k, cc = c + dc * k;
                        if (rr < 0 || cc < 0 || rr >= grid.Length || cc >= grid[rr].Length || grid[rr][cc] != word[k]) break;
                    }
                    if (k == word.Length) return (r, c, dr, dc);
                }
        return null;
    }
}
