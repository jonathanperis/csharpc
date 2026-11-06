// BlockingCollection<T> without threads: Add/TryAdd/Take/TryTake with every timeout and token form, bounded
// capacity, CompleteAdding, the consuming enumerable, snapshots (enumeration, ToArray, CopyTo), the backing
// collections, AddToAny / TakeFromAny / TryTakeFromAny over several collections, Dispose, and the checks and
// messages of each.
#pragma warning disable CS8600, CS8602, CS8604, CS8625, CS8605
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

static void Try(string label, Action a)
{
    try { a(); Console.WriteLine(label + ": ok"); }
    catch (Exception e) { Console.WriteLine($"{label}: {e.GetType().Name}: {e.Message}"); }
}

var bc = new BlockingCollection<int>();
Console.WriteLine($"{bc.Count} {bc.BoundedCapacity} {bc.IsAddingCompleted} {bc.IsCompleted}");
bc.Add(1); bc.Add(2); bc.TryAdd(3);
Console.WriteLine(bc.Count);
Console.WriteLine(bc.Take() + " " + bc.TryTake(out var x) + x);
Console.WriteLine(string.Join(",", bc.ToArray()));
Console.WriteLine(bc.TryTake(out x, 10) + " " + x);
Console.WriteLine(bc.TryTake(out x, TimeSpan.FromMilliseconds(10)) + " " + x);
Console.WriteLine(bc.TryTake(out x));
bc.CompleteAdding();
Console.WriteLine($"{bc.IsAddingCompleted} {bc.IsCompleted}");
Try("add after complete", () => bc.Add(5));
Try("tryadd after complete", () => bc.TryAdd(5));
Try("take after complete", () => bc.Take());
Try("trytake after complete", () => Console.WriteLine(bc.TryTake(out x, 100)));
bc.CompleteAdding(); // twice ok

var bounded = new BlockingCollection<string>(2);
Console.WriteLine(bounded.BoundedCapacity);
Console.WriteLine(bounded.TryAdd("a") + " " + bounded.TryAdd("b") + " " + bounded.TryAdd("c") + " " + bounded.TryAdd("d", 10) + " " + bounded.TryAdd("e", TimeSpan.FromMilliseconds(10)));
Console.WriteLine(bounded.Count);
Try("bad capacity 0", () => new BlockingCollection<int>(0));
Try("bad capacity -1", () => new BlockingCollection<int>(-1));
Try("capacity coll too big", () => new BlockingCollection<int>(new ConcurrentQueue<int>(new[] { 1, 2, 3 }), 2));
Try("null coll", () => new BlockingCollection<int>(null));
Try("null coll 2", () => new BlockingCollection<int>(null, 5));
Try("tryadd -2", () => bounded.TryAdd("x", -2));
Try("trytake -2", () => bounded.TryTake(out var s, -2));
Try("trytake huge timeout", () => bounded.TryTake(out var s, TimeSpan.FromMilliseconds(-2)));
Try("trytake too large timeout", () => bounded.TryTake(out var s, TimeSpan.FromMilliseconds((long)int.MaxValue + 1)));

// cancellation (the token is canceled before the call)
var cts = new CancellationTokenSource();
var empty = new BlockingCollection<int>();
cts.Cancel();
Try("take canceled", () => empty.Take(cts.Token));
Try("trytake canceled", () => empty.TryTake(out var v, 100, cts.Token));
Try("add canceled", () => empty.Add(1, cts.Token));
Try("tryadd canceled", () => empty.TryAdd(1, 100, cts.Token));
Try("enumerate canceled", () => { foreach (var v in empty.GetConsumingEnumerable(cts.Token)) { } });

// consuming enumerable on a completed collection
var src = new BlockingCollection<int>();
for (int i = 0; i < 5; i++) src.Add(i);
src.CompleteAdding();
Console.WriteLine(string.Join(",", src.GetConsumingEnumerable()));
Console.WriteLine(src.Count + " " + src.IsCompleted);
var left = new BlockingCollection<int>(); left.Add(1); left.Add(2); left.Add(3); left.CompleteAdding();
foreach (var v in left.GetConsumingEnumerable()) { Console.WriteLine("took " + v + ", " + left.Count + " left"); if (v == 2) break; }
Console.WriteLine(left.IsCompleted + " " + left.Count);
var q2 = new BlockingCollection<int>(1); q2.Add(1); q2.CompleteAdding();
Console.WriteLine(q2.Take() + " " + q2.IsCompleted + " " + q2.IsAddingCompleted);
var plain = new BlockingCollection<int>();
plain.Add(1); plain.Add(2);
Console.WriteLine(string.Join(",", plain)); // enumerates snapshot, doesn't remove
Console.WriteLine(plain.Count);
Console.WriteLine(((IEnumerable)plain).GetEnumerator().GetType().Name.Length > 0);
Console.WriteLine(((ICollection)plain).IsSynchronized);
Try("syncroot", () => { var o = ((ICollection)plain).SyncRoot; });
var arr = new int[4];
plain.CopyTo(arr, 1);
Console.WriteLine(string.Join(",", arr));
Try("copyto null", () => plain.CopyTo(null, 0));
Try("copyto small", () => plain.CopyTo(new int[1], 0));
Try("copyto neg", () => plain.CopyTo(new int[3], -1));
var arr2 = new int[4];
((ICollection)plain).CopyTo(arr2, 0);
Console.WriteLine(string.Join(",", arr2));

