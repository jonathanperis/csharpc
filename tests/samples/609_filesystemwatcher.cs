// FileSystemWatcher: properties, filters, argument checks, and created/renamed/deleted events (inotify
// on Linux) observed through WaitForChanged and event handlers, in a subdirectory too.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

class Program
{
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "csharpc-fsw-" + Environment.ProcessId);
        Directory.CreateDirectory(root);
        try { Run(root); }
        finally { Directory.Delete(root, true); }
    }

    static string Rel(string root, string path) => path.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');

    static void Run(string root)
    {
        var idle = new FileSystemWatcher();
        Console.WriteLine($"'{idle.Path}' {idle.Filter} {idle.NotifyFilter} {idle.IncludeSubdirectories} {idle.EnableRaisingEvents} {idle.InternalBufferSize}");
        idle.Filters.Add("*.*");
        idle.Filters.Add("*.txt");
        Console.WriteLine($"{idle.Filter} {string.Join(",", idle.Filters)}");
        idle.InternalBufferSize = 10;
        Console.WriteLine(idle.InternalBufferSize);
        try { idle.NotifyFilter = (NotifyFilters)1024; } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { new FileSystemWatcher(Path.Combine(root, "missing")); } catch (ArgumentException e) { Console.WriteLine(e.Message.Replace(root, "ROOT") + " " + e.ParamName); }
        try { new FileSystemWatcher(""); } catch (ArgumentException e) { Console.WriteLine(e.Message + " " + e.ParamName); }
        try { idle.Path = Path.Combine(root, "missing"); } catch (ArgumentException e) { Console.WriteLine(e.Message.Replace(root, "ROOT") + " " + e.ParamName); }
        idle.Dispose();

        Directory.CreateDirectory(Path.Combine(root, "sub"));
        using var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
        var events = new List<string>();
        var gate = new object();
        var count = new SemaphoreSlim(0);
        void Record(string what)
        {
            lock (gate) events.Add(what);
            count.Release();
        }
        watcher.Created += (s, e) => Record($"created {e.Name} {e.ChangeType} {Rel(root, e.FullPath)} {ReferenceEquals(s, watcher)}");
        watcher.Deleted += (s, e) => Record($"deleted {e.Name}");
        watcher.Renamed += (s, e) => Record($"renamed {e.OldName} -> {e.Name} {Rel(root, e.OldFullPath)}");
        watcher.EnableRaisingEvents = true;

        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        Wait(count);
        File.Move(Path.Combine(root, "a.txt"), Path.Combine(root, "b.txt"));
        Wait(count);
        File.WriteAllText(Path.Combine(root, "sub", "c.log"), "y");
        Wait(count);
        File.Delete(Path.Combine(root, "b.txt"));
        Wait(count);
        Directory.CreateDirectory(Path.Combine(root, "newdir"));
        Wait(count);
        Thread.Sleep(200); // the new directory gets its watch
        File.WriteAllText(Path.Combine(root, "newdir", "d.txt"), "z");
        Wait(count);
        lock (gate) foreach (var e in events) Console.WriteLine(e);

        watcher.Filter = "*.txt";
        var waiter = new Thread(() =>
        {
            Thread.Sleep(100);
            File.WriteAllText(Path.Combine(root, "skip.log"), "1");
            File.WriteAllText(Path.Combine(root, "take.txt"), "2");
        });
        watcher.EnableRaisingEvents = false;
        waiter.Start();
        var result = watcher.WaitForChanged(WatcherChangeTypes.Created, 5000);
        waiter.Join();
        Console.WriteLine($"{result.ChangeType} {result.Name} {result.OldName ?? "null"} {result.TimedOut} {watcher.EnableRaisingEvents}");
        var timedOut = watcher.WaitForChanged(WatcherChangeTypes.Deleted, 50);
        Console.WriteLine($"{(int)timedOut.ChangeType} {timedOut.Name ?? "null"} {timedOut.TimedOut}");
    }

    static void Wait(SemaphoreSlim count)
    {
        if (!count.Wait(5000)) Console.WriteLine("timed out waiting for an event");
    }
}
