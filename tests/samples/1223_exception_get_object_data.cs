// Exception.GetObjectData: the entries .NET's base implementation writes, reached through `base.GetObjectData` in an
// override and through a call on the exception's type (which finds the override), plus Exception.HelpLink and Source.
// What csharpc doesn't model stays an explicit NotSupportedException: the library exceptions whose own GetObjectData adds
// entries (ArgumentException's ParamName...).
using System;
using System.Collections;
using System.Runtime.Serialization;

class CodedException : Exception
{
    public int Code;
    public CodedException(string message, int code) : base(message) { Code = code; }
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue("Code", Code);
    }
}

class DerivedCoded : CodedException
{
    public string Extra = "extra";
    public DerivedCoded(string message) : base(message, 9) { }
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue("Extra", Extra);
    }
}

class MessageLess : Exception { }

static class Program
{
    static void Show(Exception e)
    {
        var info = new SerializationInfo(e.GetType(), new FormatterConverter());
        e.GetObjectData(info, new StreamingContext(StreamingContextStates.All));
        Console.WriteLine($"-- {e.GetType().Name} ({info.MemberCount} entries)");
        foreach (var entry in info)
            Console.WriteLine($"{entry.Name} = {(entry.Value is Exception inner ? "[" + inner.Message + "]" : entry.Value is IDictionary d ? "{" + d.Count + "}" : entry.Value ?? "<null>")} ({entry.ObjectType.Name})");
    }

    static void Main()
    {
        Show(new CodedException("boom", 7));
        Show(new DerivedCoded("derived"));
        Exception asBase = new CodedException("as base", 1);
        Show(asBase);
        Show(new MessageLess());
        Show(new Exception());
        Show(new Exception("plain"));
        Show(new InvalidOperationException("op", new FormatException("inner")));
        Show(new NotSupportedException());

        var full = new CodedException("full", 3) { HelpLink = "http://example/help", Source = "my-source", HResult = 42 };
        full.Data["key"] = "value";
        full.Data["other"] = 5;
        Show(full);
        Console.WriteLine(full.HelpLink);
        Console.WriteLine(full.Source);
        Console.WriteLine(new Exception().HelpLink ?? "<null>");
        Console.WriteLine(new Exception().Source ?? "<null>");

        try { new Exception("x").GetObjectData(null!, default); } catch (ArgumentNullException ex) { Console.WriteLine(ex.ParamName); }
    }
}
