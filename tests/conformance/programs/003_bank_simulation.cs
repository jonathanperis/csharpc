var accounts = new Dictionary<string, decimal> { ["alice"] = 100m, ["bob"] = 50m };
Transfer(accounts, "alice", "bob", 30m);
try { Transfer(accounts, "bob", "alice", 500m); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
foreach (var (name, bal) in accounts.OrderBy(kv => kv.Key)) Console.WriteLine($"{name}: {bal:F2}");
static void Transfer(Dictionary<string, decimal> acc, string from, string to, decimal amt)
{
    if (acc[from] < amt) throw new InvalidOperationException($"insufficient funds in {from}");
    acc[from] -= amt; acc[to] += amt;
}
