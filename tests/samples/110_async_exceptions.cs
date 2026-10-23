// Exceptions in async methods are captured in the task and rethrown at await.
try { await Boom("first"); } catch (InvalidOperationException e) { Console.WriteLine("caught " + e.Message); }

var gate = new TaskCompletionSource();
var pending = BoomAfter(gate.Task, "later");   // can't fault until the gate opens (deterministic under real threads)
Console.WriteLine($"faulted before running: {pending.IsFaulted}");
gate.SetResult();
try { await pending; } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
Console.WriteLine($"faulted after: {pending.IsFaulted}, completed: {pending.IsCompleted}, ok: {pending.IsCompletedSuccessfully}");

// Thrown synchronously (before any await): still goes into the task.
var sync = ThrowsImmediately();
Console.WriteLine($"sync fault captured: {sync.IsFaulted}");
try { await sync; } catch (ArgumentException e) { Console.WriteLine(e.Message); }

Console.WriteLine(await Divide(10, 2));
try { await Divide(1, 0); } catch (DivideByZeroException e) { Console.WriteLine(e.Message); }

// try/catch/finally inside async methods, across awaits.
Console.WriteLine(await Guarded());

// Task.WhenAll: await rethrows the first exception. "b" faults only after "a" has, otherwise which one
// comes first races between thread-pool threads under real .NET.
var first = Boom("a");
try { await Task.WhenAll(first, Divide(4, 2), BoomAfter(Settled(first), "b")); }
catch (InvalidOperationException e) { Console.WriteLine("WhenAll -> " + e.Message); }

// GetAwaiter().GetResult() rethrows the original exception.
try { Boom("direct").GetAwaiter().GetResult(); } catch (InvalidOperationException e) { Console.WriteLine("GetResult -> " + e.Message); }

// Exception chain through several async frames.
try { await Outer(); } catch (FormatException e) { Console.WriteLine($"outer saw {e.Message}"); }

var fromEx = Task.FromException<int>(new NotSupportedException("nope"));
try { Console.WriteLine(await fromEx); } catch (NotSupportedException e) { Console.WriteLine("FromException -> " + e.Message); }

async Task BoomAfter(Task gate, string message)
{
    await gate;
    throw new InvalidOperationException(message);
}

async Task Settled(Task task)
{
    try { await task; } catch { }
}

async Task Boom(string message)
{
    await Task.Yield();
    throw new InvalidOperationException(message);
}

async Task ThrowsImmediately()
{
    throw new ArgumentException("bad argument");
#pragma warning disable CS0162
    await Task.Yield();
#pragma warning restore CS0162
}

async Task<int> Divide(int a, int b)
{
    await Task.Delay(1);
    return a / b;
}

async Task<string> Guarded()
{
    var log = "";
    try
    {
        log += "try;";
        await Task.Delay(1);
        throw new InvalidOperationException("x");
    }
    catch (InvalidOperationException)
    {
        log += "catch;";
        await Task.Yield();
    }
    finally
    {
        log += "finally;";
    }
    return log;
}

async Task Outer() => await Middle();
async Task Middle() { await Task.Yield(); await Inner(); }
async Task Inner() { await Task.Delay(1); int.Parse("not a number"); }
