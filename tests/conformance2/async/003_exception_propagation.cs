try { await Boom(Task.CompletedTask); } catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }
// The task can't fault before the gate opens: checking IsFaulted right after starting a task that faults on a
// thread-pool thread would race, under real .NET too.
var gate = new TaskCompletionSource();
var t = Boom(gate.Task);
Console.WriteLine(t.IsFaulted);
gate.SetResult();
try { t.Wait(); } catch (AggregateException e) { Console.WriteLine(e.InnerException!.Message); }
static async Task Boom(Task gate) { await gate; await Task.Yield(); throw new InvalidOperationException("boom"); }
