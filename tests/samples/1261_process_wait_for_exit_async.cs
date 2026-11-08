// Process.WaitForExitAsync: completes when the child exits (at once if it already did, even with a cancelled token); a token
// cancelled while waiting cancels the task (TaskCanceledException), a token cancelled beforehand throws
// OperationCanceledException; an object without a process throws InvalidOperationException. Runs a POSIX sh.
using System.Diagnostics;

static Process Shell(string script, bool redirectOutput = false)
{
    var info = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = redirectOutput };
    info.ArgumentList.Add("-c");
    info.ArgumentList.Add(script);
    return Process.Start(info)!;
}

static async Task TryAsync(string what, Func<Task> action)
{
    try { await action(); Console.WriteLine(what + ": completed"); }
    catch (Exception e) { Console.WriteLine(what + ": " + e.GetType().Name + ": " + e.Message); }
}

using (var p = Shell("exit 3"))
{
    await p.WaitForExitAsync();
    Console.WriteLine($"exited={p.HasExited} code={p.ExitCode}");
    await p.WaitForExitAsync();
    Console.WriteLine("again: ok");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await TryAsync("exited, cancelled token", () => p.WaitForExitAsync(cancelled.Token));
}

using (var p = Shell("sleep 30"))
{
    using var cts = new CancellationTokenSource(100);
    await TryAsync("cancelled while waiting", () => p.WaitForExitAsync(cts.Token));
    Console.WriteLine("still running: " + !p.HasExited);
    p.Kill();
    await p.WaitForExitAsync();
    Console.WriteLine($"killed: exited={p.HasExited} code={p.ExitCode}");
}

using (var p = Shell("sleep 30"))
{
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await TryAsync("running, cancelled token", () => p.WaitForExitAsync(cancelled.Token));
    p.Kill();
    p.WaitForExit();
}

await TryAsync("no process", () => new Process().WaitForExitAsync());

using (var p = Shell("sleep 0.3"))
{
    Task wait = p.WaitForExitAsync();
    Console.WriteLine("completed before the child exits: " + wait.IsCompleted);
    await wait;
    Console.WriteLine($"status={wait.Status} code={p.ExitCode}");
}

using (var p = Shell("echo hello; echo world", redirectOutput: true))
{
    await p.WaitForExitAsync();
    Console.WriteLine(p.StandardOutput.ReadToEnd().Replace("\n", "|"));
}

// Several waits at once.
Process[] children = { Shell("exit 1"), Shell("sleep 0.1; exit 2"), Shell("sleep 0.2; exit 3") };
await Task.WhenAll(children.Select(c => c.WaitForExitAsync()));
Console.WriteLine(string.Join(",", children.Select(c => c.ExitCode)));
foreach (var c in children) c.Dispose();
