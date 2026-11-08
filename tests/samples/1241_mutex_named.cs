// Named mutexes inside one process: every handle with the same name and scope is the same mutex (ownership and
// recursion are shared by the handles), createdNew, Local\ and Global\ prefixes, NamedWaitHandleOptions, OpenExisting
// and TryOpenExisting, disposal of the last handle, abandonment, and the release error a named mutex throws.
using System;
using System.Threading;

class Program
{
    static string s_prefix;

    static void Try(string label, Action action)
    {
        try { action(); Console.WriteLine(label + ": ok"); }
        // The names carry the process id: keep it out of the output.
        catch (Exception e) { Console.WriteLine(label + ": " + e.GetType().FullName + ": " + e.Message.Replace(s_prefix, "<prefix>").Replace("\n", "|").Replace("\r", "")); }
    }

    static NamedWaitHandleOptions Options(bool userOnly, bool sessionOnly) =>
        new NamedWaitHandleOptions { CurrentUserOnly = userOnly, CurrentSessionOnly = sessionOnly };

    static void Main()
    {
        // Names are system-wide: keep them unique per process so that runs at the same time can't see each other.
        string prefix = s_prefix = "csharpc_1241_" + Environment.ProcessId + "_";
        bool created;

        // Handles with one name are one mutex.
        var first = new Mutex(true, prefix + "a", out created);
        Console.WriteLine("first created: " + created);
        var second = new Mutex(true, prefix + "a", out created);
        Console.WriteLine("second created: " + created);
        var third = new Mutex(false, prefix + "a", out created);
        Console.WriteLine("third created: " + created);
        Console.WriteLine("third WaitOne(0), the owner thread acquires again: " + third.WaitOne(0));
        third.ReleaseMutex();
        first.ReleaseMutex();
        Console.WriteLine("released by a different handle than the one that acquired it: " + second.WaitOne(0));
        second.ReleaseMutex();
        Try("one release too many", () => first.ReleaseMutex());
        Try("unowned release through another handle", () => third.ReleaseMutex());

        // Another thread sees it as owned until the owner thread has released every acquisition.
        var owned = new Mutex(true, prefix + "owned");
        var seen = new Thread(() =>
        {
            Console.WriteLine("thread WaitOne(0): " + owned.WaitOne(0));
            Try("thread release", () => owned.ReleaseMutex());
        });
        seen.Start();
        seen.Join();
        owned.ReleaseMutex();
        var seenFree = new Thread(() =>
        {
            Console.WriteLine("thread WaitOne(0), now free: " + owned.WaitOne(0));
            owned.ReleaseMutex();
        });
        seenFree.Start();
        seenFree.Join();

        // initiallyOwned only counts for the call that creates it.
        var creator = new Mutex(false, prefix + "initial", out created);
        var opener = new Mutex(true, prefix + "initial", out created);
        Console.WriteLine("opener created: " + created);
        var visitor = new Thread(() => Console.WriteLine("visitor WaitOne(0): " + creator.WaitOne(0)));
        visitor.Start();
        visitor.Join();

        // Local\ is the default scope; Global\ is a different mutex.
        var plain = new Mutex(false, prefix + "scope", out created);
        Console.WriteLine("plain created: " + created);
        var local = new Mutex(false, "Local\\" + prefix + "scope", out created);
        Console.WriteLine("Local\\ created: " + created);
        var global = new Mutex(false, "Global\\" + prefix + "scope", out created);
        Console.WriteLine("Global\\ created: " + created);
        var global2 = new Mutex(false, "Global\\" + prefix + "scope", out created);
        Console.WriteLine("Global\\ again created: " + created);
        var otherCase = new Mutex(false, (prefix + "SCOPE").ToUpperInvariant(), out created);
        Console.WriteLine("other case created: " + created);

        // The options overloads: the prefix has to agree with CurrentSessionOnly.
        Try("default options with Global\\", () => new Mutex(false, "Global\\" + prefix + "o1", Options(true, true)));
        Try("session false with Local\\", () => new Mutex(false, "Local\\" + prefix + "o2", Options(true, false)));
        Try("session false with Global\\", () => new Mutex(false, "Global\\" + prefix + "o3", Options(true, false)));
        var userSession = new Mutex(false, prefix + "o4", Options(true, true), out created);
        Console.WriteLine("user+session created: " + created);
        var anyUser = new Mutex(false, prefix + "o4", Options(false, true), out created);
        Console.WriteLine("any user created: " + created);
        var legacy = new Mutex(false, prefix + "o4", out created);
        Console.WriteLine("legacy overload created: " + created);
        var legacyGlobal = new Mutex(false, "Global\\" + prefix + "o5", out created);
        Console.WriteLine("legacy Global\\ created: " + created);
        var anySession = new Mutex(false, prefix + "o5", Options(true, false), out created);
        Console.WriteLine("user, any session created: " + created);
        var byName = new Mutex(prefix + "o6", new NamedWaitHandleOptions());
        Console.WriteLine("name and options: " + byName.WaitOne(0));
        byName.ReleaseMutex();
        var withOptions = new Mutex(true, prefix + "o7", new NamedWaitHandleOptions());
        withOptions.ReleaseMutex();
        var unnamedWithOptions = new Mutex(true, null, new NamedWaitHandleOptions());
        unnamedWithOptions.ReleaseMutex();
        var emptyName = new Mutex(true, "");
        emptyName.ReleaseMutex();

        // OpenExisting and TryOpenExisting.
        Mutex opened;
        Console.WriteLine("TryOpenExisting: " + Mutex.TryOpenExisting(prefix + "a", out opened) + " " + (opened != null));
        Console.WriteLine("TryOpenExisting missing: " + Mutex.TryOpenExisting(prefix + "missing", out opened) + " " + (opened == null));
        Console.WriteLine("TryOpenExisting Global\\: " + Mutex.TryOpenExisting("Global\\" + prefix + "scope", out opened));
        Console.WriteLine("TryOpenExisting Global\\ options: " + Mutex.TryOpenExisting(prefix + "scope", Options(true, false), out opened));
        Console.WriteLine("TryOpenExisting options scope: " + Mutex.TryOpenExisting(prefix + "o4", Options(false, true), out opened));
        Console.WriteLine("TryOpenExisting other options: " + Mutex.TryOpenExisting(prefix + "o4", Options(false, false), out opened));
        Try("OpenExisting missing", () => Mutex.OpenExisting(prefix + "missing"));
        Try("OpenExisting missing, options", () => Mutex.OpenExisting(prefix + "missing", Options(true, true)));
        Try("OpenExisting null", () => Mutex.OpenExisting(null));
        Try("OpenExisting empty", () => Mutex.OpenExisting(""));
        Try("TryOpenExisting null", () => Mutex.TryOpenExisting(null, out opened));
        Try("TryOpenExisting incompatible prefix", () => Mutex.TryOpenExisting("Global\\x", Options(true, true), out opened));
        Try("name with a slash", () => new Mutex(false, "a/b"));
        var viaOpen = Mutex.OpenExisting(prefix + "a");
        Console.WriteLine("OpenExisting is the same mutex: " + viaOpen.WaitOne(0));
        viaOpen.ReleaseMutex();

        // The name lives until the last handle is disposed.
        var one = new Mutex(false, prefix + "life", out created);
        var two = Mutex.OpenExisting(prefix + "life");
        one.Dispose();
        Try("disposed handle", () => one.WaitOne(0));
        Console.WriteLine("still open through the other handle: " + Mutex.TryOpenExisting(prefix + "life", out opened));
        opened.Dispose();
        Console.WriteLine("two still works: " + two.WaitOne(0));
        two.ReleaseMutex();
        two.Dispose();
        Console.WriteLine("gone after the last handle: " + Mutex.TryOpenExisting(prefix + "life", out opened));
        var again = new Mutex(true, prefix + "life", out created);
        Console.WriteLine("created again: " + created);
        again.ReleaseMutex();

        // The owner ends without releasing it: the next wait acquires it and throws.
        var abandoned = new Mutex(false, prefix + "abandoned");
        var ending = new Thread(() => { abandoned.WaitOne(); });
        ending.Start();
        ending.Join();
        Try("named, abandoned", () => abandoned.WaitOne(0));
        abandoned.ReleaseMutex();

        // Composed with other handles: a named mutex can be waited on alone, but not together with others (Unix).
        var named = new Mutex(false, prefix + "wait");
        var signaled = new ManualResetEvent(true);
        Console.WriteLine("WaitAny alone: " + WaitHandle.WaitAny(new WaitHandle[] { named }));
        named.ReleaseMutex();
        Console.WriteLine("WaitAll alone: " + WaitHandle.WaitAll(new WaitHandle[] { named }));
        named.ReleaseMutex();
        Try("WaitAny with another handle", () => WaitHandle.WaitAny(new WaitHandle[] { named, signaled }));
        Try("WaitAll with another handle", () => WaitHandle.WaitAll(new WaitHandle[] { signaled, named }, 0));
        Try("WaitAll with an unnamed mutex", () => WaitHandle.WaitAll(new WaitHandle[] { new Mutex(), named }));
        Try("WaitAny, null element first", () => WaitHandle.WaitAny(new WaitHandle[] { named, null }));
    }
}
