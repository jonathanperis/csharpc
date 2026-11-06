// Partitioner.Create over ranges (int and long, with a range size), arrays, lists and enumerables (static and
// load-balanced partitions, EnumerablePartitionerOptions), OrderablePartitioner keys and flags, user partitioners
// (Partitioner<T> and OrderablePartitioner<T>), dynamic partitions shared by several enumerators, and their use
// by Parallel.ForEach (with and without index/loop state) and PLINQ's AsParallel.
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

static string Drain<T>(IEnumerator<T> e) { var l = new List<string>(); while (e.MoveNext()) l.Add(e.Current.ToString()); return string.Join(",", l); }
static string DrainWith<T>(IEnumerator<T> e, Func<T, string> f) { var l = new List<string>(); while (e.MoveNext()) l.Add(f(e.Current)); return string.Join(" ", l); }
static string DrainKeyed<T>(IEnumerator<KeyValuePair<long, T>> e) { var l = new List<string>(); while (e.MoveNext()) l.Add(e.Current.Key + ":" + e.Current.Value); return string.Join(",", l); }

var data = Enumerable.Range(0, 10).ToArray();
var list = data.ToList();

Console.WriteLine("-- range partitioner");
foreach (var r in Partitioner.Create(0, 10, 2).GetPartitions(3).Select(p => DrainWith(p, t => $"[{t.Item1},{t.Item2})"))) Console.WriteLine(r);
foreach (var r in Partitioner.Create(0, 10, 4).GetPartitions(2).Select(p => DrainWith(p, t => $"[{t.Item1},{t.Item2})"))) Console.WriteLine(r);
Console.WriteLine(string.Join(" ", Partitioner.Create(0, 10, 3).GetDynamicPartitions().Select(t => $"[{t.Item1},{t.Item2})")));
Console.WriteLine(string.Join(" ", Partitioner.Create(0L, 10L, 3L).GetDynamicPartitions().Select(t => $"[{t.Item1},{t.Item2})")));
Console.WriteLine(string.Join(" ", Partitioner.Create(0L, 7L, 2L).GetDynamicPartitions().Select(t => $"[{t.Item1},{t.Item2})")));
Try("range equal", () => Partitioner.Create(5, 5));
var rp = Partitioner.Create(0, 100, 11); // (the default range size depends on Environment.ProcessorCount)
Console.WriteLine(rp.SupportsDynamicPartitions + " " + rp.GetType().Name + " " + rp.KeysOrderedInEachPartition + " " + rp.KeysOrderedAcrossPartitions + " " + rp.KeysNormalized);
Console.WriteLine(string.Join(" ", rp.GetOrderableDynamicPartitions().Take(3).Select(kv => $"{kv.Key}:[{kv.Value.Item1},{kv.Value.Item2})")));
Try("range bad", () => Partitioner.Create(5, 4));
Try("range bad size", () => Partitioner.Create(0, 4, 0));
Try("range bad size neg", () => Partitioner.Create(0L, 4L, -1L));
Try("range partitions 0", () => Partitioner.Create(0, 4).GetPartitions(0));
Try("range long bad", () => Partitioner.Create(5L, 4L));
Try("range huge", () => Console.WriteLine(Partitioner.Create(int.MinValue, int.MaxValue, 1 << 30).GetDynamicPartitions().Count()));
Try("range tail", () => Console.WriteLine(DrainWith(Partitioner.Create(0, 10, 7).GetPartitions(1)[0], t => $"[{t.Item1},{t.Item2})")));

