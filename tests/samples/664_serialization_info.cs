// SerializationInfo (what ISerializable.GetObjectData implementations still compile against): AddValue,
// typed getters with conversion, duplicate/missing members, the enumerator, MemberCount and type names.
#pragma warning disable SYSLIB0050, SYSLIB0051
using System;
using System.Runtime.Serialization;

class Point { }

class Program
{
    static void Main()
    {
        var info = new SerializationInfo(typeof(Point), new FormatterConverter());
        info.AddValue("x", 3);
        info.AddValue("name", "pt");
        info.AddValue("ratio", 0.5);
        info.AddValue("flag", true);
        info.AddValue("when", new DateTime(2024, 5, 6));
        info.AddValue("boxed", (object)7L, typeof(long));
        info.AddValue("nothing", null, typeof(string));
        Console.WriteLine($"{info.MemberCount} {info.FullTypeName} {info.ObjectType.Name} {info.IsFullTypeNameSetExplicit} {info.IsAssemblyNameSetExplicit}");
        Console.WriteLine($"{info.GetInt32("x")} {info.GetString("name")} {info.GetDouble("ratio")} {info.GetBoolean("flag")} {info.GetDateTime("when"):yyyy-MM-dd}");
        Console.WriteLine($"{info.GetInt64("x")} {info.GetValue("boxed", typeof(long))} {info.GetString("nothing") == null}");
        foreach (SerializationEntry e in info) Console.Write($"{e.Name}:{e.ObjectType.Name} ");
        Console.WriteLine();
        try { info.AddValue("x", 4); } catch (SerializationException e) { Console.WriteLine(e.Message); }
        try { info.GetInt32("missing"); } catch (SerializationException e) { Console.WriteLine(e.Message); }
        try { info.AddValue(null!, 1); } catch (ArgumentNullException e) { Console.WriteLine(e.ParamName); }
        info.FullTypeName = "Other.Type";
        Console.WriteLine($"{info.FullTypeName} {info.IsFullTypeNameSetExplicit}");
        var en = info.GetEnumerator();
        try { _ = en.Current; } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
    }
}
