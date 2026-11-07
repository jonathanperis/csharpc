// The metadata of the events a listener sees: the default task of an event with neither task nor opcode, explicit
// opcodes and tags and channels, WriteEventWithRelatedActivityId (the related id leaves the payload), typed
// payloads (the declared parameter types), WriteEventCore with raw descriptors, the thread's activity id.
#:property EventSourceSupport=true
#:property AllowUnsafeBlocks=true
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

[EventSource(Name = "Probe-T")]
sealed class T : EventSource
{
    public static readonly T Log = new T();
    [Event(1, Opcode = EventOpcode.Info)] public void ExplicitInfo() { WriteEvent(1); }
    [Event(2)] public void Plain() { WriteEvent(2); }
    [Event(3, Opcode = EventOpcode.Start)] public void FooStart() { WriteEvent(3); }
    [Event(4, Opcode = EventOpcode.Stop)] public void FooStop() { WriteEvent(4); }
    [Event(5, Tags = (EventTags)3, Channel = EventChannel.Operational)] public void Tagged() { WriteEvent(5); }
    [Event(6, ActivityOptions = EventActivityOptions.Detachable)] public void Act() { WriteEvent(6); }
    [Event(7)] public void Related(Guid relatedActivityId, string s) { WriteEventWithRelatedActivityId(7, relatedActivityId, s); }
    [Event(8, Level = EventLevel.Informational)] public void Ints(int a, uint b, short c, byte d, sbyte e, ushort f, ulong g, float h, char i) { WriteEvent(8, a, b, c, d, e, f, g, h, i); }
    [Event(9)] public unsafe void Core(string s, int n) { fixed (char* p = s) { EventData* d = stackalloc EventData[2]; d[0].DataPointer = (IntPtr)p; d[0].Size = (s.Length + 1) * 2; d[1].DataPointer = (IntPtr)(&n); d[1].Size = 4; WriteEventCore(9, 2, d); } }
}

class Listener : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Probe-T") EnableEvents(source, EventLevel.Verbose);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"event id={e.EventId} name={e.EventName} task={(int)e.Task} opcode={e.Opcode} tags={(int)e.Tags} channel={e.Channel} version={e.Version} names={(e.PayloadNames == null ? "NULL" : string.Join(",", e.PayloadNames))} payload={(e.Payload == null ? "NULL" : string.Join("|", e.Payload.Select(p => p?.GetType().Name + ":" + p)))} related={e.RelatedActivityId} activity={e.ActivityId} msg={(e.Message == null ? "NULL" : e.Message)}");
    }
}

class Program
{
    static void Main()
    {
        var l = new Listener();
        var t = T.Log;
        t.ExplicitInfo(); t.Plain(); t.FooStart(); t.FooStop(); t.Tagged(); t.Act(); t.Related(new Guid("11111111-2222-3333-4444-555555555555"), "rel");
        t.Ints(1, 2, 3, 4, 5, 6, 7, 8.5f, (char)122);
        t.Core("core", 5);
        EventSource.SetCurrentThreadActivityId(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        t.Plain();
        Console.WriteLine(t.IsEnabled(EventLevel.Verbose, EventKeywords.None) + " " + t.IsEnabled(EventLevel.LogAlways, (EventKeywords)5));
    }
}
