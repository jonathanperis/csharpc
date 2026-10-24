// CancellationToken.Register / UnsafeRegister and CancellationTokenRegistration: callbacks run newest
// first on Cancel, Dispose/Unregister remove them, exceptions are aggregated (or the first one
// propagates with Cancel(true)), linked sources, disposed sources and already-canceled tokens.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

static void Section(string name) => Console.WriteLine("-- " + name);

Section("order and Dispose");
{
    var cts = new CancellationTokenSource();
    var log = new List<string>();
    cts.Token.Register(() => log.Add("first"));
    var second = cts.Token.Register(() => log.Add("second"));
    cts.Token.Register(state => log.Add("state " + state), 42);
    cts.Token.Register((state, token) => log.Add($"token {state} {token.IsCancellationRequested} {token == cts.Token}"), "s");
    cts.Token.UnsafeRegister(state => log.Add("unsafe " + state), "u");
    cts.Token.UnsafeRegister((state, token) => log.Add("unsafe2 " + state + " " + token.CanBeCanceled), "v");
    var last = cts.Token.Register(() => log.Add("last"), useSynchronizationContext: true);
    second.Dispose();
    Console.WriteLine($"{last.Unregister()} {last.Unregister()} {last.Token == cts.Token} {last.Token.CanBeCanceled}");
    cts.Cancel();
    Console.WriteLine(string.Join(", ", log));
    Console.WriteLine($"{second.Unregister()} {cts.IsCancellationRequested}");
    cts.Cancel(); // a second Cancel runs nothing
    Console.WriteLine(log.Count);
}

Section("already canceled, None, default registration");
{
    var cts = new CancellationTokenSource();
    cts.Cancel();
    var reg = cts.Token.Register(() => Console.WriteLine("runs at once"));
    Console.WriteLine($"{reg == default} {reg.Token.CanBeCanceled} {reg.Unregister()}");
    reg.Dispose();
    var none = CancellationToken.None.Register(() => Console.WriteLine("never"));
    Console.WriteLine($"{none.Equals(default(CancellationTokenRegistration))} {none.GetHashCode()} {none.Token == CancellationToken.None}");
    default(CancellationTokenRegistration).Dispose();
    var canceled = new CancellationToken(true);
    Console.WriteLine($"{canceled.IsCancellationRequested} {canceled.CanBeCanceled} {canceled == new CancellationToken(true)} {new CancellationToken(false) == CancellationToken.None}");
    canceled.Register(() => Console.WriteLine("canceled token callback"));
    try { canceled.ThrowIfCancellationRequested(); }
    catch (OperationCanceledException e) { Console.WriteLine(e.Message); }
}

Section("registration equality");
{
    var cts = new CancellationTokenSource();
    var a = cts.Token.Register(() => { });
    var b = cts.Token.Register(() => { });
    var copy = a;
    Console.WriteLine($"{a == copy} {a != b} {a.Equals(b)} {a.Equals((object)copy)} {a.Equals("x")}");
    IDisposable boxed = b;
    boxed.Dispose();
    Console.WriteLine(b.Unregister());
}

Section("exceptions");
{
    var cts = new CancellationTokenSource();
    var log = new List<string>();
    cts.Token.Register(() => log.Add("oldest"));
    cts.Token.Register(() => throw new InvalidOperationException("first failure"));
    cts.Token.Register(() => log.Add("middle"));
    cts.Token.Register(() => throw new FormatException("second failure"));
    try { cts.Cancel(); }
    catch (AggregateException e)
    {
        Console.WriteLine(e.Message);
        Console.WriteLine(e.InnerException?.GetType().Name + ": " + e.InnerException?.Message);
    }
    Console.WriteLine(string.Join(", ", log));

    var first = new CancellationTokenSource();
    var ran = new List<string>();
    first.Token.Register(() => ran.Add("never runs"));
    first.Token.Register(() => throw new ArgumentException("propagates"));
    first.Token.Register(() => ran.Add("newest"));
    try { first.Cancel(throwOnFirstException: true); }
    catch (ArgumentException e) { Console.WriteLine("first: " + e.Message); }
    Console.WriteLine(string.Join(", ", ran) + " " + first.IsCancellationRequested);
    first.Token.Register(() => Console.WriteLine("late registration runs at once"));

    try { CancellationToken.None.Register(null!); }
    catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
}

Section("unregistering from a callback");
{
    var cts = new CancellationTokenSource();
    CancellationTokenRegistration older = default;
    older = cts.Token.Register(() => Console.WriteLine("older (unregistered, never runs)"));
    CancellationTokenRegistration self = default;
    self = cts.Token.Register(() =>
    {
        Console.WriteLine($"newer: unregister older {older.Unregister()}, self {self.Unregister()}");
        self.Dispose(); // the running callback: no wait on its own thread
    });
    cts.Cancel();
}

