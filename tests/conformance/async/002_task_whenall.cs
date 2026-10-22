var results = await Task.WhenAll(Square(2), Square(3), Square(4));
Console.WriteLine(string.Join(",", results));
static async Task<int> Square(int x) { await Task.Delay(1); return x * x; }
