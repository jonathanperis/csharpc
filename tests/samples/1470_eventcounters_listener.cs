// EventCounters published to an in-process listener: enabling a source with the "EventCounterIntervalSec" argument
// polls its PollingCounter, IncrementingPollingCounter, EventCounter and IncrementingEventCounter once per interval and
// writes one "EventCounters" event per counter, whose payload is a dictionary of the counter's statistics (name, display
// name and units, mean/min/max/count or increment, series, counter type, metadata). The measured interval length
// (IntervalSec) is timing and left out; each counter's first payload with data is printed.
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;

var log = DemoSource.Log;
var poll = new PollingCounter("queue-length", log, () => 42.5) { DisplayName = "Queue Length", DisplayUnits = "items" };
poll.AddMetadata("owner", "demo");
poll.AddMetadata("kind", "gauge");
var total = new IncrementingPollingCounter("requests", log, () => 7) { DisplayName = "Requests", DisplayRateTimeScale = TimeSpan.FromSeconds(1) };
var latency = new EventCounter("latency", log) { DisplayName = "Latency", DisplayUnits = "ms" };
var hits = new IncrementingEventCounter("hits", log) { DisplayName = "Hits", DisplayRateTimeScale = TimeSpan.FromMinutes(1) };
Console.WriteLine(poll);
Console.WriteLine(total);
Console.WriteLine(latency);
Console.WriteLine(hits);
Console.WriteLine($"{poll.Name} {poll.DisplayName} {poll.DisplayUnits} {poll.EventSource.Name} {total.DisplayRateTimeScale}");

using var listener = new CounterListener();
listener.EnableEvents(log, EventLevel.Verbose, EventKeywords.All, new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
latency.WriteMetric(1);
latency.WriteMetric(2.5f);
latency.WriteMetric(5.5);
hits.Increment();
hits.Increment(4.5);
if (!listener.Done.Wait(TimeSpan.FromSeconds(30))) Console.WriteLine("timed out");
lock (listener.First)
    foreach (var (name, payload) in listener.First.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine(name);
        foreach (var (key, value) in payload) Console.WriteLine($"  {key} = {value} ({value?.GetType().Name})");
    }
Console.WriteLine($"event name {listener.EventName}, id {listener.EventId}, level {listener.Level}, payload names [{listener.PayloadNames}]");
listener.DisableEvents(log);
poll.Dispose();
try { latency.WriteMetric(1); Console.WriteLine("write after disable ok"); } catch (Exception e) { Console.WriteLine(e.GetType().Name); }
try { _ = new PollingCounter(null!, log, () => 1); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { _ = new EventCounter("x", null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

[EventSource(Name = "Demo-Counters")]
sealed class DemoSource : EventSource
{
    public static readonly DemoSource Log = new();
}

sealed class CounterListener : EventListener
{
    public readonly Dictionary<string, List<KeyValuePair<string, object?>>> First = new();
    public readonly ManualResetEventSlim Done = new();
    public string? EventName, PayloadNames;
    public int EventId;
    public EventLevel Level;

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName != "EventCounters" || e.Payload is not [IDictionary<string, object?> payload]) return;
        var name = (string)payload["Name"]!;
        bool hasData = name switch { "latency" => (int)payload["Count"]! > 0, "hits" => (double)payload["Increment"]! > 0, _ => true };
        lock (First)
        {
            if (!hasData || First.ContainsKey(name)) return;
            First[name] = payload.Where(p => p.Key != "IntervalSec").ToList();
            (EventName, EventId, Level, PayloadNames) = (e.EventName, e.EventId, e.Level, string.Join(",", e.PayloadNames!));
            if (First.Count == 4) Done.Set();
        }
    }
}
