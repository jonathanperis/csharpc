Console.WriteLine(await Get(5));
try { await Fail(); } catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }
static async ValueTask<int> Get(int x) { await Task.Yield(); return x * 2; }
static async Task Fail() { await Task.Yield(); throw new InvalidOperationException("async boom"); }
