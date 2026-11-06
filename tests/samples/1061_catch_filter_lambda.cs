// Lambdas and local functions inside a `when` filter (list.Any(x => ...), a delegate argument, a capture of the
// caught exception): the filter is a function of its own, but the lambda's body is not part of it. Also with
// AggregateException.InnerExceptions, the way Task.WaitAll failures are commonly filtered.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

static bool Matches(Func<int, bool> predicate) => predicate(1);
static void Run(string label, Action action)
{
    try { action(); }
    catch (Exception e) { Console.WriteLine($"{label}: unhandled {e.GetType().Name}"); }
}

Run("static lambda", () =>
{
    try { throw new FormatException(); }
    catch (Exception) when (new[] { 1, 2 }.Any(x => x == 2)) { Console.WriteLine("static lambda: caught"); }
});
Run("delegate argument", () =>
{
    try { throw new FormatException(); }
    catch (Exception) when (Matches(x => x == 1)) { Console.WriteLine("delegate argument: caught"); }
});
Run("rejecting lambda", () =>
{
    try
    {
        try { throw new FormatException(); }
        catch (Exception) when (Matches(x => x == 2)) { Console.WriteLine("rejecting lambda: wrongly caught"); }
    }
    catch (FormatException) { Console.WriteLine("rejecting lambda: passed to the outer handler"); }
});
Run("captures exception", () =>
{
    try { throw new InvalidOperationException("captured"); }
    catch (Exception e) when (new[] { 1 }.Any(x => e.Message.Length == 8 + x - 1)) { Console.WriteLine("captures exception: " + e.Message); }
});
Run("captures local", () =>
{
    var wanted = "needle";
    try { throw new ArgumentException("a needle here"); }
    catch (ArgumentException e) when (new[] { "hay", wanted }.Any(w => e.Message.Contains(w))) { Console.WriteLine("captures local: found " + wanted); }
});
Run("nested lambdas", () =>
{
    try { throw new FormatException(); }
    catch (Exception) when (new[] { 1, 2, 3 }.Select(x => new[] { x, x * 2 }.Any(y => y == 6)).Count(b => b) == 1) { Console.WriteLine("nested lambdas: caught"); }
});
Run("local function", () =>
{
    try { throw new FormatException(); }
    catch (Exception) when (Even(4)) { Console.WriteLine("local function: caught"); }
    static bool Even(int n) => Enumerable.Range(0, n).Count(i => i % 2 == 0) == n / 2;
});
Run("aggregate inner exceptions", () =>
{
    try { throw new AggregateException(new IndexOutOfRangeException(), new FormatException()); }
    catch (AggregateException ag) when (ag.InnerExceptions.All(x => x is IndexOutOfRangeException or FormatException))
    { Console.WriteLine("aggregate inner exceptions: both " + ag.InnerExceptions.Count); }
});
Run("task wait", () =>
{
    try { Task.WaitAll(Task.Run(() => throw new IndexOutOfRangeException()), Task.Run(() => throw new FormatException())); }
    catch (AggregateException ag) when (ag.InnerExceptions.Any(x => x is FormatException)) { Console.WriteLine("task wait: filtered " + ag.InnerExceptions.Count); }
});
Run("lambda in filter and handler", () =>
{
    var seen = new List<string>();
    try { throw new FormatException(); }
    catch (Exception e) when (Log(seen, () => "filter") && e is FormatException) { Log(seen, () => "handler"); }
    Console.WriteLine("lambda in filter and handler: " + string.Join(",", seen));
});
static bool Log(List<string> sink, Func<string> text) { sink.Add(text()); return true; }
