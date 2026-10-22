Console.WriteLine(await Twice(21));
Console.WriteLine(Twice(5).Result);
static async ValueTask<int> Twice(int x) { if (x < 10) return x * 2; await Task.Yield(); return x * 2; }