Console.WriteLine("-- array / list static (no load balance)");
foreach (var p in Partitioner.Create(data, false).GetPartitions(3)) Console.WriteLine(Drain(p));
foreach (var p in Partitioner.Create(list, false).GetPartitions(4)) Console.WriteLine(Drain(p));
var staticArr = Partitioner.Create(data, false);
Console.WriteLine(staticArr.SupportsDynamicPartitions + " " + staticArr.GetType().Name);
Try("static dynamic", () => staticArr.GetDynamicPartitions());
Try("orderable static", () => Console.WriteLine(DrainKeyed(((OrderablePartitioner<int>)staticArr).GetOrderablePartitions(2)[1])));
var op = (OrderablePartitioner<int>)staticArr;
Console.WriteLine(op.KeysOrderedInEachPartition + " " + op.KeysOrderedAcrossPartitions + " " + op.KeysNormalized);
Try("partitions 0", () => staticArr.GetPartitions(0));
Try("partitions -1", () => staticArr.GetPartitions(-1));
Try("create null arr", () => Partitioner.Create((int[])null, true));
Try("create null list", () => Partitioner.Create((IList<int>)null, true));
Try("create null enum", () => Partitioner.Create((IEnumerable<int>)null));
Try("empty partitions", () => Console.WriteLine(string.Join("|", Partitioner.Create(new int[0], false).GetPartitions(3).Select(p => Drain(p)))));
Try("more partitions than items", () => Console.WriteLine(string.Join("|", Partitioner.Create(new[] { 1, 2 }, false).GetPartitions(4).Select(p => Drain(p)))));

Console.WriteLine("-- array / list dynamic (load balance)");
var dyn = Partitioner.Create(data, true);
Console.WriteLine(dyn.SupportsDynamicPartitions + " " + dyn.GetType().Name);
Console.WriteLine(string.Join(",", dyn.GetDynamicPartitions()));
var dynOrd = (OrderablePartitioner<int>)dyn;
Console.WriteLine(dynOrd.KeysOrderedInEachPartition + " " + dynOrd.KeysOrderedAcrossPartitions + " " + dynOrd.KeysNormalized);
Console.WriteLine(DrainKeyed(dynOrd.GetOrderableDynamicPartitions().GetEnumerator()));
{
    var parts = dyn.GetPartitions(3);
    var seen = new ConcurrentBag<int>();
    Parallel.ForEach(parts, p => { while (p.MoveNext()) seen.Add(p.Current); });
    Console.WriteLine(string.Join(",", seen.OrderBy(x => x)));
}
{
    var parts = Partitioner.Create(list, true).GetPartitions(2);
    var all = new List<int>();
    foreach (var p in parts) while (p.MoveNext()) all.Add(p.Current);
    Console.WriteLine(all.Count + " " + all.Sum());
}
var shared = dyn.GetDynamicPartitions();
var en1 = shared.GetEnumerator(); var en2 = shared.GetEnumerator();
en1.MoveNext(); en2.MoveNext(); en1.MoveNext();
Console.WriteLine(en1.Current + " " + en2.Current);
Try("current before movenext", () => Console.WriteLine(shared.GetEnumerator().Current));
Try("dispose enumerator", () => { var e = shared.GetEnumerator(); e.Dispose(); e.MoveNext(); });
Try("reset", () => shared.GetEnumerator().Reset());

