// An EventListener receives the events an EventSource writes: the [Event] metadata (id, name, level, keywords,
// opcode, task, version, message, payload names and values), OnEventSourceCreated and OnEventCommand, per-listener
// level and keyword filters, IsEnabled, listener Dispose. Keywords are the program's own (Keywords class).
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

enum Color { Red = 1, Green = 2 }

[EventSource(Name = "Demo-Events")]
sealed class DemoEvents : EventSource
{
    public static readonly DemoEvents Log = new DemoEvents();

    public static class Keywords { public const EventKeywords Two = (EventKeywords)0x2; public const EventKeywords Four = (EventKeywords)0x4; }
    public static class Tasks { public const EventTask Five = (EventTask)5; }

    [Event(1, Level = EventLevel.Informational, Message = "Hello {0}")]
    public void Hello(string name) { if (IsEnabled()) WriteEvent(1, name); }

    [Event(2, Level = EventLevel.Warning, Keywords = Keywords.Two, Opcode = EventOpcode.Start, Task = Tasks.Five, Version = 3)]
    public void Counted(int a, long b) { WriteEvent(2, a, b); }

    [Event(3, Level = EventLevel.Verbose)]
    public void Verb(string s, int n, bool flag, double d, Color color) { WriteEvent(3, s, n, flag, d, color); }

    [Event(4, Level = EventLevel.Error, Message = "Failed: {0} ({1})")]
    public void Failed(string reason, int code) { WriteEvent(4, reason, code); }

    [Event(5)]
    public void NoArgs() { WriteEvent(5); }

    [Event(9, Opcode = EventOpcode.Start)]
    public void OpStart() { WriteEvent(9); }

    [Event(10, Opcode = EventOpcode.Stop, Level = EventLevel.LogAlways)]
    public void OpStop() { WriteEvent(10); }

    [Event(11, Level = EventLevel.LogAlways, Message = "{0} is {1:X}")]
    public void Always(string what, int value) { WriteEvent(11, what, value); }

    [Event(6, Level = EventLevel.Critical, Keywords = Keywords.Four)]
    public void Many(string a, string b, string c, string d) { WriteEvent(6, a, b, c, d); }

    [Event(7)]
    public void Bytes(byte[] data) { WriteEvent(7, data); }

    [Event(8)]
    public void Guids(Guid g, DateTime when) { WriteEvent(8, g, when); }

    [NonEvent] public void Helper() { Console.WriteLine("helper"); }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        Console.WriteLine($"OnEventCommand {command.Command} args={(command.Arguments == null ? "null" : string.Join(",", command.Arguments.Select(kv => kv.Key + "=" + kv.Value)))}");
    }
}

class Listener : EventListener
{
    public List<string> Log = new();
    public static EventLevel Level = EventLevel.Verbose; public static EventKeywords Keywords;
    public Listener() { }
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Demo-Events") Console.WriteLine("created: " + source.Name);
        if (source.Name == "Demo-Events") EnableEvents(source, Level, Keywords);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"event {e.EventId} {e.EventName} level={e.Level} kw={(long)e.Keywords} opcode={e.Opcode} task={(int)e.Task} version={e.Version} channel={e.Channel} tags={e.Tags} msg={e.Message}");
        Console.WriteLine("  names=" + string.Join(",", e.PayloadNames ?? new System.Collections.ObjectModel.ReadOnlyCollection<string>(new List<string>())));
        Console.WriteLine("  payload=" + string.Join(" | ", (e.Payload ?? new System.Collections.ObjectModel.ReadOnlyCollection<object>(new List<object>())).Select(p => p == null ? "null" : p.GetType().Name + ":" + (p is byte[] b ? string.Join(",", b) : p.ToString()))));
        Console.WriteLine("  source=" + e.EventSource.Name + " activity=" + (e.ActivityId == Guid.Empty) + " related=" + (e.RelatedActivityId == Guid.Empty) + " thread=" + (e.OSThreadId != 0) + " ts=" + (e.TimeStamp > DateTime.MinValue));
        if (e.Message != null) Console.WriteLine("  formatted=" + string.Format(e.Message, e.Payload.ToArray()));
        Log.Add(e.EventName);
    }
}

class Program
{
    static void Main()
    {
        var log = DemoEvents.Log;
        Console.WriteLine("before listener: " + log.IsEnabled() + " " + log.Name + " " + log.Guid);
        log.Hello("nobody");
        Console.WriteLine("-- listener 1 (verbose)");
        var l1 = new Listener();
        Console.WriteLine("enabled: " + log.IsEnabled() + " " + log.IsEnabled(EventLevel.Verbose, EventKeywords.None) + " " + log.IsEnabled(EventLevel.LogAlways, EventKeywords.All));
        log.Hello("world");
        log.Counted(1, 2);
        log.Verb("s", 7, true, 2.5, Color.Green);
        log.Failed("disk", 42);
        log.NoArgs();
        log.Many("a", "b", "c", "d");
        log.Bytes(new byte[] { 1, 2, 3 });
        log.Guids(new Guid("11111111-2222-3333-4444-555555555555"), new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        log.Helper();
        log.OpStart(); log.OpStop(); log.Always("n", 255);
        Console.WriteLine("-- listener 2 (warning, keyword 2)");
        Listener.Level = EventLevel.Warning; Listener.Keywords = (EventKeywords)2;
        var l2 = new Listener();
        log.Hello("both");
        log.Counted(3, 4);
        log.Failed("x", 1);
        Console.WriteLine("-- dispose listener 1");
        l1.Dispose();
        log.Hello("only second");
        log.Counted(5, 6);
        Console.WriteLine($"l1={l1.Log.Count} l2={l2.Log.Count}");
        l2.Dispose();
        Console.WriteLine("after: " + log.IsEnabled());
        log.Hello("none");
        Console.WriteLine(string.Join(",", EventSource.GetSources().Where(s => s.Name == "Demo-Events").Select(s => s.Name)));
    }
}
