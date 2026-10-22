// Async fan-out / fan-in: many Task.Run computations awaited with Task.WhenAll, plus a chain of
// awaits on pending tasks (continuation scheduling cost). Deterministic checksum output.
const int Tasks = 2000;
const int Work = 20000;

long total = 0;
for (int round = 0; round < 4; round++)
{
    var jobs = new Task<long>[Tasks];
    for (int i = 0; i < Tasks; i++)
    {
        int seed = i + round * Tasks;
        jobs[i] = Task.Run(() => Crunch(seed));
    }
    long[] results = await Task.WhenAll(jobs);
    foreach (var r in results) total = unchecked(total * 31 + r);
}
Console.WriteLine($"fan-out checksum: {total}");

// Pipeline: each stage awaits the previous one (continuations hop between pool threads).
long pipe = 0;
for (int i = 0; i < 20000; i++) pipe += await Stage(i);
Console.WriteLine($"pipeline sum: {pipe}");

static long Crunch(int seed)
{
    ulong x = (ulong)seed * 2654435761UL + 1;
    long acc = 0;
    for (int k = 0; k < Work; k++)
    {
        x ^= x << 13; x ^= x >> 7; x ^= x << 17;
        acc += (long)(x % 1000);
    }
    return acc;
}

static async Task<long> Stage(int i)
{
    await Task.Yield();
    return i % 7;
}
