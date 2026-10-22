// Fan-out / fan-in over simulated I/O (Task.Delay): total time ~ the longest delay, not the sum.
using System.Diagnostics;

var sw = Stopwatch.StartNew();
int[] delays = [80, 40, 120, 60, 100, 20, 90, 70];
var fetches = delays.Select((d, i) => FetchAsync(i, d)).ToArray();
string[] pages = await Task.WhenAll(fetches);
sw.Stop();
foreach (var p in pages) Console.WriteLine(p);
Console.WriteLine($"overlapped: {sw.ElapsedMilliseconds < delays.Sum() / 2}");

// WhenAny picks the fastest.
var race = await Task.WhenAny(FetchAsync(100, 200), FetchAsync(101, 10), FetchAsync(102, 150));
Console.WriteLine($"winner: {race.Result}");

// Cancellation of a slow fan-out.
using var cts = new CancellationTokenSource(30);
try { await Task.Delay(5000, cts.Token); Console.WriteLine("not canceled?"); }
catch (TaskCanceledException) { Console.WriteLine("slow request canceled"); }

static async Task<string> FetchAsync(int id, int ms)
{
    await Task.Delay(ms);
    return $"page {id} ({ms} ms)";
}
