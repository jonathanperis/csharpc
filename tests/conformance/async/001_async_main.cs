await Task.Delay(1);
Console.WriteLine(await Compute(20));
static async Task<int> Compute(int x) { await Task.Yield(); return x + 1; }
