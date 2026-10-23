// ReadOnlyCollection<T> (a live read-only view: List.AsReadOnly, Array.AsReadOnly, Empty) and
// ThreadLocal<T> (per-thread values, lazy factory, Values with trackAllValues, disposal).
using System.Collections.ObjectModel;
var list = new List<int> { 3, 1, 2 };
ReadOnlyCollection<int> ro = list.AsReadOnly();
Console.WriteLine(ro.Count + " " + ro[1] + " " + ro.Contains(2) + " " + ro.IndexOf(2) + " " + string.Join(",", ro));
list.Add(9);
Console.WriteLine(ro.Count + " " + string.Join(",", ro));
try { ((IList<int>)ro).Add(1); } catch (NotSupportedException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
try { ((ICollection<int>)ro).Clear(); } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
try { ((IList<int>)ro)[0] = 5; } catch (NotSupportedException e) { Console.WriteLine(e.Message); }
try { _ = new ReadOnlyCollection<int>(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
Console.WriteLine(((ICollection<int>)ro).IsReadOnly + " " + ReadOnlyCollection<int>.Empty.Count);
var arr = Array.AsReadOnly(new[] { "a", "b" });
Console.WriteLine(arr.Count + " " + arr[1] + " " + arr.GetType().Name);
var copy = new int[6]; ro.CopyTo(copy, 1); Console.WriteLine(string.Join(",", copy));

var tl = new ThreadLocal<int>(() => Environment.CurrentManagedThreadId * 0 + 7, trackAllValues: true);
Console.WriteLine(tl.IsValueCreated + " " + tl.Value + " " + tl.IsValueCreated);
tl.Value = 10;
var t = new Thread(() => { Console.WriteLine("thread sees " + tl.Value); tl.Value = 20; });
t.Start(); t.Join();
Console.WriteLine("main sees " + tl.Value + " values " + string.Join(",", tl.Values.OrderBy(v => v)) + " " + tl);
var untracked = new ThreadLocal<string>(() => "x");
try { _ = untracked.Values; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
var noFactory = new ThreadLocal<int>();
Console.WriteLine(noFactory.Value + " " + noFactory.IsValueCreated);
tl.Dispose();
try { _ = tl.Value; } catch (ObjectDisposedException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
