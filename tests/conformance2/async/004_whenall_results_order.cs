var tasks = new[] { Delay(30, "a"), Delay(10, "b"), Delay(20, "c") };
var results = await Task.WhenAll(tasks);
Console.WriteLine(string.Join(",", results));
static async Task<string> Delay(int ms, string v) { await Task.Delay(ms); return v; }
