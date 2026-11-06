// What the plain Parallel.For / ForEach / Invoke throw: an AggregateException around the body's exception (and
// around the inner one for nested loops), and Invoke's argument checks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

static void Try(string label, Action a)
{
    try { a(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}" + (e is AggregateException ag ? " [" + string.Join("; ", ag.InnerExceptions.Select(x => x.GetType().Name + ":" + x.Message)) + "]" : "")); }
}
Try("For throws", () => Parallel.For(0, 5, i => { if (i == 2) throw new InvalidOperationException("two"); }));
Try("For long throws", () => Parallel.For(0L, 5L, i => { if (i == 2) throw new InvalidOperationException("two"); }));
Try("ForEach array throws", () => Parallel.ForEach(new[] { 1, 2, 3 }, x => { if (x == 2) throw new InvalidOperationException("two"); }));
Try("ForEach list throws", () => Parallel.ForEach(new List<int> { 1, 2, 3 }, x => { if (x == 2) throw new InvalidOperationException("two"); }));
Try("ForEach enumerable throws", () => Parallel.ForEach(Enumerable.Range(0, 3).Select(x => x), x => { if (x == 2) throw new InvalidOperationException("two"); }));
Try("Invoke throws", () => Parallel.Invoke(() => { }, () => throw new InvalidOperationException("a")));
Try("Invoke 1 throws", () => Parallel.Invoke(() => throw new InvalidOperationException("only")));
Try("Invoke null elem", () => Parallel.Invoke(() => { }, null));
Try("Invoke null", () => Parallel.Invoke((Action[])null));
Try("For nested exception", () => Parallel.For(0, 1, i => Parallel.For(0, 1, j => throw new InvalidOperationException("n" + i + j))));
