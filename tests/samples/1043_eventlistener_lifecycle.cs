// Listener and source lifecycles: a listener created after the source (replayed from its base constructor, before the
// derived constructor runs), sources created later, two listeners with different filters (newest first), arguments and
// EnableEvent / DisableEvent in OnEventCommand, DisableEvents and Dispose, handlers on EventWritten /
// EventSourceCreated / EventCommandExecuted (a new EventSourceCreated handler hears of the existing sources at once),
// and Dispose of the source.
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

[EventSource(Name = "Life-A")]
sealed class SourceA : EventSource
{
    public static readonly SourceA Log = new SourceA();
    public static class Keywords { public const EventKeywords Net = (EventKeywords)1; public const EventKeywords Disk = (EventKeywords)2; }
    [Event(1, Keywords = Keywords.Net, Level = EventLevel.Informational)] public void Net(string host) { WriteEvent(1, host); }
    [Event(2, Keywords = Keywords.Disk, Level = EventLevel.Warning)] public void Disk(string path, long bytes) { WriteEvent(2, path, bytes); }
    [Event(3, Level = EventLevel.Verbose)] public void Chatty(int n) { WriteEvent(3, n); }
    [Event(4, Level = EventLevel.Critical)] public void Boom(string why) { WriteEvent(4, why); }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        Console.WriteLine($"  [A.OnEventCommand {command.Command} enabled={IsEnabled()}]");
        if (command.Command == EventCommand.Enable && command.Arguments != null && command.Arguments.TryGetValue("mute", out var mute) && mute == "chatty")
        {
            Console.WriteLine("  [disabling event 3: " + command.DisableEvent(3) + ", unknown: " + command.DisableEvent(99) + "]");
        }
    }
}

sealed class SourceB : EventSource
{
    public SourceB() : base("Life-B") { }
    public void Ping() { Write("Ping"); }
}

class Plain : EventListener
{
    public readonly string Tag;
    public readonly List<string> Seen = new List<string>();
    public static EventLevel Level = EventLevel.Verbose;
    public static EventKeywords Keywords = EventKeywords.None;
    public static IDictionary<string, string> Arguments;
    public Plain(string tag) { Tag = tag; }
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (!source.Name.StartsWith("Life-")) return;
        Console.WriteLine($"  [listener {Tag ?? "(ctor not run)"} sees source {source.Name}]");
        if (source.Name == "Life-A") EnableEvents(source, Level, Keywords, Arguments);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Seen.Add(e.EventName);
        Console.WriteLine($"  [{Tag}] {e.EventSource.Name}/{e.EventName} {e.Level} {string.Join(",", e.Payload)}");
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine("-- a source first, then a listener (replay)");
        var a = SourceA.Log;
        Console.WriteLine("enabled before: " + a.IsEnabled());
        var first = new Plain("first");
        Console.WriteLine("enabled: " + a.IsEnabled() + " " + a.IsEnabled(EventLevel.Verbose, EventKeywords.None) + " " + a.IsEnabled(EventLevel.Informational, SourceA.Keywords.Net));
        a.Net("example.org"); a.Disk("/tmp/x", 12); a.Chatty(1); a.Boom("bad");

        Console.WriteLine("-- a second listener with a filter, a source created later");
        Plain.Level = EventLevel.Warning; Plain.Keywords = SourceA.Keywords.Disk;
        var second = new Plain("second");
        a.Net("both?"); a.Disk("/var", 1); a.Chatty(2); a.Boom("again");
        var b = new SourceB();
        Console.WriteLine("b enabled: " + b.IsEnabled());
        b.Ping();
        Console.WriteLine(string.Join(",", EventSource.GetSources().Where(s => s.Name.StartsWith("Life-")).Select(s => s.Name)));

        Console.WriteLine("-- disable one source for one listener");
        first.DisableEvents(a);
        a.Net("x"); a.Disk("/y", 2);
        Console.WriteLine("enabled: " + a.IsEnabled() + " " + a.IsEnabled(EventLevel.Informational, SourceA.Keywords.Net) + " " + a.IsEnabled(EventLevel.Warning, SourceA.Keywords.Disk));
        first.EnableEvents(a, EventLevel.LogAlways);
        a.Chatty(3);

        Console.WriteLine("-- EventWritten / EventSourceCreated events and arguments");
        Plain.Level = EventLevel.Verbose; Plain.Keywords = EventKeywords.None;
        Plain.Arguments = new Dictionary<string, string> { ["mute"] = "chatty" };
        var third = new Plain("third");
        a.Chatty(4); a.Net("last");
        second.Dispose(); third.Dispose();
        first.Dispose(); first.Dispose();
        Console.WriteLine("enabled after: " + a.IsEnabled());
        a.Boom("nobody");
        Console.WriteLine($"{first.Seen.Count} {second.Seen.Count} {third.Seen.Count}");

        Console.WriteLine("-- handlers instead of an override");
        var commandsSeen = 0;
        a.EventCommandExecuted += (s, e) => { commandsSeen++; Console.WriteLine("  [command executed: " + e.Command + "]"); };
        var handlers = new HandlerListener();
        a.Boom("via handler");
        handlers.Dispose();
        Console.WriteLine("commands: " + commandsSeen);
        Console.WriteLine("-- dispose the source");
        var last = new Plain("last");
        a.Dispose();
        a.Boom("disposed");
        Console.WriteLine("enabled: " + a.IsEnabled());
        last.Dispose();

        Console.WriteLine("-- errors");
        try { first.EnableEvents(null, EventLevel.Verbose); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
        try { first.DisableEvents(null); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
}

class HandlerListener : EventListener
{
    public HandlerListener()
    {
        EventWritten += (s, e) => Console.WriteLine("  [handler] " + e.EventName + " " + string.Join(",", e.Payload));
        EventSourceCreated += (s, e) => { if (e.EventSource.Name == "Life-A") ((EventListener)s).EnableEvents(e.EventSource, EventLevel.Informational); };
        foreach (var source in EventSource.GetSources().Where(x => x.Name == "Life-A")) EnableEvents(source, EventLevel.Informational);
    }
}