// backing collections
var stack = new BlockingCollection<int>(new ConcurrentStack<int>());
stack.Add(1); stack.Add(2); stack.Add(3);
Console.WriteLine(stack.Take() + "," + stack.Take() + "," + stack.Take());
var bag = new BlockingCollection<int>(new ConcurrentBag<int>(), 3);
bag.Add(1); bag.Add(2);
Console.WriteLine(bag.Count + " " + bag.BoundedCapacity);
var prefilled = new BlockingCollection<int>(new ConcurrentQueue<int>(new[] { 7, 8, 9 }));
Console.WriteLine(prefilled.Count + " " + prefilled.Take());
var prefilledBounded = new BlockingCollection<int>(new ConcurrentQueue<int>(new[] { 7, 8 }), 2);
Console.WriteLine(prefilledBounded.TryAdd(1) + " " + prefilledBounded.Count);

// across collections
var c1 = new BlockingCollection<int>(); var c2 = new BlockingCollection<int>(1);
var both = new[] { c1, c2 };
int idx = BlockingCollection<int>.TryAddToAny(both, 10); Console.WriteLine(idx);
idx = BlockingCollection<int>.TryAddToAny(both, 11); Console.WriteLine(idx);
Console.WriteLine(BlockingCollection<int>.TryAddToAny(both, 12, 10) + "");
Console.WriteLine(BlockingCollection<int>.AddToAny(both, 13) + "");
Console.WriteLine(BlockingCollection<int>.TryTakeFromAny(both, out var taken) + " " + taken);
Console.WriteLine(BlockingCollection<int>.TakeFromAny(both, out taken) + " " + taken);
Console.WriteLine(BlockingCollection<int>.TakeFromAny(both, out taken) + " " + taken);
Console.WriteLine(BlockingCollection<int>.TryTakeFromAny(both, out taken, 10) + " " + taken);
Console.WriteLine(BlockingCollection<int>.TryTakeFromAny(both, out taken, TimeSpan.FromMilliseconds(10)) + " " + taken);
Try("addtoany null", () => BlockingCollection<int>.AddToAny(null, 1));
Try("addtoany empty", () => BlockingCollection<int>.AddToAny(new BlockingCollection<int>[0], 1));
Try("addtoany null elem", () => BlockingCollection<int>.AddToAny(new BlockingCollection<int>[] { null }, 1));
Try("takefromany null", () => BlockingCollection<int>.TakeFromAny(null, out taken));
Try("takefromany disposed", () => { var d = new BlockingCollection<int>(); d.Dispose(); BlockingCollection<int>.TakeFromAny(new[] { d }, out taken); });
var completed = new BlockingCollection<int>(); completed.CompleteAdding();
Try("addtoany completed", () => BlockingCollection<int>.AddToAny(new[] { completed }, 1));
Try("takefromany completed", () => BlockingCollection<int>.TakeFromAny(new[] { completed }, out taken));
Try("trytakefromany completed", () => Console.WriteLine(BlockingCollection<int>.TryTakeFromAny(new[] { completed }, out taken, 20)));

// dispose (the exception messages name the type with its assembly version: only the types are printed)
var disposed = new BlockingCollection<int>();
disposed.Add(1);
disposed.Dispose();
static void Kind(string label, Action action) { try { action(); Console.WriteLine(label + ": ok"); } catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().Name); } }
Kind("add disposed", () => disposed.Add(1));
Kind("take disposed", () => disposed.Take());
Kind("count disposed", () => { _ = disposed.Count; });
Kind("toarray disposed", () => disposed.ToArray());
Kind("complete disposed", () => disposed.CompleteAdding());
Kind("enumerate disposed", () => { foreach (var v in disposed) { } });
Kind("capacity disposed", () => { _ = disposed.BoundedCapacity; });
Kind("iscompleted disposed", () => { _ = disposed.IsCompleted; });
Kind("isaddingcompleted disposed", () => { _ = disposed.IsAddingCompleted; });
disposed.Dispose();

// timeouts
var slow = new BlockingCollection<int>();
Console.WriteLine(slow.TryTake(out x, 30));
Console.WriteLine(slow.TryTake(out x, TimeSpan.Zero));
Console.WriteLine(slow.TryTake(out x, 1));

// IProducerConsumerCollection edge: TryAdd failing backing collection
var weird = new BlockingCollection<int>(new RejectingCollection());
Try("weird add", () => weird.Add(1));
Try("weird tryadd", () => Console.WriteLine(weird.TryAdd(2)));
Console.WriteLine(weird.Count);
class RejectingCollection : IProducerConsumerCollection<int>
{
    public int Count => 0;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public void CopyTo(int[] array, int index) { }
    public void CopyTo(Array array, int index) { }
    public IEnumerator<int> GetEnumerator() { yield break; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int[] ToArray() => new int[0];
    public bool TryAdd(int item) => false;
    public bool TryTake(out int item) { item = 0; return false; }
}