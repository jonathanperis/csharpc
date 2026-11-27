// Assembly.GetCallingAssembly(): NativeAOT walks no stack and returns the entry assembly, wherever it is called from.
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

Show("Main", Assembly.GetCallingAssembly());
Show("helper", Helper.Caller());
Show("lambda", new Func<Assembly>(() => Assembly.GetCallingAssembly())());
Show("same as entry", Helper.Caller() == Assembly.GetEntryAssembly() ? Helper.Caller() : null);

static void Show(string label, Assembly? assembly) => Console.WriteLine($"{label}: {assembly?.GetName().Name ?? "null"}");

static class Helper
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Assembly Caller() => Assembly.GetCallingAssembly();
}
