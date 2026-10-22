// .Result / .Wait() wrap failures in AggregateException; TaskCompletionSource drives tasks by hand.
var faulty = Fail("kaput");
try { _ = faulty.Result; }
catch (AggregateException e) { Console.WriteLine($"{e.Message} | inner: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
try { faulty.Wait(); } catch (AggregateException e) { Console.WriteLine("Wait -> " + e.InnerException?.Message); }
Console.WriteLine(faulty.Exception?.InnerException?.Message);

var all = Task.WhenAll(Fail("one"), Fail("two"));
try { all.Wait(); } catch (AggregateException e) { Console.WriteLine(e.Message); }

var tcs = new TaskCompletionSource<int>();
var waiter = Consume(tcs.Task);
Console.WriteLine($"before SetResult: {tcs.Task.IsCompleted}");
tcs.SetResult(42);
Console.WriteLine(await waiter);
Console.WriteLine(tcs.TrySetResult(7));
try { tcs.SetResult(8); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

var failing = new TaskCompletionSource<string>();
failing.SetException(new TimeoutExceptionLike("too slow"));
try { await failing.Task; } catch (TimeoutExceptionLike e) { Console.WriteLine("TCS exception -> " + e.Message); }

var canceled = new TaskCompletionSource();
Console.WriteLine(canceled.TrySetCanceled());
try { await canceled.Task; } catch (TaskCanceledException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
Console.WriteLine(canceled.Task.IsCanceled);

var signal = new TaskCompletionSource();
var worker = Worker(signal.Task);
signal.SetResult();
await worker;

async Task<int> Fail(string message)
{
    await Task.Delay(1);
    throw new InvalidOperationException(message);
}

async Task<int> Consume(Task<int> source) => await source + 1;

async Task Worker(Task signal)
{
    await signal;
    Console.WriteLine("worker released");
}

class TimeoutExceptionLike(string message) : Exception(message);
