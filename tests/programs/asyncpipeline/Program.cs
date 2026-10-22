// An async processing pipeline: producer -> transform stages -> aggregator, with bounded queues built on
// TaskCompletionSource, IAsyncEnumerable stages, Task.WhenAll fan-out, retries, timeouts and cancellation.
// All output is deterministic (sequenced by awaiting, not by racing threads).
var log = new List<string>();
var pipeline = new Pipeline(log);
var results = await pipeline.RunAsync(Enumerable.Range(1, 12));
Console.WriteLine($"processed {results.Count} items");
foreach (var r in results.OrderBy(r => r.Id)) Console.WriteLine($"  #{r.Id,2} {r.Value,8:F2} attempts={r.Attempts} {(r.Error is null ? "" : "ERR " + r.Error)}");
Console.WriteLine($"stage log: {log.Count} entries; first: {log[0]}; last: {log[^1]}");

var cts = new CancellationTokenSource();
var slow = SlowSequence(cts.Token);
int seen = 0;
try
{
    await foreach (var x in slow)
    {
        seen += x;
        if (x == 3) cts.Cancel();
    }
}
catch (OperationCanceledException) { Console.WriteLine($"cancelled after seeing sum {seen}"); }

var timeouts = await Task.WhenAll(WithTimeout(Work(10, 5), 50), WithTimeout(Work(200, 6), 50), WithTimeout(Work(1, 7), 50));
Console.WriteLine("timeouts: " + string.Join(", ", timeouts.Select(t => t?.ToString() ?? "timeout")));

var queue = new AsyncQueue<int>(capacity: 2);
var producer = Task.Run(async () =>
{
    for (int i = 0; i < 6; i++) await queue.EnqueueAsync(i * i);
    queue.Complete();
});
var consumed = new List<int>();
while (await queue.DequeueAsync() is { } item) consumed.Add(item);
await producer;
Console.WriteLine($"queue: [{string.Join(",", consumed)}], max depth seen {queue.MaxDepth}");

static async IAsyncEnumerable<int> SlowSequence([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
{
    for (int i = 1; i <= 10; i++)
    {
        await Task.Delay(1, token);
        token.ThrowIfCancellationRequested();
        yield return i;
    }
}

static async Task<int> Work(int delayMs, int value) { await Task.Delay(delayMs); return value * 10; }

static async Task<int?> WithTimeout(Task<int> task, int ms)
{
    var winner = await Task.WhenAny(task, Task.Delay(ms));
    return winner == task ? await task : null;
}

record Result(int Id, double Value, int Attempts, string? Error);

class Pipeline(List<string> log)
{
    public async Task<List<Result>> RunAsync(IEnumerable<int> ids)
    {
        var results = new List<Result>();
        await foreach (var batch in Batches(Produce(ids), size: 4))
        {
            log.Add($"batch [{string.Join(",", batch)}]");
            var processed = await Task.WhenAll(batch.Select(ProcessWithRetry));
            results.AddRange(processed);
        }
        return results;
    }

    private async IAsyncEnumerable<int> Produce(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            await Task.Yield();
            log.Add($"produce {id}");
            yield return id;
        }
    }

    private static async IAsyncEnumerable<List<int>> Batches(IAsyncEnumerable<int> source, int size)
    {
        var batch = new List<int>(size);
        await foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count == size) { yield return batch; batch = new List<int>(size); }
        }
        if (batch.Count > 0) yield return batch;
    }

    private async Task<Result> ProcessWithRetry(int id)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var value = await Transform(id, attempt);
                return new Result(id, value, attempt, null);
            }
            catch (InvalidOperationException e) when (attempt < 3)
            {
                log.Add($"retry {id} after '{e.Message}'");
            }
            catch (Exception e)
            {
                return new Result(id, double.NaN, attempt, e.Message);
            }
        }
    }

    private static async Task<double> Transform(int id, int attempt)
    {
        await Task.Delay(id % 3);
        if (id % 5 == 0 && attempt < 2) throw new InvalidOperationException($"flaky {id}");
        if (id == 11) throw new ArgumentException("bad input 11");
        return Math.Sqrt(id) * 10 + attempt;
    }
}

class AsyncQueue<T>(int capacity) where T : struct
{
    private readonly Queue<T> _items = new();
    private readonly Queue<TaskCompletionSource> _spaceWaiters = new();
    private TaskCompletionSource? _itemWaiter;
    private bool _completed;
    public int MaxDepth { get; private set; }

    public async Task EnqueueAsync(T item)
    {
        while (_items.Count >= capacity)
        {
            var tcs = new TaskCompletionSource();
            _spaceWaiters.Enqueue(tcs);
            await tcs.Task;
        }
        _items.Enqueue(item);
        MaxDepth = Math.Max(MaxDepth, _items.Count);
        var waiter = _itemWaiter;
        _itemWaiter = null;
        waiter?.SetResult();
    }

    public void Complete()
    {
        _completed = true;
        var waiter = _itemWaiter;
        _itemWaiter = null;
        waiter?.SetResult();
    }

    public async Task<T?> DequeueAsync()
    {
        while (_items.Count == 0)
        {
            if (_completed) return null;
            _itemWaiter = new TaskCompletionSource();
            await _itemWaiter.Task;
        }
        var item = _items.Dequeue();
        if (_spaceWaiters.TryDequeue(out var space)) space.SetResult();
        return item;
    }
}
