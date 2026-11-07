// BlockingCollection<T> between threads: a Take or Add blocked until CompleteAdding or a token cancels it,
// bounded producers and a consuming enumerable, TryTake with an infinite timeout, TakeFromAny / AddToAny blocked
// on several collections (WaitHandle.WaitAny over the collections' semaphores and the token), and
// WaitHandle.WaitAny / WaitAll, CancellationToken.WaitHandle and SemaphoreSlim.AvailableWaitHandle. Each result
// is the same whichever side runs first.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

static void Try(string label, Action a)
{
    try { a(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
}

Console.WriteLine("-- cancel and complete");
var empty = new BlockingCollection<int>();
var cts2 = new CancellationTokenSource();
var blocked = Task.Run(() => { try { empty.Take(cts2.Token); return "taken"; } catch (OperationCanceledException e) { return "canceled " + (e.CancellationToken == cts2.Token); } });
Thread.Sleep(30);
cts2.Cancel();
Console.WriteLine(blocked.Result);
var full = new BlockingCollection<int>(1);
full.Add(1);
var cts3 = new CancellationTokenSource();
var blockedAdd = Task.Run(() => { try { full.Add(2, cts3.Token); return "added"; } catch (OperationCanceledException) { return "canceled"; } });
Thread.Sleep(30);
cts3.Cancel();
Console.WriteLine(blockedAdd.Result);
var q = new BlockingCollection<int>();
var taker = Task.Run(() => { try { return q.Take().ToString(); } catch (InvalidOperationException e) { return e.Message; } });
Thread.Sleep(30);
q.CompleteAdding();
Console.WriteLine(taker.Result);
var timed = new BlockingCollection<int>();
var timedTaker = Task.Run(() => { try { return timed.TryTake(out var v, Timeout.Infinite) ? "took " + v : "nothing"; } catch (InvalidOperationException e) { return e.Message; } });
Thread.Sleep(30);
timed.Add(99);
Console.WriteLine(timedTaker.Result);

Console.WriteLine("-- consuming enumerable and producers");
var src = new BlockingCollection<int>();
var consumer = Task.Run(() => string.Join(",", src.GetConsumingEnumerable()));
for (int i = 0; i < 5; i++) src.Add(i);
src.CompleteAdding();
Console.WriteLine(consumer.Result);
var pc = new BlockingCollection<int>(3);
long sum = 0;
var cons = Task.Run(() => { foreach (var v in pc.GetConsumingEnumerable()) sum += v; });
var prods = Enumerable.Range(0, 4).Select(p => Task.Run(() => { for (int i = 1; i <= 100; i++) pc.Add(i); })).ToArray();
Task.WaitAll(prods);
pc.CompleteAdding();
cons.Wait();
Console.WriteLine(sum + " " + pc.IsCompleted + " " + pc.Count);

Console.WriteLine("-- across collections");
var a = new BlockingCollection<int>(1);
var b = new BlockingCollection<int>(1);
a.Add(1); b.Add(2);
var addAny = Task.Run(() => BlockingCollection<int>.AddToAny(new[] { a, b }, 7));
Thread.Sleep(30);
BlockingCollection<int>.TryTakeFromAny(new[] { b }, out var gone);
Console.WriteLine("added to " + addAny.Result + " after taking " + gone);
var c = new BlockingCollection<string>();
var d = new BlockingCollection<string>();
var takeAny = Task.Run(() => { var index = BlockingCollection<string>.TakeFromAny(new[] { c, d }, out var item); return index + ":" + item; });
Thread.Sleep(30);
d.Add("from d");
Console.WriteLine(takeAny.Result);
var tokenSource = new CancellationTokenSource();
var cancelAny = Task.Run(() => { try { BlockingCollection<string>.TakeFromAny(new[] { c, d }, out _, tokenSource.Token); return "took"; } catch (OperationCanceledException) { return "canceled"; } });
Thread.Sleep(30);
tokenSource.Cancel();
Console.WriteLine(cancelAny.Result);
var closing = new BlockingCollection<string>();
var closeAny = Task.Run(() => { try { BlockingCollection<string>.TakeFromAny(new[] { closing }, out _); return "took"; } catch (ArgumentException e) { return e.GetType().Name; } catch (InvalidOperationException e) { return e.GetType().Name; } });
Thread.Sleep(30);
closing.CompleteAdding();
Console.WriteLine(closeAny.Result);

Console.WriteLine("-- WaitHandle.WaitAny / WaitAll");
var e1 = new ManualResetEvent(false);
var e2 = new ManualResetEvent(false);
var anyWake = Task.Run(() => WaitHandle.WaitAny(new WaitHandle[] { e1, e2 }));
Thread.Sleep(30);
e2.Set();
Console.WriteLine(anyWake.Result);
var allWake = Task.Run(() => WaitHandle.WaitAll(new WaitHandle[] { e1, e2 }, 10000));
Thread.Sleep(30);
e1.Set();
Console.WriteLine(allWake.Result);
var auto = new AutoResetEvent(false);
var autoWake = Task.Run(() => WaitHandle.WaitAny(new WaitHandle[] { new ManualResetEvent(false), auto }));
Thread.Sleep(30);
auto.Set();
Console.WriteLine(autoWake.Result + " " + auto.WaitOne(0));
var cts = new CancellationTokenSource();
var never = new ManualResetEvent(false);
var tokenWake = Task.Run(() => WaitHandle.WaitAny(new WaitHandle[] { never, cts.Token.WaitHandle }));
Thread.Sleep(30);
cts.Cancel();
Console.WriteLine(tokenWake.Result);
var s = new SemaphoreSlim(0);
var semWake = Task.Run(() => WaitHandle.WaitAny(new WaitHandle[] { never, s.AvailableWaitHandle }));
Thread.Sleep(30);
s.Release();
Console.WriteLine(semWake.Result + " " + s.CurrentCount);
