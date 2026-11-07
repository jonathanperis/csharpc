// What an EventSource reports to the listener that enables it when its events are wrong: an undefined opcode, a
// duplicate event id, an id that is not positive and an argument count that does not match the event. They arrive as
// "EventSourceMessage" events and the source stays disabled (or, for a bad call, delivers the event anyway).
#:property EventSourceSupport=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

[EventSource(Name = "Probe-A")]
sealed class A : EventSource
{
    [Event(1, Task = (EventTask)5)] public void E1() { WriteEvent(1); }
}
[EventSource(Name = "Probe-B")]
sealed class B : EventSource
{
    [Event(1, Opcode = (EventOpcode)99)] public void E1() { WriteEvent(1); }
}
[EventSource(Name = "Probe-C")]
sealed class C : EventSource
{
    [Event(1)] public void E1(int x) { WriteEvent(1, x); }
    [Event(1)] public void E2(int x) { WriteEvent(1, x); }
}
[EventSource(Name = "Probe-D")]
sealed class D : EventSource
{
    [Event(1)] public void E1(int x) { WriteEvent(2, x); }
    [Event(2)] public void E2(int x, int y) { WriteEvent(2, x); }
    public void NotMarked(int x) { WriteEvent(3, x); }
}
[EventSource(Name = "Probe-E")]
sealed class E : EventSource
{
    [Event(1, Level = EventLevel.Informational)] public void E1(string s) { WriteEvent(1, s); }
    [Event(2)] public void Two(string s, int n) { WriteEvent(2, s, n, 5); }
}
[EventSource(Name = "Probe-F")]
sealed class F : EventSource
{
    [Event(0)] public void Zero() { WriteEvent(0); }
}
[EventSource(Name = "Probe-G")]
sealed class G : EventSource
{
    public G() : base(EventSourceSettings.ThrowOnEventWriteErrors) { }
    [Event(1)] public void Ok(string s) { WriteEvent(1, s); }
    [Event(2)] public void Mismatch(string s) { WriteEvent(2, s, s); }
}

class Listener : EventListener
{
    public static string Only;
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == Only) { Console.WriteLine("created " + source.Name); EnableEvents(source, EventLevel.Verbose); }
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"event id={e.EventId} name={e.EventName} task={(int)e.Task} opcode={e.Opcode} names={string.Join(",", e.PayloadNames ?? new System.Collections.ObjectModel.ReadOnlyCollection<string>(new List<string>()))} payload={string.Join("|", (e.Payload ?? new System.Collections.ObjectModel.ReadOnlyCollection<object>(new List<object>())).Select(p => p?.ToString() ?? "null"))}");
    }
}

class Program
{
    static void Run(string name, Func<EventSource> make, Action<EventSource> use)
    {
        Console.WriteLine("== " + name);
        Listener.Only = name;
        using var l = new Listener();
        var s = make();
        Console.WriteLine("enabled=" + s.IsEnabled() + " construction=" + (s.ConstructionException?.GetType().Name ?? "none") + " " + s.ConstructionException?.Message);
        try { use(s); } catch (Exception e) { Console.WriteLine(e.GetType().Name + ": " + e.Message); }
    }
    static void Main()
    {
        Run("Probe-A", () => new A(), s => ((A)s).E1());
        Run("Probe-B", () => new B(), s => ((B)s).E1());
        Run("Probe-C", () => new C(), s => { ((C)s).E1(1); ((C)s).E2(2); });
        Run("Probe-E", () => new E(), s => { ((E)s).E1("a"); ((E)s).Two("b", 1); });
        Run("Probe-F", () => new F(), s => ((F)s).Zero());
        Run("Probe-G", () => new G(), s => { ((G)s).Ok("fine"); ((G)s).Mismatch("x"); });
    }
}