Console.WriteLine("-- enumerable");
var seq = Partitioner.Create(Enumerable.Range(100, 8));
Console.WriteLine(seq.SupportsDynamicPartitions + " " + seq.GetType().Name);
{
    var parts = seq.GetPartitions(2);
    var a = parts[0]; var b = parts[1];
    var got = new List<int>();
    a.MoveNext(); got.Add(a.Current); b.MoveNext(); got.Add(b.Current);
    while (a.MoveNext()) got.Add(a.Current);
    while (b.MoveNext()) got.Add(b.Current);
    Console.WriteLine(string.Join(",", got.OrderBy(x => x)));
}
Console.WriteLine(string.Join(",", Partitioner.Create(Enumerable.Range(0, 5)).GetDynamicPartitions().Select(x => x * 2)));
Console.WriteLine(string.Join(",", Partitioner.Create(Enumerable.Range(0, 5), EnumerablePartitionerOptions.NoBuffering).GetDynamicPartitions()));
Console.WriteLine(string.Join(",", ((OrderablePartitioner<int>)Partitioner.Create(Enumerable.Range(7, 3))).GetOrderableDynamicPartitions().Select(kv => kv.Key + "=" + kv.Value)));
var ordSeq = (OrderablePartitioner<int>)Partitioner.Create(Enumerable.Range(0, 3));
Console.WriteLine(ordSeq.KeysOrderedInEachPartition + " " + ordSeq.KeysOrderedAcrossPartitions + " " + ordSeq.KeysNormalized);
Try("bad options", () => Partitioner.Create(Enumerable.Range(0, 3), (EnumerablePartitionerOptions)8));
Try("enum partitions 0", () => Partitioner.Create(Enumerable.Range(0, 3)).GetPartitions(0));
Try("enum empty", () => Console.WriteLine(Partitioner.Create(new List<int>().Select(x => x)).GetDynamicPartitions().Count()));
Try("enum throws", () => Console.WriteLine(Partitioner.Create(Throwing()).GetDynamicPartitions().Count()));
static IEnumerable<int> Throwing() { yield return 1; throw new InvalidOperationException("source failed"); }
{
    int moves = 0;
    IEnumerable<int> Counting() { for (int i = 0; i < 20; i++) { Interlocked.Increment(ref moves); yield return i; } }
    var dynamicParts = Partitioner.Create(Counting()).GetDynamicPartitions();
    var e = dynamicParts.GetEnumerator();
    e.MoveNext();
    Console.WriteLine("buffered ahead: " + (moves > 1));
    var nb = Partitioner.Create(Counting(), EnumerablePartitionerOptions.NoBuffering).GetDynamicPartitions().GetEnumerator();
    int before = moves; nb.MoveNext();
    Console.WriteLine("nobuffering ahead: " + (moves - before));
}
{
    var disposed = new TrackedEnumerable(5);
    var parts = Partitioner.Create(disposed).GetPartitions(2);
    foreach (var p in parts) while (p.MoveNext()) { }
    foreach (var p in parts) p.Dispose();
    Console.WriteLine("source disposed: " + disposed.Disposed);
}

