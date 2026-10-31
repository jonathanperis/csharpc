// Exception filters around awaits (the try's frame record leaves with the suspended method and comes
// back when it resumes), in async methods that fault their task, and around iterators: .NET's
// MoveNext doesn't catch, so a caller's filter runs before the iterator's finally blocks.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

class Program
{
    static bool Log(string what, bool result)
    {
        Console.WriteLine("filter " + what + " -> " + result);
        return result;
    }

    static async Task<int> ThrowAfterAwait(int n)
    {
        await Task.Yield();
        try
        {
            await Task.Delay(1);
            throw new InvalidOperationException("after await " + n);
        }
        finally { Console.WriteLine("async finally " + n); }
    }

    static async Task<int> Caller(int n)
    {
        var attempts = 0;
        try
        {
            attempts++;
            await Task.Delay(1);
            return await ThrowAfterAwait(n);
        }
        catch (InvalidOperationException e) when (Log($"caller {n} attempts {attempts}: {e.Message}", n % 2 == 0))
        {
            return -n;
        }
    }

    static async Task Outer()
    {
        try
        {
            Console.WriteLine("even: " + await Caller(2));
            Console.WriteLine("odd: " + await Caller(3));
        }
        catch (InvalidOperationException e) when (Log("outer " + e.Message, true))
        {
            Console.WriteLine("outer caught " + e.Message);
        }
    }

    static async Task SynchronousThrow()
    {
        try { throw new FormatException("sync part"); }
        finally { Console.WriteLine("finally in async body"); }
#pragma warning disable CS0162
        await Task.Yield();
#pragma warning restore CS0162
    }

    static IEnumerable<int> Numbers()
    {
        try
        {
            yield return 1;
            yield return 2;
            throw new ArgumentException("in iterator");
        }
        finally { Console.WriteLine("iterator finally"); }
    }

    static async Task Main()
    {
        await Outer();

        Console.WriteLine("-- the async body catches into its task: no filter runs at the throw");
        Task task;
        try { task = SynchronousThrow(); }
        catch (FormatException) when (Log("call site", true)) { task = Task.CompletedTask; }
        try { await task; }
        catch (FormatException e) when (Log("await " + e.Message, true)) { Console.WriteLine("awaited and caught"); }

        Console.WriteLine("-- iterator");
        try
        {
            foreach (var n in Numbers()) Console.WriteLine("item " + n);
        }
        catch (ArgumentException e) when (Log("iterator caller " + e.Message, true)) { Console.WriteLine("caught from iterator"); }

        Console.WriteLine("-- Task.Run");
        var result = await Task.Run(() =>
        {
            try { throw new TimeoutException("pool"); }
            catch (TimeoutException) when (Log("on the pool", true)) { return 42; }
        });
        Console.WriteLine("pool result " + result);
    }
}
