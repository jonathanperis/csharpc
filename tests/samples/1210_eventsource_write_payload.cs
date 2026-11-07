// EventSource.Write<T>: the self-describing events. An EventListener sees the payload names and values read from the
// public properties of an anonymous type or an [EventData] type (derived type first, then its bases), the options
// (level, keywords, opcode, tags), the activity ids, and null Payload / PayloadNames for events without data.
#:property EventSourceSupport=true
#pragma warning disable IL2026
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

enum Color { Red = 1, Green = 2 }
[Flags] enum Perms : byte { Read = 1, Write = 2 }

[EventData] class Req { public string Url { get; set; } = "u"; public int Status { get; set; } = 200; }
[EventData] class Req2 { public string Url { get; set; } = "p"; [EventIgnore] public int Hidden { get; set; } = 1; public long Size { get; set; } = 5; public int Field = 9; }
[EventData] struct Pt { public int X { get; set; } public int Y { get; set; } }
[EventData] class Inherited : Req { public bool Extra { get; set; } = true; }
[EventData] class ReadOnly { public int A => 1; public int B { get; } = 2; public int C { get; private set; } = 3; public int D { get; init; } = 4; protected int Hidden { get; set; } = 5; public static int Stat { get; set; } = 6; public int this[int i] => i; public int WriteOnly { set { } } public virtual int V { get; set; } = 7; }
[EventData] class Nest { public string Name { get; set; } = "n"; public Pt P { get; set; } = new Pt { X = 1, Y = 2 }; public Req Inner { get; set; } = new Req(); public Pt? MaybePt { get; set; } }
[EventData] class Scalars
{
    public bool B { get; set; } = true; public byte By { get; set; } = 1; public sbyte Sb { get; set; } = -1; public short S { get; set; } = -2; public ushort Us { get; set; } = 3;
    public int I { get; set; } = -4; public uint U { get; set; } = 4; public long L { get; set; } = -5; public ulong Ul { get; set; } = 5; public float F { get; set; } = 1.5f; public double D { get; set; } = 2.5;
    public char C { get; set; } = 'x'; public Guid G { get; set; } = new Guid("01234567-89ab-cdef-0123-456789abcdef");
    public DateTime T { get; set; } = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc); public DateTimeOffset Dto { get; set; } = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
    public TimeSpan Ts { get; set; } = TimeSpan.FromSeconds(3); public decimal Dec { get; set; } = 1.25m; public IntPtr P { get; set; } = (IntPtr)7; public UIntPtr Up { get; set; } = (UIntPtr)8;
    public Color Col { get; set; } = Color.Green; public Perms Perm { get; set; } = Perms.Read | Perms.Write;
    public string Str { get; set; } = "text"; public string NullStr { get; set; } = null; public string EmptyStr { get; set; } = "";
    public int? NI { get; set; } = 5; public int? NN { get; set; } = null; public Color? NC { get; set; } = Color.Red; public Guid? NG { get; set; }
}
[EventData] class Arrays
{
    public int[] Ints { get; set; } = { 1, 2, 3 }; public long[] Longs { get; set; } = { 4 }; public double[] Doubles { get; set; } = null; public Guid[] Guids { get; set; } = { Guid.Empty };
    public bool[] Bools { get; set; } = { true }; public char[] Chars { get; set; } = { 'a' }; public byte[] Bytes { get; set; } = { 1, 2 }; public short[] Shorts { get; set; } = { -1 }; public float[] Floats { get; set; } = { 1.5f };
    public Color[] Colors { get; set; } = { Color.Red, Color.Green }; public Req[] Reqs { get; set; } = { new Req(), new Req { Url = "v" } }; public int?[] NullableInts { get; set; } = { 1, null };
    public DateTime[] Dates { get; set; } = { new DateTime(2020, 1, 1) }; public decimal[] Decimals { get; set; } = { 1.5m }; public TimeSpan[] Spans { get; set; } = { TimeSpan.Zero }; public int[] Empty { get; set; } = new int[0];
}

class L : EventListener
{
    public string Tag = "wide";
    public static EventLevel Level = EventLevel.Verbose;
    public static EventKeywords Keywords = EventKeywords.All;
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Probe-Write") EnableEvents(source, Level, Keywords);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"[{Tag}] event id={e.EventId} nameNull={e.EventName == null} name={e.EventName} level={e.Level} kw={(long)e.Keywords:x} opcode={e.Opcode} tags={e.Tags} act={Short(e.ActivityId)} rel={Short(e.RelatedActivityId)}");
        Console.WriteLine("  names=" + (e.PayloadNames == null ? "null" : "[" + string.Join(",", e.PayloadNames) + "]"));
        Console.WriteLine("  payload=" + (e.Payload == null ? "null" : "[" + string.Join(" | ", e.Payload.Select(Program.Show)) + "]"));
    }
    static string Short(Guid g) => g == Guid.Empty ? "empty" : g.ToString().Substring(0, 8);
}

[EventSource(Name = "Probe-Write")]
sealed class Src : EventSource
{
    public static Src Log = new();
    [Event(1, Level = EventLevel.Informational)] public void Declared(string what, int n) { WriteEvent(1, what, n); }
    [NonEvent] public void Mixed(string what) { Write("FromHelper", new { What = what, Count = 3 }); }
}

