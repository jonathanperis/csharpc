// ConcurrentQueue, ConcurrentStack and ConcurrentBag: order, snapshots, ranges, interfaces and
// exceptions, used from the main thread and from threads that each run alone.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// ConcurrentQueue: FIFO; enumeration is a snapshot.
var queue = new ConcurrentQueue<int>(Enumerable.Range(1, 5));
queue.Enqueue(6);
Console.WriteLine($"{queue.Count} {queue.IsEmpty} {queue.TryPeek(out var head)} {head}");
var snapshot = queue.GetEnumerator();
queue.TryDequeue(out var first);
queue.Enqueue(7);
var seen = new List<int>();
while (snapshot.MoveNext()) seen.Add(snapshot.Current);
Console.WriteLine($"{first} | {string.Join(",", seen)} | {string.Join(",", queue)} | {string.Join(",", queue.ToArray())}");
for (var i = 0; i < 40; i++) queue.Enqueue(100 + i);
while (queue.Count > 3) queue.TryDequeue(out _);
Console.WriteLine(string.Join(",", queue));
var target = new int[6];
queue.CopyTo(target, 2);
Console.WriteLine(string.Join(",", target));
try { queue.CopyTo(target, 4); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { queue.CopyTo(target, -1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
queue.Clear();
Console.WriteLine($"{queue.IsEmpty} {queue.TryDequeue(out var none)} {none} {queue.TryPeek(out _)}");
IProducerConsumerCollection<string> pcc = new ConcurrentQueue<string>();
pcc.TryAdd("a");
pcc.TryAdd("b");
Console.WriteLine($"{pcc.TryTake(out var taken)} {taken} {pcc.Count}");
var objects = new object[3];
((ICollection)pcc).CopyTo(objects, 1);
Console.WriteLine(string.Join(",", objects.Select(o => o ?? "null")));
try { _ = ((ICollection)pcc).SyncRoot; } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
try { _ = new ConcurrentQueue<int>(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

// ConcurrentStack: LIFO; ranges.
var stack = new ConcurrentStack<string>(new[] { "a", "b", "c" });
stack.Push("d");
stack.PushRange(new[] { "e", "f", "g" });
stack.PushRange(new[] { "x", "h", "i", "y" }, 1, 2);
Console.WriteLine($"{stack.Count} {string.Join("", stack)} {string.Join("", stack.ToArray())}");
var popped = new string[5];
Console.WriteLine($"{stack.TryPopRange(popped, 1, 3)} {string.Join(",", popped.Select(p => p ?? "-"))}");
Console.WriteLine($"{stack.TryPop(out var top)} {top} {stack.TryPeek(out var peek)} {peek} {stack.Count}");
var all = new string[10];
Console.WriteLine($"{stack.TryPopRange(all)} {string.Join("", all.Where(s => s != null))} {stack.IsEmpty} {stack.TryPop(out _)}");
var empty = new ConcurrentStack<int>();
Console.WriteLine($"{empty.TryPopRange(new int[2])} {empty.ToArray().Length} {empty.Count}");
try { stack.PushRange(new[] { "a" }, 1, 1); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { stack.PushRange(new[] { "a" }, -1, 1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { stack.PushRange(new[] { "a" }, 2, 0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { stack.PushRange(new[] { "a" }, 0, -2); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
var numbers = new ConcurrentStack<int>();
numbers.PushRange(Enumerable.Range(0, 10).ToArray());
var copy = new int[12];
numbers.CopyTo(copy, 1);
Console.WriteLine(string.Join(",", copy));
numbers.Clear();
Console.WriteLine(numbers.IsEmpty);

// ConcurrentBag: each thread's items LIFO; other threads' items are stolen oldest first.
var bag = new ConcurrentBag<int>(new[] { 1, 2, 3 });
bag.Add(4);
Console.WriteLine($"{bag.Count} {string.Join(",", bag)} {string.Join(",", bag.ToArray())}");
Console.WriteLine($"{bag.TryTake(out var t1)} {t1} {bag.TryPeek(out var p1)} {p1}");
RunAlone(() => { bag.Add(10); bag.Add(11); });
Console.WriteLine($"{bag.Count} {string.Join(",", bag)}");
var order = new List<int>();
while (bag.TryTake(out var item)) order.Add(item);
Console.WriteLine(string.Join(",", order) + " " + bag.IsEmpty);
RunAlone(() => { bag.Add(20); bag.Add(21); bag.Add(22); });
Console.WriteLine($"{bag.TryPeek(out var stolen)} {stolen} {bag.TryTake(out stolen)} {stolen} {string.Join(",", bag)}");
var bagCopy = new int[4];
bag.CopyTo(bagCopy, 1);
Console.WriteLine(string.Join(",", bagCopy));
try { bag.CopyTo(bagCopy, 3); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
bag.Clear();
Console.WriteLine($"{bag.Count} {bag.IsEmpty} {bag.TryTake(out _)}");
new ConcurrentBag<int>().CopyTo(new int[1], 5); // nothing was ever added: no check
RunAlone(() => Console.WriteLine($"other thread: {bag.TryTake(out var none)} {bag.IsEmpty}"));
Console.WriteLine("done");

static void RunAlone(Action action)
{
    var thread = new Thread(() => action());
    thread.Start();
    thread.Join();
}
