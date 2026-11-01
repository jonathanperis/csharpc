// Where "--- End of stack trace from previous location ---" appears: an exception a task captured is
// rethrown by the await (or by ExceptionDispatchInfo.Throw) with the frames recorded so far, and .NET
// marks the boundary unless the last of those frames is an async method's (await chains read as one
// trace). Wait() and Result wrap the exception in an AggregateException thrown from .NET's own frames.
using System;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        try { await FromCompletionSource(); } catch (Exception e) { Report("TaskCompletionSource, awaited", e); }
        try { FromCompletionSource().GetAwaiter().GetResult(); } catch (Exception e) { Report("TaskCompletionSource, GetResult()", e); }
        try { await Task.FromException(new InvalidOperationException("from exception")); } catch (Exception e) { Report("Task.FromException", e); }
        try { await RethrowAfterAwait(); } catch (Exception e) { Report("ExceptionDispatchInfo after an await", e); }
        try { RethrowSync(); } catch (Exception e) { Report("ExceptionDispatchInfo in a method", e); }
        try { await Outer().ConfigureAwait(false); } catch (Exception e) { Report("ConfigureAwait(false)", e); }
        try { await Task.WhenAll(Inner("first"), Inner("second")); } catch (Exception e) { Report("Task.WhenAll", e); }
        try { await AsValueTask(); } catch (Exception e) { Report("ValueTask", e); }
        Func<Task> lambda = async () => { await Task.Yield(); Thrower("async lambda"); };
        try { await lambda(); } catch (Exception e) { Report("async lambda", e); }
        // Every await of a faulted task starts from the frames it captured.
        var faulted = Inner("awaited twice");
        try { await faulted; } catch (Exception e) { Report("first await", e); }
        try { await faulted; } catch (Exception e) { Report("second await", e); }
        try { await Rethrow(faulted); } catch (Exception e) { Report("third await, through a method", e); }
        try { Outer().Wait(); } catch (Exception e) { Report("Wait()", e); Report("Wait() inner", e.InnerException!); }
        try { Console.WriteLine(WithResult().Result); } catch (Exception e) { Report("Result", e); }
    }

    static void Thrower(string what) => throw new InvalidOperationException(what);

    static Task FromCompletionSource()
    {
        var source = new TaskCompletionSource();
        try { Thrower("completion source"); }
        catch (Exception e) { source.SetException(e); }
        return source.Task;
    }

    static async Task Inner(string what)
    {
        await Task.Yield();
        Thrower(what);
    }

    static async Task Outer()
    {
        await Task.Delay(1);
        await Inner("outer");
    }

    static async Task<int> WithResult()
    {
        await Task.Yield();
        Thrower("result");
        return 1;
    }

    static async Task Rethrow(Task task) => await task;

    static async ValueTask AsValueTask()
    {
        await Inner("value task");
    }

    static async Task RethrowAfterAwait()
    {
        ExceptionDispatchInfo? captured = null;
        try { await Inner("captured"); }
        catch (Exception e) { captured = ExceptionDispatchInfo.Capture(e); }
        captured!.Throw();
    }

    static void RethrowSync()
    {
        ExceptionDispatchInfo? captured = null;
        try { Thrower("captured sync"); }
        catch (Exception e) { captured = ExceptionDispatchInfo.Capture(e); }
        captured!.Throw();
    }

    static void Report(string name, Exception e)
    {
        Console.WriteLine("== " + name + ": " + e.GetType().Name + ": " + e.Message);
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
