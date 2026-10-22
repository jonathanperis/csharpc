// Every out-of-range access must throw — never read or write out of bounds.
int[] a = [1, 2, 3];
int[][] jagged = [[1], null!];
int[,] grid = new int[2, 3];
string s = "abc";
List<int> list = [1, 2];
int minusOne = -1, big = int.MaxValue, three = 3;
long longIndex = 1L << 40;

Try("a[-1]", () => a[minusOne]);
Try("a[3]", () => a[three]);
Try("a[int.MaxValue]", () => a[big]);
Try("a[1L<<40]", () => a[longIndex]);
Try("a[^0]", () => a[^0]);
Try("a[^4]", () => a[^(three + 1)]);
Try("a[1..5]", () => a[1..(three + 2)].Length);
Try("a[2..1]", () => a[2..1].Length);
Try("write a[3]", () => { a[three] = 9; return 0; });
Try("grid[2,0]", () => grid[2, 0]);
Try("grid[0,3]", () => grid[0, three]);
Try("grid[-1,0]", () => grid[minusOne, 0]);
Try("jagged[1][0]", () => jagged[1][0]);
Try("s[3]", () => s[three]);
Try("s[-1]", () => s[minusOne]);
Try("s[^0]", () => s[^0]);
Try("s[1..9]", () => s[1..9].Length);
Try("s.Substring(-1)", () => s.Substring(minusOne).Length);
Try("s.Substring(1, 5)", () => s.Substring(1, 5).Length);
Try("list[2]", () => list[2]);
Try("list[-1]", () => list[minusOne]);
Try("Array.Copy past end", () => { Array.Copy(a, 0, new int[2], 0, 3); return 0; });
Console.WriteLine(string.Join(",", a));

static void Try(string what, Func<int> f)
{
    try { Console.WriteLine($"{what} = {f()}"); }
    catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); }
}
