// With event sources on (EventSourceSupport=true), the runtime's and libraries' default event sources exist before Main runs, as
// EventSource.InitializeDefaultEventSources creates them (#455): Microsoft-Windows-DotNETRuntime, FrameworkEventSource, ArrayPoolEventSource
// and System.Runtime; System.Diagnostics.Metrics is preregistered and created when the first listener appears. Each listener is told about
// every source, in creation order; none of them is enabled until a listener asks.
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

foreach (var source in EventSource.GetSources())
    Console.WriteLine($"{source.Name} {source.Guid} enabled={source.IsEnabled()} construction={source.ConstructionException?.GetType().Name ?? "ok"}");

using (var first = new Listener())
    Console.WriteLine("first listener: " + string.Join(", ", first.Created));
using var second = new Listener();
Console.WriteLine("second listener: " + string.Join(", ", second.Created));
Console.WriteLine("sources: " + string.Join(", ", EventSource.GetSources().Select(s => s.Name)));
Console.WriteLine("any enabled: " + EventSource.GetSources().Any(s => s.IsEnabled()));

sealed class Listener : EventListener
{
    public readonly List<string> Created = new();
    protected override void OnEventSourceCreated(EventSource source) { lock (Created) Created.Add(source.Name); }
}
