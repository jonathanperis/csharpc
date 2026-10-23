// Type.Assembly and the Assembly / AssemblyName / Version objects it leads to: the program's own
// assembly, System.Private.CoreLib and the other framework assemblies BCL types live in at run time
// (which differ from the reference assemblies the compiler binds against), equality, and
// Assembly.GetExecutingAssembly / GetEntryAssembly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

class Widget { }

static class Program
{
    static void Main()
    {
        var program = typeof(Widget).Assembly;
        Console.WriteLine(program.FullName);
        Console.WriteLine(program);
        AssemblyName name = program.GetName();
        Console.WriteLine($"{name.Name} | {name.Version} | culture='{name.CultureName}' | token={name.GetPublicKeyToken()?.Length}");
        Console.WriteLine(name.FullName);
        Console.WriteLine(name);
        Console.WriteLine($"{name.Version!.Major}.{name.Version.Minor}.{name.Version.Build}.{name.Version.Revision}");

        Console.WriteLine("-- framework types");
        foreach (var t in new[] { typeof(int), typeof(string), typeof(List<int>), typeof(Dictionary<string, Widget>), typeof(LinkedList<int>),
                     typeof(SortedSet<int>), typeof(Queue<int>), typeof(Enumerable), typeof(Regex), typeof(StringBuilder), typeof(Console),
                     typeof(Uri), typeof(Math), typeof(Action<int>), typeof(Func<int, int>), typeof(int[]), typeof(Widget[]), typeof(List<Widget>),
                     typeof(DayOfWeek), typeof(Version), typeof(Assembly), typeof(Exception), typeof(InvalidOperationException), typeof(KeyValuePair<int, int>) })
            Console.WriteLine($"{t.Name,-28} {t.Assembly.GetName().Name}");

        Console.WriteLine("-- full names");
        var corelib = typeof(object).Assembly;
        Console.WriteLine(corelib.FullName);
        var token = corelib.GetName().GetPublicKeyToken()!;
        Console.WriteLine(string.Concat(token.Select(b => b.ToString("x2"))));
        Console.WriteLine(typeof(LinkedList<int>).Assembly.FullName);
        Console.WriteLine(typeof(Regex).Assembly);

        Console.WriteLine("-- identity");
        Console.WriteLine(typeof(int).Assembly == typeof(List<string>).Assembly);
        Console.WriteLine(typeof(int).Assembly == typeof(Widget).Assembly);
        Console.WriteLine(typeof(int).Assembly != typeof(LinkedList<int>).Assembly);
        Console.WriteLine(ReferenceEquals(typeof(Widget).Assembly, typeof(Program).Assembly));
        Console.WriteLine(typeof(Widget).Assembly.Equals(typeof(Program).Assembly));
        Console.WriteLine(Assembly.GetExecutingAssembly() == program);
        Console.WriteLine(Assembly.GetEntryAssembly() == program);
        Console.WriteLine(Assembly.GetEntryAssembly()!.GetName().Name);
        Console.WriteLine(program.IsDynamic);
        Console.WriteLine(new Widget().GetType().Assembly == program);
        Console.WriteLine(new List<int>().GetType().Assembly == corelib);

        var byAssembly = new Dictionary<string, List<string>>();
        foreach (var t in new[] { typeof(int), typeof(Widget), typeof(HashSet<int>), typeof(Program), typeof(string), typeof(Stack<int>) })
        {
            var key = t.Assembly.GetName().Name!;
            if (!byAssembly.TryGetValue(key, out var list)) byAssembly[key] = list = new List<string>();
            list.Add(t.Name);
        }
        foreach (var (key, list) in byAssembly) Console.WriteLine($"{key}: {string.Join(", ", list)}");

        Console.WriteLine("-- System.Version");
        var v = new Version(2, 5, 1);
        Console.WriteLine($"{v} {v.Major} {v.Minor} {v.Build} {v.Revision} {v.ToString(2)} {v.ToString(3)}");
        Console.WriteLine(new Version(1, 2));
        Console.WriteLine(new Version(1, 2, 3, 4).ToString(4));
        Console.WriteLine(new Version(1, 2, 3, 65539).MinorRevision + " " + new Version(1, 2, 3, 65539).MajorRevision);
        Console.WriteLine(new Version(1, 2) < new Version(1, 2, 0));
        Console.WriteLine(new Version(1, 10) > new Version(1, 9, 9, 9));
        Console.WriteLine(new Version(3, 0) == new Version(3, 0));
        Console.WriteLine(new Version(3, 0).Equals(new Version(3, 0, 0)));
        Console.WriteLine(new Version(3, 0).CompareTo(new Version(2, 99)));
        Console.WriteLine(new Version(3, 0).CompareTo(null));
        Console.WriteLine(new Version().ToString());
        Console.WriteLine(new Version(4, 1, 2, 3).GetHashCode());
        Version? none = null;
        Console.WriteLine(none < new Version(0, 0));
        try { new Version(1, 2).ToString(3); }
        catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
        try { new Version(1, 2, 3).ToString(5); }
        catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
        try { new Version(1, -2); }
        catch (ArgumentOutOfRangeException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
        try { new Version(1, 2).CompareTo("1.2"); }
        catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    }
}
