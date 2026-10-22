await foreach (var x in Count(3)) Console.WriteLine(x);
static async IAsyncEnumerable<int> Count(int n) { for (int i = 0; i < n; i++) { await Task.Yield(); yield return i * i; } }