Section("linked sources");
{
    var a = new CancellationTokenSource();
    var b = new CancellationTokenSource();
    var linked = CancellationTokenSource.CreateLinkedTokenSource(a.Token, b.Token);
    var single = CancellationTokenSource.CreateLinkedTokenSource(a.Token);
    var many = CancellationTokenSource.CreateLinkedTokenSource(new[] { b.Token, CancellationToken.None, a.Token });
    a.Token.Register(() => Console.WriteLine($"a callback; linked {linked.IsCancellationRequested}"));
    linked.Token.Register(() => Console.WriteLine("linked callback"));
    single.Token.Register(() => Console.WriteLine("single callback"));
    many.Token.Register(() => Console.WriteLine("many callback"));
    var disposedLink = CancellationTokenSource.CreateLinkedTokenSource(a.Token);
    disposedLink.Token.Register(() => Console.WriteLine("disposed link: never"));
    disposedLink.Dispose();
    a.Cancel();
    Console.WriteLine($"{linked.IsCancellationRequested} {single.IsCancellationRequested} {many.IsCancellationRequested} {b.IsCancellationRequested} {disposedLink.IsCancellationRequested}");

    var parent = new CancellationTokenSource();
    var child = CancellationTokenSource.CreateLinkedTokenSource(parent.Token);
    child.Token.Register(() => throw new InvalidOperationException("child callback"));
    parent.Token.Register(() => throw new InvalidOperationException("parent callback"));
    try { parent.Cancel(); }
    catch (AggregateException e) { Console.WriteLine(e.Message); }

    try { CancellationTokenSource.CreateLinkedTokenSource(Array.Empty<CancellationToken>()); }
    catch (ArgumentException e) { Console.WriteLine(e.Message); }
    try { CancellationTokenSource.CreateLinkedTokenSource((CancellationToken[])null!); }
    catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
    var precanceled = new CancellationTokenSource();
    precanceled.Cancel();
    Console.WriteLine(CancellationTokenSource.CreateLinkedTokenSource(precanceled.Token, CancellationToken.None).IsCancellationRequested);
}

Section("Dispose and TryReset");
{
    var cts = new CancellationTokenSource();
    var token = cts.Token;
    var reg = token.Register(() => Console.WriteLine("never (disposed)"));
    cts.Dispose();
    cts.Dispose();
    Console.WriteLine($"{cts.IsCancellationRequested} {token.CanBeCanceled}");
    try { cts.Cancel(); } catch (ObjectDisposedException e) { Console.WriteLine(e.Message); }
    try { _ = cts.Token; } catch (ObjectDisposedException e) { Console.WriteLine(e.GetType().Name); }
    try { cts.CancelAfter(10); } catch (ObjectDisposedException e) { Console.WriteLine(e.GetType().Name); }
    try { cts.TryReset(); } catch (ObjectDisposedException e) { Console.WriteLine(e.GetType().Name); }
    token.Register(() => Console.WriteLine("never (registered after Dispose)"));
    Console.WriteLine(reg.Unregister());

    using (var reset = new CancellationTokenSource())
    {
        reset.Token.Register(() => Console.WriteLine("never (reset)"));
        Console.WriteLine(reset.TryReset());
        reset.Token.Register(() => Console.WriteLine("after reset"));
        reset.Cancel();
        Console.WriteLine(reset.TryReset());
    }
    IDisposable viaInterface = new CancellationTokenSource();
    viaInterface.Dispose();
    try { ((CancellationTokenSource)viaInterface).Cancel(); } catch (ObjectDisposedException) { Console.WriteLine("disposed through IDisposable"); }
}

Section("delays and CancelAsync");
{
    var cts = new CancellationTokenSource();
    var log = new List<string>();
    cts.Token.Register(() => log.Add("before delay"));
    var delay = Task.Delay(Timeout.Infinite, cts.Token);
    cts.Token.Register(() => log.Add($"after delay: {delay.IsCanceled}"));
    await cts.CancelAsync();
    Console.WriteLine(string.Join(", ", log) + $"; delay canceled {delay.IsCanceled}");
    var async = new CancellationTokenSource();
    async.Token.Register(() => throw new InvalidOperationException("async failure"));
    try { await async.CancelAsync(); }
    catch (AggregateException e) { Console.WriteLine("CancelAsync: " + e.Message); }
    await using (var reg = new CancellationTokenSource().Token.Register(() => { }))
        Console.WriteLine("await using");
}

Section("AggregateException constructors");
{
    var inner = new List<Exception> { new InvalidOperationException("a"), new ArgumentException("b", "p") };
    Console.WriteLine(new AggregateException(inner).Message);
    Console.WriteLine(new AggregateException("custom", inner).Message);
    Console.WriteLine(new AggregateException(new FormatException("x"), new TimeoutException("y")).Message);
    Console.WriteLine(new AggregateException("with one", new FormatException("z")).Message);
    Console.WriteLine(new AggregateException("no inner").Message + " | " + new AggregateException().Message);
    Console.WriteLine(new AggregateException(inner).InnerException?.Message);
    try { _ = new AggregateException(new Exception[] { new Exception("ok"), null! }); }
    catch (ArgumentException e) { Console.WriteLine(e.Message); }
    try { _ = new AggregateException((IEnumerable<Exception>)null!); }
    catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
    try { _ = new AggregateException("m", (Exception)null!); }
    catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
}
