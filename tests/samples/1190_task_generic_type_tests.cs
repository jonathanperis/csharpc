// A Task<T> is told from a Task<U> at run time: type tests (`is`, `as`, patterns, switch), casts and their
// InvalidCastException message, GetType()/typeof for tasks whose type is a plain Task<T> in .NET (FromResult, a
// TaskCompletionSource<T>, Task.Run<T>, new Task<T>), and the IList.Add(object) call site over a List<Task<string>>
// that stopped the compiler ("type test against Task<string>") because List<T>'s IList slots test `value is T`.
// Tasks made by async methods, WhenAll, ContinueWith and the like are tested with `is` only: their run-time classes
// are .NET-internal types (AsyncStateMachineBox, ...) that derive from Task<T>, whose names aren't printed here.
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

static string Kind(object? o) => o switch
{
    null => "null",
    Task<int> => "Task<int>",
    Task<string> => "Task<string>",
    Task<long> => "Task<long>",
    Task<bool> => "Task<bool>",
    Task<object> => "Task<object>",
    Task<int[]> => "Task<int[]>",
    Task<Task<int>> => "Task<Task<int>>",
    Task<List<string>> => "Task<List<string>>",
    Task => "Task",
    _ => "not a task: " + o.GetType().Name,
};

static void Tests(string label, object task)
{
    Console.WriteLine($"{label}: Task {task is Task}, Task<int> {task is Task<int>}, Task<string> {task is Task<string>}, Task<long> {task is Task<long>}, Task<object> {task is Task<object>}, kind {Kind(task)}");
}

Tests("FromResult(1)", Task.FromResult(1));
Tests("FromResult(\"s\")", Task.FromResult("s"));
Tests("FromResult(1L)", Task.FromResult(1L));
Tests("FromResult<object>", Task.FromResult<object>("s"));
Tests("FromResult(true)", Task.FromResult(true));
Tests("FromResult(int[])", Task.FromResult(new[] { 1, 2 }));
Tests("FromResult(Task<int>)", Task.FromResult(Task.FromResult(3)));
Tests("FromResult(List<string>)", Task.FromResult(new List<string>()));
Tests("CompletedTask", Task.CompletedTask);
Tests("Task.Run(Func<int>)", Task.Run(() => 5));
Tests("Task.Run(Func<string>)", Task.Run(() => "five"));
Tests("Task.Run(Func<Task<string>>)", Task.Run(() => Task.FromResult("six")));
Tests("Task.Run(Action)", Task.Run(() => { }));
Tests("new TaskCompletionSource<int>().Task", new TaskCompletionSource<int>().Task);
Tests("new TaskCompletionSource<string>().Task", new TaskCompletionSource<string>().Task);
Tests("new Task<int>(...)", new Task<int>(() => 1));
Tests("FromException<int>", Task.FromException<int>(new InvalidOperationException()));
Tests("FromCanceled<string>", Task.FromCanceled<string>(new CancellationToken(true)));

async Task<int> AsyncInt() { await Task.Yield(); return 1; }
async Task<string> AsyncString() { await Task.Yield(); return "a"; }
async Task AsyncVoid() { await Task.Yield(); }
async ValueTask<int> AsyncValueInt() { await Task.Yield(); return 1; }
Tests("async Task<int>", AsyncInt());
Tests("async Task<string>", AsyncString());
Tests("async Task<int> (completed synchronously)", ((Func<Task<int>>)(async () => 9))());
Tests("async ValueTask<int>.AsTask()", AsyncValueInt().AsTask());
Console.WriteLine($"async Task: Task {AsyncVoid() is Task}, Task<int> {(object)AsyncVoid() is Task<int>}");
Tests("WhenAll(Task<int>[])", Task.WhenAll(new[] { Task.FromResult(1), Task.FromResult(2) }));
Tests("WhenAll(Task<string>[])", Task.WhenAll(new[] { Task.FromResult("a") }));
Tests("WhenAny(Task<int>, Task<int>)", Task.WhenAny(Task.FromResult(1), Task.FromResult(2)));
Tests("ContinueWith<string>", Task.FromResult(1).ContinueWith(t => "x"));
Tests("ContinueWith<int>", Task.FromResult("a").ContinueWith(t => t.Result.Length));

