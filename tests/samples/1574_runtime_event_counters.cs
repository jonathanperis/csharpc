// The System.Runtime event source's 27 counters (#455), enabled by a listener with EventCounterIntervalSec: each counter's name, kind,
// display name and units, and the payload fields of its kind; the ProcessorCount event and the AppContextSwitch events the same command
// writes. Counter values depend on the machine and the moment, so only their presence is printed.
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;

using var listener = new RuntimeListener();
if (!listener.AllSeen.Wait(TimeSpan.FromSeconds(60))) Console.WriteLine("timed out");
listener.DisableEvents(listener.Runtime!);
List<(string Name, string Type, string DisplayName, string Units)> counters;
lock (listener.Counters) counters = listener.Counters.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
Console.WriteLine($"counters: {counters.Count}");
foreach (var c in counters) Console.WriteLine($"{c.Name} | {c.Type} | {c.DisplayName} | {c.Units}");
lock (listener.Events)
{
    var processorCount = listener.Events.Where(e => e.Name == "ProcessorCount").ToList();
    Console.WriteLine($"ProcessorCount events: {processorCount.Count}, payload is Environment.ProcessorCount: {processorCount.All(e => (int)e.Payload[0]! == Environment.ProcessorCount)}");
    var switches = listener.Events.Where(e => e.Name == "LogAppContextSwitch").Select(e => $"{e.Payload[0]}={e.Payload[1]}").ToList();
    Console.WriteLine("EventSource.IsSupported switch logged: " + switches.Contains("System.Diagnostics.Tracing.EventSource.IsSupported=1"));
}

sealed class RuntimeListener : EventListener
{
    public EventSource? Runtime;
    public readonly Dictionary<string, (string Name, string Type, string DisplayName, string Units)> Counters = new();
    public readonly List<(string Name, object?[] Payload)> Events = new();
    public readonly ManualResetEventSlim AllSeen = new();

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name != "System.Runtime") return;
        Runtime = source;
        EnableEvents(source, EventLevel.Verbose, (EventKeywords)(-1), new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventSource.Name != "System.Runtime") return;
        if (e.EventName == "EventCounters" && e.Payload is [IDictionary<string, object> p])
        {
            var name = (string)p["Name"];
            var type = (string)p["CounterType"];
            var units = p.TryGetValue("DisplayUnits", out var u) ? (string)u : "";
            // A polling counter reports Mean, Min, Max and Count; an incrementing one its Increment.
            bool shape = type == "Mean" ? p.ContainsKey("Mean") && p.ContainsKey("Min") && p.ContainsKey("Max") && p.ContainsKey("Count")
                : type == "Sum" && p.ContainsKey("Increment");
            lock (Counters)
            {
                Counters[name] = (name, shape ? type : type + " (unexpected payload)", (string)p["DisplayName"], units);
                if (Counters.Count >= 27) AllSeen.Set();
            }
        }
        else lock (Events) Events.Add((e.EventName ?? "", e.Payload?.ToArray() ?? new object?[0]));
    }
}
