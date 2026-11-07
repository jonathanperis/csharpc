// EventSource.Write<T> failures: the payload type is checked whenever the source is enabled (before the filters, and
// not at all when nothing listens), a primitive or enumerable top-level T and non-compliant types are exceptions, and
// failures while reading the data reach the listeners as an "EventSourceMessage" event.
#:property EventSourceSupport=true
#pragma warning disable IL2026
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

enum Color { Red = 1 }
[EventData] class Req { public string Url { get; set; } = "u"; }
[EventData] class Thrower { public int Ok => 1; public int Boom => throw new InvalidOperationException("kaboom"); }
[EventData] class NullNested { public Req Inner { get; set; } = null; }
[EventData] class WithVersion { public Version V { get; set; } = new Version(1, 2); }
[EventData] class WithObject { public object O { get; set; } = 3; }
[EventData] class WithPlain { public Plain P { get; set; } = new Plain(); }
[EventData] class WithStrings { public string[] S { get; set; } = { "a" }; }
[EventData] class WithNestedArray { public int[][] N { get; set; } = { new[] { 1 } }; }
[EventData] class WithNullableArray { public int?[] N { get; set; } = { 1, null }; }
[EventData] class WithHalf { public Half H { get; set; } = (Half)1.5f; }
[EventData] class WithDateOnly { public DateOnly D { get; set; } = new DateOnly(2020, 1, 2); }
[EventData] class WithType { public Type T { get; set; } = typeof(int); }
class Plain { public int A { get; set; } = 1; }
struct PlainStruct { public int A { get; set; } }

class L : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Probe-Errors") EnableEvents(source, EventLevel.Informational, EventKeywords.All);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"event id={e.EventId} name={e.EventName} level={e.Level} names={(e.PayloadNames == null ? "null" : string.Join(",", e.PayloadNames))} payload={(e.Payload == null ? "null" : string.Join(" | ", e.Payload.Select(p => p?.ToString())))}");
    }
}

[EventSource(Name = "Probe-Errors")]
sealed class Src : EventSource { public static Src Log = new(); }

static class Program
{
    static void Try(string what, Action a)
    {
        Console.WriteLine("-- " + what);
        try { a(); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var log = Src.Log;
        Try("nobody listens: no analysis, no exception", () => { log.Write("x", new Version(1, 2)); Console.WriteLine("returned"); });
        Console.WriteLine($"enabled: {log.IsEnabled()}");
        using var l = new L();
        Console.WriteLine($"enabled: {log.IsEnabled()}");
        Try("primitive int", () => log.Write("Prim", 42));
        Try("enum", () => log.Write("Enum", Color.Red));
        Try("nullable", () => log.Write("Nullable", new EventSourceOptions { Level = EventLevel.Informational }, (int?)3));
        Try("int array", () => log.Write("Array", new[] { 1, 2 }));
        Try("dictionary", () => log.Write("Dict", new Dictionary<string, int> { ["a"] = 1 }));
        Try("string", () => log.Write("Str", "hello"));
        Try("plain class", () => log.Write("Plain", new Plain()));
        Try("plain struct", () => log.Write("PlainStruct", new PlainStruct { A = 1 }));
        Try("Version", () => log.Write("Version", new Version(1, 2)));
        Try("object", () => log.Write<object>("Object", new { A = 1 }));
        Try("property of type Version", () => log.Write("PV", new WithVersion()));
        Try("property of type object", () => log.Write("PO", new WithObject()));
        Try("property of a plain class", () => log.Write("PP", new WithPlain()));
        Try("property of type Half", () => log.Write("PH", new WithHalf()));
        Try("property of type DateOnly", () => log.Write("PD", new WithDateOnly()));
        Try("property of type Type", () => log.Write("PT", new WithType()));
        Try("string array property", () => log.Write("SA", new WithStrings()));
        Try("nested array property", () => log.Write("NA", new WithNestedArray()));
        Try("nullable array property", () => log.Write("NullableA", new WithNullableArray()));
        Try("anonymous with a plain property", () => log.Write("AP", new { P = new Plain() }));
        Try("filtered out, still analyzed", () => log.Write("Filtered", new EventSourceOptions { Level = EventLevel.Verbose }, new Version(1, 2)));
        Try("filtered out, valid", () => log.Write("FilteredOk", new EventSourceOptions { Level = EventLevel.Verbose }, new { A = 1 }));
        Console.WriteLine("== failures while reading the data reach the listener (once the level is enabled)");
        var info = new EventSourceOptions { Level = EventLevel.Informational };
        Try("null data", () => log.Write<Req>("NullData", info, null));
        Try("null data, filtered", () => log.Write<Req>("NullDataFiltered", null));
        Try("null nested payload", () => log.Write("NullNested", info, new NullNested()));
        Try("getter throws", () => log.Write("Thrower", info, new Thrower()));
        Try("null string T", () => log.Write<string>("NullStringT", info, null));
        Try("nullable T", () => log.Write("NullableT", info, (int?)3));
        Try("null nullable T", () => log.Write("NullNullableT", info, (int?)null));
        Try("string T", () => log.Write<string>("StringT", info, "text"));
        Console.WriteLine("== after dispose");
        var disposable = new EventSource("Disposed-Source");
        disposable.Dispose();
        Try("disposed source", () => { disposable.Write("x", new Version(1, 2)); Console.WriteLine("returned"); });
    }
}
