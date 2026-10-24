// WeakReference, WeakReference<T> and ConditionalWeakTable: targets that are still referenced survive
// collections, and a table's values live as long as their keys (even when nothing else references the
// value). Whether an unreferenced target is collected is up to the collector, so it isn't printed.
using System.Runtime.CompilerServices;

var node = new Node("root");
var weak = new WeakReference<Node>(node);
var untyped = new WeakReference(node, trackResurrection: true);
GC.Collect();
Console.WriteLine($"{weak.TryGetTarget(out var target)} {target?.Name} {untyped.IsAlive} {((Node)untyped.Target!).Name} {untyped.TrackResurrection}");
weak.SetTarget(new Node("other"));
Console.WriteLine(weak.TryGetTarget(out target) ? target.Name : "none");
untyped.Target = null;
Console.WriteLine($"{untyped.IsAlive} {untyped.Target is null}");
var empty = new WeakReference<Node>(null!);
Console.WriteLine(empty.TryGetTarget(out _));
var literal = new WeakReference<string>("literal");
GC.Collect();
Console.WriteLine(literal.TryGetTarget(out var text) ? text : "gone");

var annotations = new ConditionalWeakTable<Node, Annotation>();
var keys = Enumerable.Range(0, 20).Select(i => new Node("n" + i)).ToList();
foreach (var key in keys) annotations.Add(key, new Annotation(key.Name.Length * 10));
GC.Collect();
GC.Collect();
// The annotations are referenced only by the table: their keys keep them alive.
Console.WriteLine(string.Join(",", keys.Take(5).Select(k => annotations.TryGetValue(k, out var a) ? a.Value.ToString() : "-")));
Console.WriteLine(annotations.Count());

try { annotations.Add(keys[0], new Annotation(0)); }
catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
Console.WriteLine($"{annotations.TryAdd(keys[0], new Annotation(1))} {annotations.TryAdd(node, new Annotation(2))}");
annotations.AddOrUpdate(keys[1], new Annotation(99));
Console.WriteLine(annotations.TryGetValue(keys[1], out var updated) ? updated.Value : -1);
Console.WriteLine($"{annotations.Remove(keys[2])} {annotations.Remove(keys[2])} {annotations.TryGetValue(keys[2], out _)}");
Console.WriteLine(annotations.Remove(keys[3], out var removed) ? removed.Value : -1);
Console.WriteLine(annotations.GetValue(keys[2], k => new Annotation(k.Name.Length)).Value);
Console.WriteLine(annotations.GetValue(keys[2], k => new Annotation(-1)).Value);
Console.WriteLine(annotations.GetOrCreateValue(new Node("fresh")).Value);
Console.WriteLine(annotations.GetOrAdd(keys[4], new Annotation(7)).Value);
Console.WriteLine(annotations.GetOrAdd(keys[3], k => new Annotation(k.Name.Length + 100)).Value);
Console.WriteLine(annotations.GetOrAdd(keys[3], (k, extra) => new Annotation(extra), 5).Value);
Console.WriteLine(string.Join(" ", annotations.Take(6).Select(p => $"{p.Key.Name}={p.Value.Value}")));
annotations.Clear();
Console.WriteLine($"{annotations.Count()} {annotations.TryGetValue(keys[0], out _)}");

// Distinct keys that are equal by value are still distinct keys (identity, not Equals).
var byIdentity = new ConditionalWeakTable<string, Annotation>();
var a1 = new string('x', 3);
var a2 = new string('x', 3);
byIdentity.Add(a1, new Annotation(1));
byIdentity.Add(a2, new Annotation(2));
Console.WriteLine($"{byIdentity.TryGetValue(a1, out var v1) && v1.Value == 1} {byIdentity.TryGetValue(a2, out var v2) && v2.Value == 2} {byIdentity.TryGetValue("xxx", out _)}");
try { byIdentity.TryGetValue(null!, out _); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { byIdentity.GetValue(a1, null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

// Values that reference their own key don't keep the entry alive in .NET; they still work as values.
var cyclic = new ConditionalWeakTable<Node, Holder>();
foreach (var key in keys) cyclic.Add(key, new Holder(key));
GC.Collect();
Console.WriteLine(cyclic.TryGetValue(keys[19], out var holder) ? holder.Owner.Name : "none");
GC.KeepAlive(keys);
GC.KeepAlive(node);

sealed class Node(string name) { public string Name => name; }
sealed class Annotation
{
    public Annotation() : this(42) { }
    public Annotation(int value) => Value = value;
    public int Value { get; }
}
sealed class Holder(Node owner) { public Node Owner => owner; }
