// Type.GetType / Assembly.GetType with a constant name find a type the program never names otherwise
// (kept at compile time, as ILLink keeps it for NativeAOT); unknown names give null or TypeLoadException.
using System;
using System.Reflection;

namespace Plugins
{
    public class Unused
    {
        public override string ToString() => "unused";
    }

    public class Outer
    {
        public class Inner { }
    }
}

class Program
{
    static void Main()
    {
        Assembly assembly = typeof(Program).Assembly;
        Type unused = assembly.GetType("Plugins.Unused");
        Console.WriteLine(unused?.FullName ?? "null");
        Console.WriteLine(Type.GetType("Plugins.Outer+Inner")?.FullName ?? "null");
        Console.WriteLine(assembly.GetType("plugins.unused", false, true)?.Name ?? "null");
        Console.WriteLine(assembly.GetType("Plugins.Missing") == null);
        Console.WriteLine(assembly.GetType("System.String") == null);
        try { assembly.GetType("Plugins.Missing", true); }
        catch (TypeLoadException e) { Console.WriteLine(e.Message); }
        try { Type.GetType("Plugins.Missing", true); }
        catch (TypeLoadException e) { Console.WriteLine(e.Message); }
        Console.WriteLine(ReferenceEquals(unused.Assembly, assembly));
    }
}
