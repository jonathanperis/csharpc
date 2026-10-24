static async Task<int> Work(int x) { await Task.Delay(1); return x * 2; }
var results = await Task.WhenAll(Work(1), Work(2), Work(3));
Console.WriteLine(results.Sum());
