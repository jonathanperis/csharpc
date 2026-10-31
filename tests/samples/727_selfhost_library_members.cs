// Members csharpc's own code uses (M6 self-hosting): type tests against Thread (a List<Thread> through
// IList), GC.Collect's overloads, FileNotFoundException.FusionLog, the current Process (id, CPU times),
// RuntimeEnvironment.GetRuntimeDirectory's shape, and Assembly.GetCustomAttributes(Type, bool).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading;

[assembly: Tag("first")]
[assembly: Tag("second")]

var worker = new Thread(() => { });
IList threads = new List<Thread> { worker };
Console.WriteLine($"threads: {threads.Contains(worker)} {threads.IndexOf(worker)} {threads.Contains("text")} {((object)worker is Thread)} {((object)"x" is Thread)}");
try { threads.Add("not a thread"); } catch (ArgumentException e) { Console.WriteLine(e.GetType().Name); }
var gate = new Lock();
Console.WriteLine($"lock: {((object)gate is Lock)} {new List<object> { gate, worker }.FindIndex(o => o is Lock)}");

GC.Collect(0);
GC.Collect(2, GCCollectionMode.Forced);
GC.Collect(1, GCCollectionMode.Optimized, false);
GC.Collect(2, GCCollectionMode.Aggressive, true, true);
try { GC.Collect(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.ParamName}: {e.Message}"); }
try { GC.Collect(0, (GCCollectionMode)9); } catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.ParamName}: {e.Message}"); }
Console.WriteLine("collected");

Console.WriteLine($"fusion log: {new FileNotFoundException("missing", "a.dll").FusionLog ?? "null"}");

using (var self = Process.GetCurrentProcess())
{
    Console.WriteLine($"process: {self.Id == Environment.ProcessId} {self.HasExited} {self.TotalProcessorTime >= TimeSpan.Zero} {self.UserProcessorTime >= TimeSpan.Zero}");
    if (OperatingSystem.IsLinux()) Console.WriteLine($"memory: {self.PeakWorkingSet64 > 0} {self.WorkingSet64 > 0} {self.PeakWorkingSet64 >= self.WorkingSet64 / 2}");
    else Console.WriteLine("memory: True True True");
}
Console.WriteLine($"another object: {!ReferenceEquals(Process.GetCurrentProcess(), Process.GetCurrentProcess())}");

var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
Console.WriteLine($"runtime directory: {Path.IsPathRooted(runtimeDirectory)} {runtimeDirectory.EndsWith(Path.DirectorySeparatorChar)}");

var tags = typeof(TagAttribute).Assembly.GetCustomAttributes(typeof(TagAttribute), false);
Console.WriteLine($"attributes: {tags.GetType().Name} {tags.Length} {string.Join(",", Array.ConvertAll(tags, t => ((TagAttribute)t).Name))}");
Console.WriteLine($"none: {typeof(TagAttribute).Assembly.GetCustomAttributes(typeof(ObsoleteAttribute), true).Length}");

// Parallel.ForEach with ParallelOptions (csharpc's driver writes its outputs this way).
long total = 0;
var loop = System.Threading.Tasks.Parallel.ForEach(new List<(string, int)> { ("a", 1), ("b", 2), ("c", 3) },
    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 }, item => Interlocked.Add(ref total, item.Item2));
Console.WriteLine($"foreach: {loop.IsCompleted} {total}");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    try { System.Threading.Tasks.Parallel.ForEach(new[] { 1 }.AsEnumerable(), new System.Threading.Tasks.ParallelOptions { CancellationToken = canceled.Token }, _ => { }); }
    catch (OperationCanceledException e) { Console.WriteLine($"foreach canceled: {e.CancellationToken == canceled.Token}"); }
}
try { System.Threading.Tasks.Parallel.ForEach(new[] { 1, 2 }.AsEnumerable(), new System.Threading.Tasks.ParallelOptions(), i => { if (i == 2) throw new InvalidOperationException("two"); }); }
catch (AggregateException e) { Console.WriteLine($"foreach failed: {e.InnerExceptions.Count} {e.InnerException!.Message}"); }

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
sealed class TagAttribute(string name) : Attribute { public string Name { get; } = name; }
