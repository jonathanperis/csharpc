// The OnEventCommand calls of EnableEvents and DisableEvents: one per call, also for a repeated enable or a disable
// by a listener that is not enabled; the source stays enabled while another listener is.
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

[EventSource(Name = "Cmd-A")]
sealed class SourceA : EventSource
{
    public static readonly SourceA Log = new SourceA();
    [Event(1)] public void One() { WriteEvent(1); }
    protected override void OnEventCommand(EventCommandEventArgs command) { Console.WriteLine($"  [OnEventCommand {command.Command} enabled={IsEnabled()}]"); }
}

class L1 : EventListener
{
    public L1(bool enableInBody) { if (enableInBody) foreach (var s in EventSource.GetSources().Where(x => x.Name == "Cmd-A")) EnableEvents(s, EventLevel.Verbose); }
    protected override void OnEventSourceCreated(EventSource source) { if (source.Name == "Cmd-A") EnableEvents(source, EventLevel.Verbose); }
}

class L2 : EventListener
{
    public L2() { foreach (var s in EventSource.GetSources().Where(x => x.Name == "Cmd-A")) EnableEvents(s, EventLevel.Informational); }
}

class Program
{
    static void Main()
    {
        var a = SourceA.Log;
        Console.WriteLine("-- L2 only (enable in body)");
        var l2 = new L2();
        Console.WriteLine("-- disable");
        l2.Dispose();
        Console.WriteLine("-- L1 then L2");
        var l1 = new L1(false);
        var l2b = new L2();
        Console.WriteLine("-- enable again by hand");
        l2b.EnableEvents(a, EventLevel.Informational);
        l2b.EnableEvents(a, EventLevel.Informational);
        l2b.DisableEvents(a);
        l2b.DisableEvents(a);
        Console.WriteLine("-- done");
    }
}