// as, is patterns, casts
object boxed = Task.FromResult("text");
Console.WriteLine($"as Task<string>: {(boxed as Task<string>)?.Result}; as Task<int>: {(boxed as Task<int>)?.ToString() ?? "null"}");
if (boxed is Task<string> typed) Console.WriteLine($"pattern: {typed.Result}");
Console.WriteLine($"cast: {((Task<string>)boxed).Result}");
Console.WriteLine($"is not: {boxed is not Task<int>}");
try { _ = (Task<int>)boxed; } catch (InvalidCastException e) { Console.WriteLine(e.Message); }
try { _ = (Task<object>)boxed; } catch (InvalidCastException e) { Console.WriteLine(e.Message); }
object nothing = null!;
Console.WriteLine($"null: {nothing is Task<int>}, {(Task<int>?)nothing is null}");
object notTask = "string";
try { _ = (Task<string>)notTask; } catch (InvalidCastException e) { Console.WriteLine(e.Message); }

// GetType / typeof
object[] plain = { Task.FromResult(1), Task.FromResult("s"), Task.FromResult<object>(null!), new TaskCompletionSource<long>().Task, Task.Run(() => 1.5) };
foreach (var t in plain) Console.WriteLine($"GetType: {t.GetType()}; ToString: {t}");
Console.WriteLine($"typeof(Task<int>) == GetType: {typeof(Task<int>) == plain[0].GetType()}, typeof(Task<string>) == GetType: {typeof(Task<string>) == plain[0].GetType()}");
Console.WriteLine($"typeof(Task<int>): {typeof(Task<int>)}, generic definition {typeof(Task<int>).GetGenericTypeDefinition()}, argument {typeof(Task<int>).GetGenericArguments()[0]}");
Console.WriteLine($"base type of Task<int>: {typeof(Task<int>).BaseType}, assignable to Task: {typeof(Task).IsAssignableFrom(typeof(Task<int>))}, instance: {typeof(Task<int>).IsInstanceOfType(plain[0])}, {typeof(Task<string>).IsInstanceOfType(plain[0])}");

// A generic helper: T is the task's result type, by instantiation.
static bool IsTaskOf<T>(object o) => o is Task<T>;
Console.WriteLine($"IsTaskOf<int>: {IsTaskOf<int>(plain[0])}, <string>: {IsTaskOf<string>(plain[0])}, <string> of string task: {IsTaskOf<string>(plain[1])}, <object>: {IsTaskOf<object>(plain[1])}");

// A task as an IAsyncResult / IDisposable keeps working
object asObject = Task.FromResult(7);
Console.WriteLine($"IAsyncResult: {asObject is IAsyncResult}, completed {((IAsyncResult)asObject).IsCompleted}, IDisposable: {asObject is IDisposable}");

// Collections of tasks through the non-generic interfaces (the case that failed to compile)
IList list = new List<Task<string>>();
list.Add(Task.FromResult("one"));
list.Add(Task.FromResult("two"));
Console.WriteLine($"IList: Count {list.Count}, Contains {list.Contains(list[0])}, IndexOf {list.IndexOf(list[1])}");
try { list.Add(Task.FromResult(3)); } catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { list.Add("not a task"); } catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
try { list[0] = Task.FromResult(4L); } catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
list.Add(null);
Console.WriteLine($"IList after null: Count {list.Count}");
foreach (Task<string>? t in list) Console.WriteLine($"  item: {t?.Result ?? "null"}");
IList other = new List<Task<int>> { Task.FromResult(1) };
other.Add(Task.FromResult(2));
Console.WriteLine($"IList<Task<int>>: {other.Count}");
ICollection collection = new List<Task<long>> { Task.FromResult(1L) };
Console.WriteLine($"ICollection: {collection.Count}");
IEnumerable enumerable = new Task<bool>[] { Task.FromResult(true) };
foreach (var item in enumerable) Console.WriteLine($"  array item: {Kind(item)}");
var dictionary = new Dictionary<string, Task<int>> { ["a"] = Task.FromResult(1) };
IDictionary nonGeneric = dictionary;
nonGeneric["b"] = Task.FromResult(2);
try { nonGeneric["c"] = Task.FromResult("x"); } catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
Console.WriteLine($"IDictionary: {nonGeneric.Count}");
object[] mixed = { Task.FromResult(1), Task.FromResult("a"), 3, null! };
foreach (var m in mixed) Console.WriteLine($"  mixed: {Kind(m)}");

// await still works on every kind
Console.WriteLine($"awaited: {await Task.FromResult(1)} {await Task.Run(() => "r")} {await AsyncInt()} {await AsyncValueInt()}");
Task all = Task.WhenAll(Task.FromResult(1), Task.FromResult("a"));
await all;
Console.WriteLine($"WhenAll(Task, Task) is Task<int>: {(object)all is Task<int>}, status {all.Status}");
