// Stack traces through async methods and iterators: async frames are listed under the async
// method's own name, in await order, whether the exception came before or after the first await;
// iterator frames read "Method()+MoveNext()".
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        try { await Outer(); } catch (Exception e) { Report("await chain", e); }
        try { await ThrowsBeforeAwait(); } catch (Exception e) { Report("before first await", e); }
        try { Console.WriteLine(await WithResult()); } catch (Exception e) { Report("Task<T>", e); }
        try { await RethrowsInAsync(); } catch (Exception e) { Report("rethrow in async", e); }
        try { await Local(); } catch (Exception e) { Report("async local function", e); }
        try { Sync(); } catch (Exception e) { Report("GetAwaiter().GetResult()", e); }
        try { foreach (var x in Numbers(3)) Console.WriteLine(x); } catch (Exception e) { Report("iterator", e); }
        try { Console.WriteLine(Sum(Numbers(5))); } catch (Exception e) { Report("iterator in callee", e); }

        async Task Local()
        {
            await Task.Yield();
            Thrower("local");
        }
    }

    static void Thrower(string what) => throw new InvalidOperationException(what);

    static async Task Outer()
    {
        await Task.Yield();
        await Middle();
    }

    static async Task Middle()
    {
        await Task.Delay(1);
        await Inner();
    }

    static async Task Inner()
    {
        await Task.Yield();
        Thrower("inner");
    }

    static async Task ThrowsBeforeAwait()
    {
        Thrower("early");
        await Task.Yield();
    }

    static async Task<int> WithResult()
    {
        await Task.Yield();
        return Compute(7);
    }

    static int Compute(int x) => x > 5 ? throw new ArgumentOutOfRangeException(nameof(x)) : x;

    static async Task RethrowsInAsync()
    {
        await Task.Yield();
        try
        {
            Thrower("async rethrow");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }

    static void Sync() => Outer().GetAwaiter().GetResult();

    static IEnumerable<int> Numbers(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (i == 2) Thrower("iterator " + count);
            yield return i;
        }
    }

    static int Sum(IEnumerable<int> values)
    {
        int total = 0;
        foreach (var v in values) total += v;
        return total;
    }

    static void Report(string name, Exception e)
    {
        Console.WriteLine("== " + name + ": " + e.Message);
        Console.Write(Clean(e.StackTrace));
    }

    /// <summary>"   at M() in /some/dir/file.cs:line 3" -> "   at M() in file.cs:line 3", one line each.</summary>
    static string Clean(string? text)
    {
        if (text == null) return "<null>" + Environment.NewLine;
        var sb = new StringBuilder();
        foreach (var line in text.Split(Environment.NewLine))
        {
            var at = line.IndexOf(" in ", StringComparison.Ordinal);
            var cut = Math.Max(line.LastIndexOf('/'), line.LastIndexOf('\\'));
            sb.AppendLine(at >= 0 && cut > at ? line.Substring(0, at + 4) + line.Substring(cut + 1) : line);
        }
        return sb.ToString();
    }
}
