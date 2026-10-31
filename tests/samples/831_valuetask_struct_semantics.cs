// ValueTask<T> as .NET's struct: equality and hashing, ToString, AsTask identity, Preserve, Result throwing the
// original exception, sources awaited twice (the source's error), ConfigureAwait, and async methods returning one.
using System.Threading.Tasks.Sources;

var a = new ValueTask<int>(5);
var b = new ValueTask<int>(5);
Console.WriteLine($"equal results: {a == b} {a.Equals(b)} {a.Equals((object)b)} {a != new ValueTask<int>(6)} hash {a.GetHashCode() == 5.GetHashCode()}");
Console.WriteLine($"default: {default(ValueTask<int>) == new ValueTask<int>(0)} hash {default(ValueTask<string>).GetHashCode()} '{default(ValueTask<string>)}'");
var task = Task.FromResult(7);
var fromTask = new ValueTask<int>(task);
Console.WriteLine($"task-backed: {fromTask == new ValueTask<int>(task)} {fromTask == new ValueTask<int>(7)} AsTask same {ReferenceEquals(fromTask.AsTask(), task)} '{fromTask}'");
Console.WriteLine($"result AsTask: {a.AsTask().Result} {a.AsTask().Status} distinct {!ReferenceEquals(new ValueTask<object?>(new object()).AsTask(), new ValueTask<object?>(new object()).AsTask())}");
Console.WriteLine($"preserve: {a.Preserve() == a} {a.Preserve().Result} {fromTask.Preserve() == fromTask}");
object boxed = a;
Console.WriteLine($"boxed: {boxed is ValueTask<int>} {((ValueTask<int>)boxed).Result} {boxed.Equals(b)}");

var failed = new ValueTask<int>(Task.FromException<int>(new InvalidOperationException("bad")));
try { _ = failed.Result; } catch (Exception e) { Console.WriteLine($"Result throws {e.GetType().Name}: {e.Message}"); }
Console.WriteLine($"faulted: {failed.IsCompleted} {failed.IsFaulted} {failed.IsCanceled} {failed.IsCompletedSuccessfully} '{failed}'");
var viaFactory = ValueTask.FromException<string>(new FormatException("factory"));
try { await viaFactory; } catch (FormatException e) { Console.WriteLine($"await throws {e.Message}"); }
var canceled = ValueTask.FromCanceled<int>(new CancellationToken(true));
Console.WriteLine($"canceled: {canceled.IsCanceled} {canceled.IsFaulted}");
try { await canceled; } catch (OperationCanceledException e) { Console.WriteLine($"await canceled {e.GetType().Name}"); }

// A source awaited twice: the second GetResult sees a stale token (ManualResetValueTaskSourceCore throws).
var source = new Source<int>();
source.Complete(11);
var fromSource = new ValueTask<int>(source, source.Version);
Console.WriteLine($"source: {fromSource.IsCompleted} {await fromSource}");
source.Reset();
try { Console.WriteLine(await fromSource); }
catch (InvalidOperationException e) { Console.WriteLine($"second await: {e.GetType().Name}"); }
var pending = new Source<string>();
var pendingTask = new ValueTask<string>(pending, pending.Version);
var asTask = pendingTask.AsTask();
_ = Task.Run(() => pending.Complete("done"));
Console.WriteLine($"pending as task: {await asTask} {asTask.Status}");

Console.WriteLine($"configured: {await new ValueTask<int>(3).ConfigureAwait(false)} {await new ValueTask<int>(Task.Run(() => 4)).ConfigureAwait(false)}");
Console.WriteLine($"async: {await Twice(5)} {await TwiceAfterYield(6)} {(await Pair(1)).Item2}");
var sync = Twice(8);
Console.WriteLine($"sync async method: {sync.IsCompleted} {sync.Result}");
var fault = Fails();
try { await fault; } catch (ArgumentException e) { Console.WriteLine($"async fault: {e.Message}"); }

static async ValueTask<int> Twice(int x) { await Task.CompletedTask; return x * 2; }
static async ValueTask<int> TwiceAfterYield(int x) { await Task.Yield(); return x * 2; }
static async ValueTask<(int, string)> Pair(int x) { await Task.Delay(1); return (x, "pair"); }
static async ValueTask<int> Fails() { await Task.Yield(); throw new ArgumentException("from async"); }

sealed class Source<T> : IValueTaskSource<T>
{
    private ManualResetValueTaskSourceCore<T> _core;
    public short Version => _core.Version;
    public void Reset() => _core.Reset();
    public void Complete(T value) => _core.SetResult(value);
    public T GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}
