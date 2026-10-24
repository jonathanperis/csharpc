#:property EventSourceSupport=true
#:property AllowUnsafeBlocks=true
// EventSource with event sources supported (file-based apps turn them off by default): no session or
// listener, so nothing is enabled and nothing is written, but Name, Guid (from [EventSource], or derived
// from the name), Settings, traits, ConstructionException and ToString are .NET's.
using System;
using System.Diagnostics.Tracing;

var log = DemoLog.Log;
Console.WriteLine($"{log.IsEnabled()} {log.IsEnabled(EventLevel.Critical, EventKeywords.All)} {log.IsEnabled(EventLevel.Verbose, EventKeywords.None, EventChannel.Debug)}");
Console.WriteLine($"[{log.Name}] [{log.Guid}] [{log}] [{log.Settings}] [{log.ConstructionException?.Message}] [{log.GetTrait("x")}]");
log.Started("job", 3);
log.Progress(1, 2L, "three", new byte[] { 4 }, 5.5);
log.Raw(7);

var plain = new PlainSource();
Console.WriteLine($"[{plain.Name}] [{plain.Guid}] [{plain}] [{plain.Settings}]");
Console.WriteLine($"[{EventSource.GetName(typeof(PlainSource))}] [{EventSource.GetGuid(typeof(DemoLog))}] [{EventSource.GetName(typeof(DemoLog))}] [{EventSource.GetGuid(typeof(NamedOnly))}]");

foreach (var name in new[] { "Custom-Name-Here", "MyCompany-MyProduct-MyComponent", "a", "Ünïcødé-Ñame", "lowercase-and-UPPER" })
{
    using var source = new EventSource(name);
    Console.WriteLine($"[{source.Name}] [{source.Guid}] {source.IsEnabled()} {source.Settings}");
}
var withTraits = new EventSource("Traits", EventSourceSettings.EtwManifestEventFormat, "ETW_GROUP", "{4f50731a-89cf-4782-b3e0-dce8c90476ba}", "key", "value");
Console.WriteLine($"{withTraits.GetTrait("key")} {withTraits.GetTrait("ETW_GROUP")} {withTraits.GetTrait("missing") ?? "null"} {withTraits.Settings}");
var odd = new EventSource("Odd", EventSourceSettings.Default, "only-key");
Console.WriteLine($"[{odd.Name}] {odd.ConstructionException?.GetType().Name}: {odd.ConstructionException?.Message}");
var explicitGuid = new EventSource("Explicit", new Guid("01234567-89ab-cdef-0123-456789abcdef"));
Console.WriteLine($"{explicitGuid} {explicitGuid.Settings}");
var emptyGuid = new EventSource("Empty", Guid.Empty);
Console.WriteLine($"[{emptyGuid.Name}] {emptyGuid.ConstructionException?.Message}");
try { _ = new EventSource(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
try { _ = new EventSource("Both", EventSourceSettings.EtwManifestEventFormat | EventSourceSettings.EtwSelfDescribingEventFormat); }
catch (ArgumentException e) { Console.WriteLine(e.Message); }
try { EventSource.GetName(null!); } catch (ArgumentNullException e) { Console.WriteLine(e.Message); }
withTraits.Write("evt");
withTraits.Write("evt", new EventSourceOptions { Level = EventLevel.Warning });
log.Dispose();
log.Dispose();
Console.WriteLine($"{log.IsEnabled()} {EventSource.CurrentThreadActivityId}");

[EventSource(Name = "Demo-Log", Guid = "12345678-1234-1234-1234-123456789012")]
sealed class DemoLog : EventSource
{
    public static readonly DemoLog Log = new();

    [Event(1, Level = EventLevel.Informational, Message = "Started {0} ({1})")]
    public void Started(string name, int count) { if (IsEnabled()) WriteEvent(1, name, count); }

    [Event(2)]
    public void Progress(int a, long b, string c, byte[] d, double e) => WriteEvent(2, a, b, c, d, e);

    [NonEvent]
    public unsafe void Raw(int value)
    {
        EventData* data = stackalloc EventData[1];
        data[0].DataPointer = (IntPtr)(&value);
        data[0].Size = sizeof(int);
        WriteEventCore(3, 1, data);
    }
}

sealed class PlainSource : EventSource { }

[EventSource(Name = "Only-A-Name")]
sealed class NamedOnly : EventSource { }
