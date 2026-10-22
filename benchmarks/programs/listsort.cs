// List<int>.Sort (introsort), HashSet<int> and Dictionary<int,int> churn (corelib collections).
var list = new List<int>();
uint seed = 7;
for (int i = 0; i < 2_000_000; i++)
{
    seed = seed * 1664525 + 1013904223;
    list.Add((int)(seed >> 8));
}
list.Sort();
long sum = 0;
for (int i = 0; i < list.Count; i += 1000) sum += list[i];
Console.WriteLine($"sorted: first={list[0]} last={list[list.Count - 1]} sample-sum={sum}");

var set = new HashSet<int>();
var map = new Dictionary<int, int>();
for (int i = 0; i < 1_000_000; i++)
{
    int k = (int)((uint)(i * 2654435761u) % 200_000);
    if (!set.Add(k)) set.Remove(k);
    map[k % 50_000] = map.TryGetValue(k % 50_000, out int v) ? v + k : k;
}
long total = 0;
foreach (var kv in map) total += kv.Value ^ kv.Key;
Console.WriteLine($"set={set.Count} map={map.Count} total={total}");
