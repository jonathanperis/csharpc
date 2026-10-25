// System.Diagnostics.Trace, compiled from System.Diagnostics.TraceSource's IL (it names its types in
// System.Private.CoreLib, which the reference pack doesn't: a facade bridges the two), with listeners
// writing to the console; Debug output reaches Trace's listeners once Trace has installed its provider.
using System;
using System.Diagnostics;
using System.IO;

Trace.WriteLine("before any listener: the default listener only talks to a debugger");
var listener = new TextWriterTraceListener(Console.Out) { Name = "console" };
Trace.Listeners.Add(listener);
Trace.AutoFlush = true;
Console.WriteLine($"listeners: {Trace.Listeners.Count} [{string.Join(", ", ListenerNames())}]");

Trace.WriteLine("hello from Trace");
Trace.Write("partial ");
Trace.WriteLine("line", "category");
Trace.Indent();
Trace.WriteLine("indented once");
Trace.IndentSize = 2;
Trace.Indent();
Trace.WriteLine($"indent level {Trace.IndentLevel}, size {Trace.IndentSize}");
Trace.Unindent();
Trace.Unindent();
Trace.WriteLineIf(true, "conditional true");
Trace.WriteLineIf(false, "conditional false");

// Debug goes through the provider Trace installed (the program is built with DEBUG, like `dotnet run`).
Debug.WriteLine("from Debug");
Debug.Print("Debug.Print {0}", "args");
Debug.WriteLineIf(true, "Debug conditional");

var writer = new StringWriter();
var captured = new TextWriterTraceListener(writer);
Trace.Listeners.Add(captured);
Trace.WriteLine("to both listeners");
Trace.Flush();
Trace.Listeners.Remove(captured);
Console.WriteLine($"captured: {writer.ToString().TrimEnd()}");
Trace.Listeners.Remove("console");
Trace.WriteLine("after removing the console listener: not printed");
Console.WriteLine($"done, listeners {Trace.Listeners.Count}");

static string[] ListenerNames()
{
    var names = new string[Trace.Listeners.Count];
    for (int i = 0; i < names.Length; i++) names[i] = Trace.Listeners[i].Name + ":" + Trace.Listeners[i].GetType().Name;
    return names;
}
