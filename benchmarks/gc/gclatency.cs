using System.Diagnostics;

// Allocation, pause and live-heap benchmark (benchmarks/gc/gc.sh).
//
// Stdout is one checksum, recorded from real .NET. It depends only on the work, which is fixed at
// 4 threads so a 2-core and a 4-core machine print the same line. Stderr is one metrics line per
// phase (gc.sh parses it); those timings are not part of the checksum.
//
// churn: many short-lived Fortune objects (id + message string + a formatted line) and a small
// sliding window, the shape of a Fortunes response.
// livebuild / livemutate: a pointer-dense graph large enough that a collection has to trace tens
// of megabytes, then more garbage allocated while that graph stays live.

int threads = ResolveThreads();
long checksum = RunChurn(threads) + RunLive(threads);
Console.WriteLine(checksum);

static int ResolveThreads()
{
    string? spec = Environment.GetEnvironmentVariable("GC_BENCH_THREADS");
    if (spec != null && int.TryParse(spec, out int forced) && forced >= 1 && forced <= 16) return forced;
    return 4;
}

static long RunChurn(int threads)
{
    const int OpsPerThread = 500_000;
    var samples = new long[threads][];
    var sums = new long[threads];
    int collections = GC.CollectionCount(0);
    TimeSpan pause = GC.GetTotalPauseDuration();
    long allocated = GC.GetTotalAllocatedBytes();
    Gate.Reset();
    var clock = new Stopwatch();
    var workers = new Thread[threads];
    for (int t = 0; t < threads; t++)
    {
        int id = t;
        samples[id] = new long[OpsPerThread];
        workers[id] = new Thread(() => sums[id] = ChurnWorker(id, OpsPerThread, samples[id]));
        workers[id].Start();
    }
    Gate.ReleaseWhenReady(threads, clock);
    for (int t = 0; t < threads; t++) workers[t].Join();
    clock.Stop();
    long sum = 0;
    for (int t = 0; t < threads; t++) sum += sums[t];
    Report("churn", threads, OpsPerThread * threads, clock.ElapsedMilliseconds,
        GC.CollectionCount(0) - collections, (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
        GC.GetTotalAllocatedBytes() - allocated, GC.GetTotalMemory(false), samples);
    return sum;
}

static long ChurnWorker(int id, int ops, long[] samples)
{
    var window = new Fortune[1024];
    Gate.WaitForRelease();
    long sum = 0;
    var sw = Stopwatch.StartNew();
    long previous = sw.ElapsedTicks;
    int begin = id * ops;
    for (int i = 0; i < ops; i++)
    {
        int n = begin + i;
        var fortune = new Fortune(n, "fortune " + (n % 1000));
        window[i & 1023] = fortune;
        string line = fortune.Id + " " + fortune.Message;
        sum += line.Length;
        Fortune? older = window[(i + 1) & 1023];
        if (older != null) sum += older.Id & 1023;
        long now = sw.ElapsedTicks;
        samples[i] = now - previous;
        previous = now;
    }
    return sum;
}

static long RunLive(int threads)
{
    const int CellsPerThread = 200_000;
    const int MutatePerThread = 120_000;
    var cells = new Cell[threads][];
    var buildSamples = new long[threads][];
    int collections = GC.CollectionCount(0);
    TimeSpan pause = GC.GetTotalPauseDuration();
    long allocated = GC.GetTotalAllocatedBytes();
    Gate.Reset();
    var clock = new Stopwatch();
    var workers = new Thread[threads];
    for (int t = 0; t < threads; t++)
    {
        int id = t;
        cells[id] = new Cell[CellsPerThread];
        buildSamples[id] = new long[CellsPerThread];
        workers[id] = new Thread(() => BuildWorker(id, cells[id], buildSamples[id]));
        workers[id].Start();
    }
    Gate.ReleaseWhenReady(threads, clock);
    for (int t = 0; t < threads; t++) workers[t].Join();
    clock.Stop();
    Report("livebuild", threads, CellsPerThread * threads, clock.ElapsedMilliseconds,
        GC.CollectionCount(0) - collections, (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
        GC.GetTotalAllocatedBytes() - allocated, GC.GetTotalMemory(false), buildSamples);

    // One full collection before the timed mutations, so the first measured pause is a collection
    // of the retained graph rather than the collector's first-time setup.
    GC.Collect();

    var mutateSamples = new long[threads][];
    var sinks = new long[threads];
    collections = GC.CollectionCount(0);
    pause = GC.GetTotalPauseDuration();
    allocated = GC.GetTotalAllocatedBytes();
    Gate.Reset();
    clock = new Stopwatch();
    for (int t = 0; t < threads; t++)
    {
        int id = t;
        mutateSamples[id] = new long[MutatePerThread];
        Cell[] mine = cells[id];
        workers[id] = new Thread(() => sinks[id] = MutateWorker(mine, mutateSamples[id]));
        workers[id].Start();
    }
    Gate.ReleaseWhenReady(threads, clock);
    for (int t = 0; t < threads; t++) workers[t].Join();
    clock.Stop();
    Report("livemutate", threads, MutatePerThread * threads, clock.ElapsedMilliseconds,
        GC.CollectionCount(0) - collections, (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
        GC.GetTotalAllocatedBytes() - allocated, GC.GetTotalMemory(false), mutateSamples);

    long sum = 0;
    for (int t = 0; t < threads; t++) sum += sinks[t];
    for (int t = 0; t < threads; t++)
    {
        Cell[] mine = cells[t];
        for (int i = 0; i < mine.Length; i++)
        {
            Cell c = mine[i];
            sum += c.Id;
            sum += c.Left!.Id;
            sum += c.Right!.Id;
        }
    }
    return sum;
}

static void BuildWorker(int id, Cell[] mine, long[] samples)
{
    Gate.WaitForRelease();
    int n = mine.Length;
    int seed = id * n;
    var sw = Stopwatch.StartNew();
    long previous = sw.ElapsedTicks;
    for (int i = 0; i < n; i++)
    {
        var cell = new Cell(seed + i, null, null);
        mine[i] = cell;
        if (i > 0)
        {
            cell.Left = mine[i - 1];
            mine[i - 1].Right = cell;
        }
        long now = sw.ElapsedTicks;
        samples[i] = now - previous;
        previous = now;
    }
    mine[0].Left = mine[n - 1];
    mine[n - 1].Right = mine[0];
}

static long MutateWorker(Cell[] mine, long[] samples)
{
    Gate.WaitForRelease();
    int n = mine.Length;
    int ops = samples.Length;
    long sink = 0;
    var sw = Stopwatch.StartNew();
    long previous = sw.ElapsedTicks;
    for (int i = 0; i < ops; i++)
    {
        int slot = (int)((i * 17L) % n);
        Node? dead = null;
        for (int k = 0; k < 16; k++) dead = new Node(k + slot, dead);
        // One heap slot keeps the newest list; the previous list becomes garbage. Reading it back
        // keeps the store (and so the allocation) observable on every runtime.
        mine[0].Junk = dead;
        sink += mine[0].Junk!.Value;
        mine[slot].Right = new Cell(mine[slot].Id ^ (i + 1), mine[(slot + 1) % n], null);
        long now = sw.ElapsedTicks;
        samples[i] = now - previous;
        previous = now;
    }
    return sink;
}

static void Report(string phase, int threads, int ops, long wallMs, int collections, double pauseMs, long allocated, long heap, long[][] samples)
{
    int count = 0;
    for (int t = 0; t < samples.Length; t++) count += samples[t].Length;
    var all = new long[count];
    int offset = 0;
    for (int t = 0; t < samples.Length; t++)
    {
        Array.Copy(samples[t], 0, all, offset, samples[t].Length);
        offset += samples[t].Length;
    }
    Array.Sort(all);
    double us = 1e6 / Stopwatch.Frequency;
    long Percentile(double q)
    {
        if (count == 0) return 0;
        int index = (int)(q * (count - 1));
        if (index < 0) index = 0;
        if (index >= count) index = count - 1;
        return all[index];
    }
    long opsPerSec = wallMs > 0 ? ops * 1000L / wallMs : 0;
    Console.Error.WriteLine(
        "gcbench phase=" + phase +
        " threads=" + threads +
        " ops=" + ops +
        " wall_ms=" + wallMs +
        " ops_s=" + opsPerSec +
        " collections=" + collections +
        " pause_ms=" + pauseMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
        " alloc_mib=" + (allocated / 1048576.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
        " heap_mib=" + (heap / 1048576.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
        " p50_us=" + (Percentile(0.50) * us).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
        " p99_us=" + (Percentile(0.99) * us).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
        " p999_us=" + (Percentile(0.999) * us).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
        " max_us=" + (Percentile(1.0) * us).ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
}

static class Gate
{
    static int s_arrived, s_go;

    public static void Reset()
    {
        s_arrived = 0;
        s_go = 0;
    }

    public static void WaitForRelease()
    {
        Interlocked.Increment(ref s_arrived);
        while (Volatile.Read(ref s_go) == 0) Thread.SpinWait(32);
    }

    public static void ReleaseWhenReady(int threads, Stopwatch clock)
    {
        while (Volatile.Read(ref s_arrived) < threads) Thread.SpinWait(32);
        clock.Start();
        Volatile.Write(ref s_go, 1);
    }
}

sealed class Fortune
{
    public readonly int Id;
    public readonly string Message;
    public Fortune(int id, string message) { Id = id; Message = message; }
}

sealed class Cell
{
    public readonly int Id;
    public Cell? Left;
    public Cell? Right;
    public Node? Junk;
    public Cell(int id, Cell? left, Cell? right) { Id = id; Left = left; Right = right; }
}

sealed class Node
{
    public readonly int Value;
    public readonly Node? Next;
    public Node(int value, Node? next) { Value = value; Next = next; }
}
