// EventSource throughput, the program behind docs/il-first/System.Diagnostics.Tracing.md (#423): WriteEvent with nothing
// listening, then the manifest events (int, string and long, mixed and params arguments) and the self-describing Write<T>
// with an in-process EventListener that sums every payload. The checksum depends on the fixed inputs only.
#:property EventSourceSupport=true
#pragma warning disable IL2026
using System;
using System.Diagnostics.Tracing;

class Program
{
    static void Main()
    {
        var log = BenchSource.Log;
        string[] names = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];
        long check = 0;
        for (int i = 0; i < 20_000_000; i++)
        {
            log.Tick(i);
            if (log.IsEnabled()) check++;
        }
        using var listener = new SumListener();
        listener.EnableEvents(log, EventLevel.Verbose, EventKeywords.All);
        for (int i = 0; i < 400_000; i++)
        {
            log.Tick(i);
            log.Named(names[i & 7], i * 3L);
            log.Mixed(i, names[(i >> 3) & 7], i * 0.5);
            if ((i & 3) == 0) log.Many(i, i + 1L, names[i & 7], (byte)i, i * 0.25f);
        }
        for (int i = 0; i < 100_000; i++)
            log.Write("Shape", new { Width = i, Name = names[i & 7], Area = i * 2.0 });
        Console.WriteLine(check + listener.Sum);
    }
}

[EventSource(Name = "Bench-Trace")]
sealed class BenchSource : EventSource
{
    public static readonly BenchSource Log = new();

    public static class Keywords
    {
        public const EventKeywords Extra = (EventKeywords)4;
    }

    [Event(1, Level = EventLevel.Informational)]
    public void Tick(int n) => WriteEvent(1, n);

    [Event(2, Level = EventLevel.Verbose)]
    public void Named(string name, long value) => WriteEvent(2, name, value);

    [Event(3, Level = EventLevel.Warning, Keywords = Keywords.Extra)]
    public void Mixed(int a, string b, double c) => WriteEvent(3, a, b, c);

    [Event(4)]
    public void Many(int a, long b, string c, byte d, float e) => WriteEvent(4, a, b, c, d, e);
}

sealed class SumListener : EventListener
{
    public long Sum;

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Sum += e.EventId + (e.EventName?.Length ?? 0) + (long)e.Level;
        if (e.Payload is null) return;
        foreach (var value in e.Payload)
            Sum += value switch
            {
                int i => i,
                long l => l,
                string s => s.Length,
                double d => (long)d,
                float f => (long)f,
                byte b => b,
                _ => 1,
            };
    }
}
