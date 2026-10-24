// Which frames a stack trace holds: from the throw point to the catching frame; `throw;` keeps
// them (the rethrowing frame isn't listed twice), `throw e;` starts over, ExceptionDispatchInfo
// adds "--- End of stack trace from previous location ---", filters see the frames up to their
// frame, and exceptions leaving a finally block (or a using's Dispose) list the method twice when
// the finally ran on the normal path, once when it ran during unwinding.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text;

class Program
{
    static void Main()
    {
        Show("rethrow", Rethrow);
        Show("rethrow in caller", RethrowInCaller);
        Show("nested rethrow", NestedRethrow);
        Show("rethrow through finally", RethrowThroughFinally);
        Show("throw e", ThrowAgain);
        Show("throw saved", ThrowSaved);
        Show("dispatch info", Dispatch);
        Show("filter", Filter);
        Show("catch throws", CatchThrows);
        Show("finally throws", FinallyThrows);
        Show("finally throws while unwinding", FinallyThrowsWhileUnwinding);
        Show("finally caught in method", FinallyCaughtInMethod);
        Show("using dispose", UsingDispose);
        Show("using var dispose", UsingVarDispose);
        Show("foreach dispose", ForeachDispose);
        Show("thrown twice", ThrownTwice);
    }

    static void Thrower() => throw new InvalidOperationException("thrown");

    static void Rethrow()
    {
        try
        {
            Thrower();
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("rethrowing");
            throw;
        }
    }

    static void RethrowInCaller()
    {
        try
        {
            Rethrow();
        }
        catch
        {
            throw;
        }
    }

    static void NestedRethrow()
    {
        try
        {
            try
            {
                Thrower();
            }
            catch
            {
                try { throw; }
                catch { throw; }
            }
        }
        catch (Exception e) when (e.Message == "nope")
        {
        }
    }

    static void RethrowThroughFinally()
    {
        try
        {
            Thrower();
        }
        catch (Exception)
        {
            try
            {
                throw;
            }
            finally
            {
                Console.WriteLine("finally");
            }
        }
    }

    static void ThrowAgain()
    {
        try
        {
            Thrower();
        }
        catch (Exception e)
        {
            throw e;
        }
    }

    static void ThrowSaved()
    {
        Exception? saved = null;
        try { Thrower(); } catch (Exception e) { saved = e; }
        Console.WriteLine(Clean(saved!.StackTrace));
        throw saved;
    }

    static void Dispatch()
    {
        ExceptionDispatchInfo? info = null;
        try
        {
            Thrower();
        }
        catch (Exception e)
        {
            info = ExceptionDispatchInfo.Capture(e);
        }
        Console.WriteLine("captured");
        info!.Throw();
    }

    static void Filter()
    {
        try
        {
            Thrower();
        }
        catch (Exception e) when (Log(e))
        {
        }
    }

    static bool Log(Exception e)
    {
        Console.Write("filter sees: " + Clean(e.StackTrace));
        return false;
    }

    static void CatchThrows()
    {
        try
        {
            Thrower();
        }
        catch
        {
            Console.WriteLine("handling");
            Thrower();
        }
    }

    static void FinallyThrows()
    {
        try
        {
            Console.WriteLine("try");
        }
        finally
        {
            Console.WriteLine("finally");
            Thrower();
        }
    }

    static void FinallyThrowsWhileUnwinding()
    {
        try
        {
            Thrower();
        }
        finally
        {
            Console.WriteLine("finally");
            throw new ArgumentException("from finally");
        }
    }

    static void FinallyCaughtInMethod()
    {
        try
        {
            try
            {
                Console.WriteLine("inner try");
            }
            finally
            {
                Thrower();
            }
        }
        catch (Exception e)
        {
            Console.Write("caught here: " + Clean(e.StackTrace));
        }
    }

    sealed class Resource : IDisposable
    {
        public void Dispose() => throw new ObjectDisposedException("resource");
    }

    static void UsingDispose()
    {
        using (new Resource())
        {
            Console.WriteLine("using");
        }
    }

    static void UsingVarDispose()
    {
        using var resource = new Resource();
        Console.WriteLine("using var");
    }

    sealed class Items : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => new Enumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        sealed class Enumerator : IEnumerator<int>
        {
            int _n;
            public int Current => _n;
            object IEnumerator.Current => _n;
            public bool MoveNext() => ++_n < 3;
            public void Reset() { }
            public void Dispose() => throw new InvalidOperationException("dispose");
        }
    }

    static void ForeachDispose()
    {
        foreach (var item in new Items())
        {
            Console.WriteLine(item);
        }
    }

    static void ThrownTwice()
    {
        var e = new InvalidOperationException("twice");
        try { throw e; } catch { }
        try
        {
            throw e;
        }
        catch { }
        Console.Write("after second throw: " + Clean(e.StackTrace));
        throw e;
    }

    static void Show(string name, Action action)
    {
        Console.WriteLine("== " + name);
        try
        {
            action();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.GetType().Name + ": " + e.Message);
            Console.Write(Clean(e.StackTrace));
        }
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
