// Change tokens from Microsoft.Extensions.Primitives (IL): CancellationChangeToken registers its
// callbacks with CancellationToken.UnsafeRegister and hands out the CancellationTokenRegistration as an
// IDisposable; CompositeChangeToken links several tokens; ChangeToken.OnChange re-registers after
// every change.
using Microsoft.Extensions.Primitives;

var log = new List<string>();

// CancellationChangeToken: callbacks run (newest first) when the source is canceled.
var cts = new CancellationTokenSource();
IChangeToken token = new CancellationChangeToken(cts.Token);
Console.WriteLine($"{token.HasChanged} {token.ActiveChangeCallbacks}");
var first = token.RegisterChangeCallback(state => log.Add("first " + state), "a");
var removed = token.RegisterChangeCallback(state => log.Add("removed " + state), "b");
token.RegisterChangeCallback(state => log.Add("last " + state), "c");
removed.Dispose();
cts.Cancel();
Console.WriteLine($"{token.HasChanged} {string.Join(", ", log)}");
first.Dispose(); // after the change: nothing to remove
log.Clear();

// A callback registered after the change runs at once.
token.RegisterChangeCallback(state => log.Add("late " + state), 1);
Console.WriteLine(string.Join(", ", log));
log.Clear();

// A disposed source: the registration is dropped (the callback never runs).
var disposedSource = new CancellationTokenSource();
var disposedToken = new CancellationChangeToken(disposedSource.Token);
disposedSource.Dispose();
using (disposedToken.RegisterChangeCallback(_ => log.Add("never"), null)) { }
Console.WriteLine($"disposed: active {disposedToken.ActiveChangeCallbacks} changed {disposedToken.HasChanged}");

// CompositeChangeToken: changes when any of its tokens changes.
var a = new CancellationTokenSource();
var b = new CancellationTokenSource();
var composite = new CompositeChangeToken(new IChangeToken[] { new CancellationChangeToken(a.Token), new CancellationChangeToken(b.Token) });
Console.WriteLine($"composite: {composite.HasChanged} {composite.ActiveChangeCallbacks} {composite.ChangeTokens.Count}");
composite.RegisterChangeCallback(state => log.Add("composite " + state), "x");
b.Cancel();
Console.WriteLine($"composite: {composite.HasChanged} {string.Join(", ", log)}");
log.Clear();

// ChangeToken.OnChange: a producer returns a fresh token after each change.
var current = new CancellationTokenSource();
var version = 0;
using (ChangeToken.OnChange(() => new CancellationChangeToken(current.Token), () => log.Add("changed " + ++version)))
{
    for (var i = 0; i < 3; i++)
    {
        var previous = current;
        current = new CancellationTokenSource();
        previous.Cancel();
    }
}
current.Cancel(); // after Dispose: no more callbacks
Console.WriteLine(string.Join(", ", log));

// OnChange with state (a fresh source per token, so the re-registration waits for the next change).
var stateSource = new CancellationTokenSource();
var stateLog = new List<string>();
var subscription = ChangeToken.OnChange(() => new CancellationChangeToken(stateSource.Token), s => stateLog.Add("state " + s), 99);
var fired = stateSource;
stateSource = new CancellationTokenSource();
fired.Cancel();
subscription.Dispose();
stateSource.Cancel();
Console.WriteLine(string.Join(", ", stateLog));

// A change token over an already-canceled token: HasChanged at once, callbacks run synchronously.
var precanceled = new CancellationChangeToken(new CancellationToken(true));
precanceled.RegisterChangeCallback(_ => Console.WriteLine("precanceled callback"), null);
Console.WriteLine(precanceled.HasChanged);
