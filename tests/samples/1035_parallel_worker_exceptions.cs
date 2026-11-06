// Exceptions thrown by parallel iterations that run on a helper thread rather than the calling thread. csharpc's
// runtime catches those at the top of the helper's stack (csrt_trap_call, after running the finally blocks) and
// rethrows them on the caller, so Parallel.For / ForEach / Invoke surface one AggregateException either way.
// Every iteration sleeps a little so that helpers get to run some, and any iteration off the main thread throws
// (the last one always does, so a machine where no helper joins the loop still throws). The output never says
// which thread threw or how many inner exceptions .NET collected.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

int main = Environment.CurrentManagedThreadId;
int finallies = 0;

void Body(bool last)
{
    try
    {
        Thread.Sleep(1);
        if (last || Environment.CurrentManagedThreadId != main) throw new InvalidOperationException("boom");
    }
    finally { Interlocked.Increment(ref finallies); }
}

bool Check(Action loop)
{
    try { loop(); return false; }
    catch (AggregateException e)
    {
        return e.InnerExceptions.Count >= 1 && e.InnerExceptions.All(x => x is InvalidOperationException { Message: "boom" });
    }
}

const int Rounds = 8;
const int Count = 16;
int forOk = 0, forEachOk = 0, invokeOk = 0, longOk = 0;
int[] items = Enumerable.Range(0, Count).ToArray();
Action[] actions = Enumerable.Range(0, Count).Select(i => (Action)(() => Body(i == Count - 1))).ToArray();

for (int r = 0; r < Rounds; r++)
{
    if (Check(() => Parallel.For(0, Count, i => Body(i == Count - 1)))) forOk++;
    if (Check(() => Parallel.For(0L, (long)Count, i => Body(i == Count - 1)))) longOk++;
    if (Check(() => Parallel.ForEach(items, x => Body(x == Count - 1)))) forEachOk++;
    if (Check(() => Parallel.Invoke(actions))) invokeOk++;
}

Console.WriteLine($"For: {forOk}/{Rounds}");
Console.WriteLine($"For long: {longOk}/{Rounds}");
Console.WriteLine($"ForEach: {forEachOk}/{Rounds}");
Console.WriteLine($"Invoke: {invokeOk}/{Rounds}");
Console.WriteLine("finally blocks ran: " + (finallies >= 4 * Rounds));
