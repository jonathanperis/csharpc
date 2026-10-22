// Results of every shape flow through tasks: primitives, strings, structs, objects, ValueTask.
Console.WriteLine(await GetBool());
Console.WriteLine(await GetDouble());
Console.WriteLine(await GetString());
var p = await GetPoint();
Console.WriteLine($"({p.X}, {p.Y})");
var box = await GetBox();
Console.WriteLine(box.Value);
Console.WriteLine(await Twice(21));
ValueTask<int> pending = Twice(5);
Console.WriteLine(await pending);
Console.WriteLine(GetString().Result);
Console.WriteLine(GetDouble().GetAwaiter().GetResult());
Console.WriteLine(await Sum(new[] { 1, 2, 3, 4 }));
(int q, int r) = await DivMod(17, 5);
Console.WriteLine($"{q} r {r}");
Console.WriteLine(await Safe());

static async Task<bool> GetBool() { await Task.Yield(); return true; }
static async Task<double> GetDouble() { await Task.Delay(1); return 2.5; }
static async Task<string> GetString() { await Task.Yield(); return "text"; }
static async Task<Point> GetPoint() { await Task.Yield(); return new Point(3, 4); }
static async Task<Box> GetBox() { await Task.Yield(); return new Box { Value = "boxed" }; }
static async ValueTask<int> Twice(int x) { await Task.Yield(); return x * 2; }
static async Task<long> Sum(int[] items)
{
    long total = 0;
    foreach (var item in items) { await Task.Yield(); total += item; }
    return total;
}
static async Task<(int, int)> DivMod(int a, int b) { await Task.Yield(); return (a / b, a % b); }
static async Task<string> Safe()
{
    try { await Task.Delay(1); return "from try"; }
    finally { Console.WriteLine("finally after await"); }
}

struct Point(int x, int y) { public int X = x; public int Y = y; }
class Box { public string Value = ""; }
