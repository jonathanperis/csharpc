// Four threads allocate objects of changing sizes while collections empty whole chunks of the heap and
// hand them to other size classes. A slot a thread has claimed but not handed out yet starts with a null
// method table, never with a field of an older, differently sized object (the collector crashed reading
// one as a method table in about one run in ten of benchmarks/gc/gclatency.cs).
const int Threads = 4, Rounds = 40, PerRound = 4000;
var sums = new long[Threads];
var workers = new Thread[Threads];
using var barrier = new Barrier(Threads + 1);
for (int t = 0; t < Threads; t++)
{
    int id = t;
    workers[t] = new Thread(() =>
    {
        long sum = 0;
        var keep = new object[64];
        for (int round = 0; round < Rounds; round++)
        {
            barrier.SignalAndWait();
            // Each round favours another size class, so chunks emptied by the last collection change class.
            int shape = (round + id) % 4;
            for (int i = 0; i < PerRound; i++)
            {
                object o = shape switch
                {
                    0 => new Small(i, id),
                    1 => new string((char)('a' + i % 26), 1 + (i + round) % 40),
                    2 => new long[(i + round) % 24 + 1],
                    _ => new Medium(i, round, "m" + (i % 10)),
                };
                keep[i % keep.Length] = o;
                sum += o switch
                {
                    Small s => s.A + s.B,
                    string s => s.Length,
                    long[] a => a.Length,
                    Medium m => m.Total,
                    _ => 0,
                };
            }
            barrier.SignalAndWait();
        }
        sums[id] = sum + keep.Length;
    });
    workers[t].Start();
}
for (int round = 0; round < Rounds; round++)
{
    barrier.SignalAndWait(); // the workers allocate
    barrier.SignalAndWait();
    GC.Collect();            // empties the chunks of the classes this round used
}
foreach (var w in workers) w.Join();
Console.WriteLine(sums.Sum());

sealed class Small(int a, int b) { public int A = a; public int B = b; }

sealed class Medium(int a, int b, string tag)
{
    public long X = a, Y = b, Z = a ^ b;
    public string Tag = tag;
    public long Total => X + Y + Z + Tag.Length;
}