Console.WriteLine("-- Parallel.ForEach and PLINQ");
{
    var sum = 0L;
    Parallel.ForEach(Partitioner.Create(0, 1000, 100), range => { long local = 0; for (int i = range.Item1; i < range.Item2; i++) local += i; Interlocked.Add(ref sum, local); });
    Console.WriteLine(sum);
    var hits = new ConcurrentBag<int>();
    Parallel.ForEach(Partitioner.Create(data, true), x => hits.Add(x));
    Console.WriteLine(string.Join(",", hits.OrderBy(x => x)));
    var indexes = new ConcurrentDictionary<long, int>();
    Try("static orderable foreach", () => Parallel.ForEach(Partitioner.Create(list, false), (item, state, index) => indexes[index] = item));
    Parallel.ForEach(Partitioner.Create(list, true), (item, state, index) => indexes[index] = item);
    Console.WriteLine(string.Join(",", indexes.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
    var ids = new ConcurrentDictionary<long, int>();
    Parallel.ForEach(Partitioner.Create(Enumerable.Range(50, 6)), (item, state, index) => ids[index] = item);
    Console.WriteLine(string.Join(",", ids.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
    var stopped = 0;
    Parallel.ForEach(Partitioner.Create(0, 100), (range, state) => { Interlocked.Increment(ref stopped); state.Stop(); });
    Console.WriteLine(stopped >= 1);
    Console.WriteLine(Partitioner.Create(data, true).AsParallel().Select(x => x * x).Sum());
    Console.WriteLine(string.Join(",", Partitioner.Create(data, false).AsParallel().AsOrdered().Select(x => x + 1)));
    Console.WriteLine(Partitioner.Create(Enumerable.Range(0, 100)).AsParallel().Where(x => x % 7 == 0).Count());
    Try("static ForEach no dynamic", () => Parallel.ForEach(new NoDynamic(), x => { }));
    Try("ForEach null", () => Parallel.ForEach((Partitioner<int>)null, x => { }));
    Try("ForEach bad partitioner", () => Parallel.ForEach(new BadPartitioner(), x => { }));
    Try("ForEach throwing body", () => Parallel.ForEach(Partitioner.Create(data, true), x => { if (x == 3) throw new ArgumentException("bad item"); }));
}

Console.WriteLine("-- user partitioners");
Console.WriteLine(string.Join("|", new Halves().GetPartitions(2).Select(p => Drain(p))));
Console.WriteLine(new Halves().SupportsDynamicPartitions);
Try("user dynamic", () => new Halves().GetDynamicPartitions());
Try("user partitions 0", () => new Halves().GetPartitions(0));
var oh = new OrderedHalves();
Console.WriteLine(oh.KeysOrderedInEachPartition + " " + oh.KeysOrderedAcrossPartitions + " " + oh.KeysNormalized);
Console.WriteLine(string.Join("|", oh.GetPartitions(2).Select(p => Drain(p))));
Console.WriteLine(string.Join("|", oh.GetOrderablePartitions(2).Select(p => DrainKeyed(p))));
Try("orderable dynamic default", () => oh.GetOrderableDynamicPartitions());
Try("orderable dynamic default 2", () => oh.GetDynamicPartitions());
Try("orderable partitions null", () => new NullOrderable().GetPartitions(2));
Try("orderable partitions wrong count", () => new WrongCountOrderable().GetPartitions(2));
Try("orderable ctor flags", () => Console.WriteLine(new OrderedHalves2().KeysNormalized));
Try("orderable ctor bad", () => new BadFlags());
{
    var seen = new ConcurrentBag<int>();
    Try("user orderable", () => Parallel.ForEach(oh, x => seen.Add(x)));
    Console.WriteLine(string.Join(",", seen.OrderBy(x => x)));
}

class TrackedEnumerable : IEnumerable<int>
{
    private readonly int _n;
    public int Disposed;
    public TrackedEnumerable(int n) { _n = n; }
    public IEnumerator<int> GetEnumerator() => new E(this);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    class E : IEnumerator<int>
    {
        readonly TrackedEnumerable _o; int _i = -1;
        public E(TrackedEnumerable o) { _o = o; }
        public int Current => _i;
        object IEnumerator.Current => _i;
        public bool MoveNext() => ++_i < _o._n;
        public void Reset() { _i = -1; }
        public void Dispose() { Interlocked.Increment(ref _o.Disposed); }
    }
}

class NoDynamic : Partitioner<int>
{
    public override IList<IEnumerator<int>> GetPartitions(int partitionCount) => new[] { Enumerable.Range(0, 4).GetEnumerator() };
}
class BadPartitioner : Partitioner<int>
{
    public override IList<IEnumerator<int>> GetPartitions(int partitionCount) => null;
}
class Halves : Partitioner<int>
{
    public override IList<IEnumerator<int>> GetPartitions(int partitionCount)
    {
        if (partitionCount <= 0) throw new ArgumentOutOfRangeException("partitionCount");
        return new[] { Enumerable.Range(0, 3).GetEnumerator(), Enumerable.Range(3, 3).GetEnumerator() };
    }
}
class OrderedHalves : OrderablePartitioner<int>
{
    public OrderedHalves() : base(true, false, true) { }
    public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int partitionCount) =>
        new IEnumerator<KeyValuePair<long, int>>[]
        {
            Enumerable.Range(0, 3).Select(i => new KeyValuePair<long, int>(i, i)).GetEnumerator(),
            Enumerable.Range(3, 3).Select(i => new KeyValuePair<long, int>(i, i)).GetEnumerator(),
        };
}
class OrderedHalves2 : OrderablePartitioner<int>
{
    public OrderedHalves2() : base(false, true, false) { }
    public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int partitionCount) => null;
}
class BadFlags : OrderablePartitioner<int>
{
    public BadFlags() : base(false, true, true) { }
    public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int partitionCount) => null;
}
class NullOrderable : OrderablePartitioner<int>
{
    public NullOrderable() : base(true, true, true) { }
    public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int partitionCount) => null;
}
class WrongCountOrderable : OrderablePartitioner<int>
{
    public WrongCountOrderable() : base(true, true, true) { }
    public override IList<IEnumerator<KeyValuePair<long, int>>> GetOrderablePartitions(int partitionCount) => new IEnumerator<KeyValuePair<long, int>>[1];
}