static class Program
{
    public static string Show(object o) => o switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        IDictionary<string, object> d => "{" + string.Join(", ", d.Select(kv => kv.Key + "=" + Show(kv.Value))) + "}",
        IEnumerable e => o.GetType().Name + "[" + string.Join(",", e.Cast<object>().Select(Show)) + "]",
        _ => o.GetType().Name + ":" + Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture),
    };

    static void Filters(Src log)
    {
        log.Write("DefaultLevelVerbose", new { A = 1 });
        log.Write("InfoNoKeywords", new EventSourceOptions { Level = EventLevel.Informational }, new { A = 2 });
        log.Write("InfoKeyword2", new EventSourceOptions { Level = EventLevel.Informational, Keywords = (EventKeywords)2 }, new { A = 3 });
        log.Write("InfoKeyword4", new EventSourceOptions { Level = EventLevel.Informational, Keywords = (EventKeywords)4 }, new { A = 4 });
        log.Write("ErrorKeyword2", new EventSourceOptions { Level = EventLevel.Error, Keywords = (EventKeywords)2 }, new { A = 5 });
    }

    static void Try(string what, Action a)
    {
        Console.WriteLine("-- " + what);
        try { a(); } catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        using var l = new L();
        var log = Src.Log;
        Try("name only", () => log.Write("NameOnly"));
        Try("null name", () => log.Write(null));
        Try("null name with data", () => log.Write(null, new { A = 1 }));
        Try("name and options only", () => log.Write("OptionsOnly", new EventSourceOptions { Level = EventLevel.Critical, Keywords = (EventKeywords)3 }));
        Try("anonymous", () => log.Write("Anon", new { A = 1, B = "two", C = 3.5 }));
        Try("property order", () => log.Write("Ord", new { Z = 1, A = 2, M = 3 }));
        Try("empty anonymous", () => log.Write("Empty", new { }));
        Try("EventData class", () => log.Write("Req", new Req()));
        Try("EventData class, ignore and fields", () => log.Write("Req2", new Req2()));
        Try("EventData struct", () => log.Write("Pt", new Pt { X = 3, Y = 4 }));
        Try("inherited", () => log.Write("Inh", new Inherited()));
        Try("accessors", () => log.Write("Acc", new ReadOnly()));
        Try("scalars", () => log.Write("Scalars", new Scalars()));
        Try("arrays", () => log.Write("Arrays", new Arrays()));
        Try("nested", () => log.Write("Nest", new Nest()));
        Try("nested anonymous", () => log.Write("NestAnon", new { Outer = 1, Inner = new { X = 1, Y = "y" }, Items = new[] { new { X = 1 }, new { X = 2 } } }));
        Try("anonymous enums, nullables", () => log.Write("AnonEnum", new { C = Color.Red, F = Perms.Read, N = (int?)null, M = (int?)3, S = (string)null }));
        Try("options", () => log.Write("Opt", new EventSourceOptions { Level = EventLevel.Warning, Keywords = (EventKeywords)0x10, Opcode = EventOpcode.Start, Tags = (EventTags)0x20, ActivityOptions = EventActivityOptions.Detachable }, new { X = 1 }));
        Try("ref options", () => { var o = new EventSourceOptions { Level = EventLevel.Error }; var d = new { X = 1 }; log.Write("RefOpt", ref o, ref d); });
        Try("level LogAlways", () => log.Write("LA", new EventSourceOptions { Level = EventLevel.LogAlways }, new { X = 1 }));
        Try("opcode stop", () => log.Write("Stop", new EventSourceOptions { Opcode = EventOpcode.Stop }, new { X = 1 }));
        Try("activity ids", () =>
        {
            var o = new EventSourceOptions { Opcode = EventOpcode.Info };
            var a = new Guid("11111111-1111-1111-1111-111111111111"); var r = new Guid("22222222-2222-2222-2222-222222222222"); var d = new { X = 1 };
            log.Write("ActRel", ref o, ref a, ref r, ref d);
            EventSource.SetCurrentThreadActivityId(new Guid("33333333-3333-3333-3333-333333333333"));
            log.Write("ThreadAct", new { X = 1 });
            var empty = Guid.Empty;
            log.Write("ExplicitEmpty", ref o, ref empty, ref r, ref d);
            EventSource.SetCurrentThreadActivityId(Guid.Empty);
        });
        Try("declared and self-describing events mix", () => { log.Declared("declared", 7); log.Mixed("helper"); });
        Try("default struct", () => log.Write("DefaultPt", default(Pt)));

        Console.WriteLine("== a listener with a narrower filter, alone");
        l.Dispose();
        L.Level = EventLevel.Informational;
        L.Keywords = (EventKeywords)2;
        using var narrow = new L { Tag = "narrow" };
        Filters(log);
        Console.WriteLine("== and next to a listener that takes everything: the source-wide filter is the widest");
        L.Level = EventLevel.Verbose;
        L.Keywords = EventKeywords.All;
        using var wide2 = new L { Tag = "wide2" };
        Filters(log);
    }
}
