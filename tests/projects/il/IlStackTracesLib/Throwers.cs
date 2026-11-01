using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace IlStackTracesLib;

public static class Throwers
{
    public static void Throw(string what) => throw new InvalidOperationException(what);

    public static int Recurse(int depth)
    {
        if (depth == 0) Throw("recursion");
        return Recurse(depth - 1) + 1;
    }

    [StackTraceHidden]
    public static void Hidden(string what) => Throw(what);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Inlined(string what) => Hidden(what);

    public static void ThroughHiddenFrames() => Inlined("hidden");

    public static void Generic<T>(T value, ref int x) => Box<T>.Nested<int>.Go(value, x);

    public static void Lambda(int n)
    {
        Action a = () => Throw("lambda " + n);
        a();
    }

    public static void Local(string s)
    {
        Inner();
        void Inner() => Throw("local " + s);
    }

    public static IEnumerable<int> Iterate(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i == 1) Throw("iterator");
            yield return i;
        }
    }

    public static void Callback(Action action) => action();

    public static async Task InnerAsync()
    {
        await Task.Yield();
        Throw("async");
    }

    public static async Task OuterAsync()
    {
        await Task.Delay(1);
        await InnerAsync();
    }

    public static async Task<int> EarlyAsync(int x)
    {
        if (x > 0) Throw("early");
        await Task.Yield();
        return x;
    }

    public static async ValueTask ValueTaskAsync()
    {
        try { await InnerAsync(); }
        catch (InvalidOperationException) { await Task.Yield(); throw; }
    }

    public static async Task<int> AwaitInCatch(bool async)
    {
        if (async) await ValueTaskAsync();
        return 1;
    }

    public static void Edi()
    {
        ExceptionDispatchInfo? edi = null;
        try { Throw("edi"); }
        catch (Exception e) { edi = ExceptionDispatchInfo.Capture(e); }
        edi.Throw();
    }

    public static Task FromCompletionSource()
    {
        var source = new TaskCompletionSource();
        try { Throw("completion source"); } catch (Exception e) { source.SetException(e); }
        return source.Task;
    }

    public static async Task AwaitCompletionSource() => await FromCompletionSource();

    public static void Wrapped()
    {
        try { Throw("inner"); }
        catch (Exception e) { throw new ApplicationException("outer", e); }
    }

    public static void Finally()
    {
        try { Console.WriteLine("in try"); }
        finally { Throw("finally"); }
    }
}

public class Box<T>
{
    public class Nested<U>
    {
        public static void Go(T value, U u) => Throwers.Throw("generic " + value + u);
    }
}

public struct Point
{
    public int X;
    public Point(int x) { if (x < 0) Throwers.Throw("ctor"); X = x; }
    public int this[int i] => i == 0 ? X : throw new ArgumentOutOfRangeException(nameof(i));
    public static Point operator +(Point a, Point b) => new Point(a.X + b.X - 100);
}
