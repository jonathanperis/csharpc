// The current culture flows like .NET's ExecutionContext: into Task.Run work, await continuations
// (which may resume on another thread) and threads started after it was set.
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

double d = 1.5;
CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
await Task.Delay(10);
Console.WriteLine("after delay: " + CultureInfo.CurrentCulture.Name + " " + d);
string run = await Task.Run(() => CultureInfo.CurrentCulture.Name + " " + d);
Console.WriteLine("Task.Run: " + run);
await Task.Yield();
Console.WriteLine("after yield: " + CultureInfo.CurrentCulture.Name + " " + d);
string inner = await Task.Run(async () =>
{
    await Task.Delay(5);
    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
    await Task.Delay(5);
    return CultureInfo.CurrentCulture.Name + " " + d;
});
Console.WriteLine("inner: " + inner + " | outer: " + CultureInfo.CurrentCulture.Name + " " + d);
var results = new string[3];
var tasks = new Task[3];
for (int i = 0; i < 3; i++)
{
    int k = i;
    tasks[i] = Task.Run(() => results[k] = CultureInfo.CurrentCulture.Name);
}
await Task.WhenAll(tasks);
Console.WriteLine("WhenAll: " + string.Join(",", results));
await Task.Delay(1).ConfigureAwait(false);
Console.WriteLine("ConfigureAwait(false): " + CultureInfo.CurrentCulture.Name + " " + d);
var t = new Thread(() => run = CultureInfo.CurrentCulture.Name + " " + d);
t.Start();
t.Join();
Console.WriteLine("thread: " + run);
