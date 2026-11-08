#:property Optimize=true
// An async method the program never runs may use what csharpc can't compile yet (BigInteger.GetByteCount,
// AppDomain.DomainUnload, Process.WaitForExitAsync): reaching a pooled ValueTask box, AsyncBuilderCore.Start or another
// builder for ONE state machine must not make every other state machine of the program reachable. Such a method is
// a stub (warning CSLLVM0007) that would end the program with the diagnostic if it were called. Compiled from IL
// (--via-il) every async method is a state machine, which is where this showed.
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

Console.WriteLine("start");
// Listing the assembly's types (ASP.NET's startup scan does) builds the method tables of all of them, the async
// state machines of the never-called methods below included.
Console.WriteLine(typeof(Pooled).Assembly.GetTypes().Any(t => t.Name == nameof(Pooled)));
Console.WriteLine(await Pooled.Suspends(20));
Console.WriteLine(await Pooled.Completes(5));
try { await Pooled.FaultsFirst(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
Console.WriteLine(await Pooled.Twice(7));
Console.WriteLine(await Plain.Add(1, 2));
await Plain.Nothing();
Console.WriteLine("end");

public static class Pooled
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> Suspends(int x)
    {
        await Task.Yield();
        return x + 1;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> Completes(int x) => await Task.FromResult(x * 2);

    // Faults before the first await: the box exists before any state machine was copied into it.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> FaultsFirst()
    {
        if (DateTime.MinValue.Year == 1) throw new InvalidOperationException("faulted before the first await");
        await Task.Yield();
        return 0;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> Twice(int x)
    {
        await Task.Delay(1);
        await Task.Yield();
        return x * 3;
    }

    // Never called.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> NeverPooled(int x)
    {
        await Task.Yield();
        return new BigInteger(x).GetByteCount();
    }
}

public static class Plain
{
    public static async Task<int> Add(int a, int b)
    {
        await Task.Yield();
        return a + b;
    }

    public static async Task Nothing() => await Task.Yield();

    // Never called.
    public static async Task NeverTask()
    {
        await Task.Yield();
        await Process.GetCurrentProcess().WaitForExitAsync();
    }

    public static async ValueTask NeverValueTask()
    {
        await Task.Yield();
        AppDomain.CurrentDomain.DomainUnload += (s, e) => Console.WriteLine("unload");
    }

    public static async void NeverVoid()
    {
        await Task.Yield();
        Console.WriteLine(new BigInteger(2).GetByteCount());
    }
}
