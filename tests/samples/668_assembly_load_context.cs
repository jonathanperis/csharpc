// AssemblyLoadContext as NativeAOT has it: contexts work, nothing loads from a path.
#nullable enable
#pragma warning disable IL2026 // LoadFromAssemblyPath: only its argument checks run here
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

class PluginContext : AssemblyLoadContext
{
    public PluginContext() : base(isCollectible: true) { }
    protected override Assembly? Load(AssemblyName name)
    {
        Console.WriteLine("Load " + name.Name);
        return null;
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine(AssemblyLoadContext.Default.Name);
        Console.WriteLine(AssemblyLoadContext.Default.IsCollectible);
        Console.WriteLine(AssemblyLoadContext.GetLoadContext(typeof(Program).Assembly) == AssemblyLoadContext.Default);
        var named = new AssemblyLoadContext("named");
        Console.WriteLine(named.Name + " " + named.IsCollectible);
        var plugin = new PluginContext();
        Console.WriteLine(plugin.Name == null);
        Console.WriteLine(plugin.IsCollectible);
        Console.WriteLine(string.Join(",", AssemblyLoadContext.All.Select(c => c.Name ?? "(null)")));
        try { plugin.LoadFromAssemblyPath("relative.dll"); }
        catch (ArgumentException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { named.Unload(); }
        catch (InvalidOperationException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        plugin.Unloading += c => Console.WriteLine("unloading " + (c == plugin));
        plugin.Unload();
        try { plugin.LoadFromAssemblyPath("/tmp/none.dll"); }
        catch (InvalidOperationException e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        Console.WriteLine(string.Join(",", AssemblyLoadContext.All.Select(c => c.Name ?? "(null)")));
    }
}
