// Task.Run with a CancellationToken (and its Func<Task> unwrapping), Task.FromCanceled, and
// LazyInitializer.EnsureInitialized (Roslyn's RoslynLazyInitializer wraps it everywhere).
using var cts = new CancellationTokenSource();
var none = CancellationToken.None;

await Task.Run(() => Console.WriteLine("action ran"), none);
Console.WriteLine(await Task.Run(() => 6 * 7, none));
await Task.Run(async () => { await Task.Yield(); Console.WriteLine("async action ran"); }, none);
Console.WriteLine(await Task.Run(async () => { await Task.Yield(); return "async result"; }, none));

cts.Cancel();
var canceled = Task.Run(() => Console.WriteLine("never"), cts.Token);
try { await canceled; } catch (TaskCanceledException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
var canceledAsync = Task.Run(async () => { await Task.Yield(); return 2; }, cts.Token);
try { await canceledAsync; } catch (OperationCanceledException) { Console.WriteLine($"canceled async: {canceledAsync.IsCanceled}"); }

var faulted = Task.Run(async () => { await Task.Yield(); throw new InvalidOperationException("inner failed"); }, none);
try { await faulted; } catch (InvalidOperationException e) { Console.WriteLine($"{e.Message} {faulted.IsFaulted}"); }
var innerCanceled = Task.Run(() => Task.FromCanceled(cts.Token), none);
try { await innerCanceled; } catch (TaskCanceledException) { Console.WriteLine($"inner canceled: {innerCanceled.IsCanceled}"); }
var nullInner = Task.Run(() => (Task)null!, none);
try { await nullInner; } catch (TaskCanceledException) { Console.WriteLine($"null inner: {nullInner.IsCanceled}"); }

var fromCanceled = Task.FromCanceled<int>(cts.Token);
Console.WriteLine($"{fromCanceled.IsCanceled} {fromCanceled.IsCompleted}");
try { Task.FromCanceled(none); } catch (ArgumentOutOfRangeException e) { Console.WriteLine(e.Message); }
try { Task.Run((Action)null!, none); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { Task.Run((Func<Task>)null!, none); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

Cache? cache = null;
var first = LazyInitializer.EnsureInitialized(ref cache);
var second = LazyInitializer.EnsureInitialized(ref cache);
Console.WriteLine($"{first.Id} {ReferenceEquals(first, second)}");
List<string>? names = null;
LazyInitializer.EnsureInitialized(ref names, () => new List<string> { "made" });
LazyInitializer.EnsureInitialized(ref names, () => new List<string> { "not again" });
Console.WriteLine(string.Join(",", names));
try { string? nothing = null; LazyInitializer.EnsureInitialized(ref nothing, () => null!); }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); }

int number = 0;
bool initialized = false;
object? syncLock = null;
Console.WriteLine(LazyInitializer.EnsureInitialized(ref number, ref initialized, ref syncLock, () => 17));
Console.WriteLine(LazyInitializer.EnsureInitialized(ref number, ref initialized, ref syncLock, () => 99));
Console.WriteLine($"{initialized} {syncLock is not null}");
var counter = 0;
bool made = false;
object? gate = null;
Cache? viaFlag = null;
LazyInitializer.EnsureInitialized(ref viaFlag, ref made, ref gate);
Console.WriteLine($"{viaFlag?.Id} {made}");
string? locked = null;
object? lockObject = null;
Console.WriteLine(LazyInitializer.EnsureInitialized(ref locked, ref lockObject, () => $"locked {++counter}"));
Console.WriteLine(LazyInitializer.EnsureInitialized(ref locked, ref lockObject, () => $"locked {++counter}"));

var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => LazyInitializer.EnsureInitialized(ref shared, () => new Cache()), none)).ToArray();
var results = await Task.WhenAll(tasks);
Console.WriteLine(results.All(r => ReferenceEquals(r, shared)));

partial class Program
{
    static Cache? shared;
}

sealed class Cache
{
    private static int s_next;
    public int Id { get; } = Interlocked.Increment(ref s_next);
}
