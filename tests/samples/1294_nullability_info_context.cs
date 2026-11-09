// System.Reflection.NullabilityInfoContext over properties, fields, parameters and events of members in a
// nullable-annotated file (and one that is oblivious): ReadState/WriteState, element types, generic arguments,
// the [AllowNull]/[MaybeNull]/[NotNull]/[DisallowNull] adjustments, return values and [NullableContext] inherited from the type.
#nullable enable
#pragma warning disable CS0649, CS0169, CS0067
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

class Sample
{
    public string NotNullString = "";
    public string? NullableString;
    public int Number;
    public int? NullableNumber;
    public List<string?> ListOfMaybe = new();
    public List<string>? MaybeListOfStrings;
    public Dictionary<string, List<string?>?> Nested = new();
    public string[] Array = new string[0];
    public string?[]? MaybeArrayOfMaybe;
    public (string, string?) Tuple;
    public KeyValuePair<string?, int?> Pair;
    public object Obj = new();
    public object? MaybeObj;

    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string ReadOnlyName => "";
    public string? WriteOnly { set { } }
    [AllowNull] public string AllowNullProperty { get; set; } = "";
    [MaybeNull] public string MaybeNullProperty { get; set; } = "";
    [DisallowNull] public string? DisallowNullProperty { get; set; }
    [NotNull] public string? NotNullProperty { get; set; }
    public int Count { get; set; }
    private string? PrivateMaybe { get; set; }
    internal string InternalName { get; set; } = "";

    public event EventHandler? Changed;
    public event EventHandler Ready = delegate { };

    public string Echo(string text, string? other, int n, List<string?> items) => text;

    public string? Find([AllowNull] string key, [DisallowNull] string? value) => null;

    public Sample(string required, string? optional) { }
}

class Box<T>
{
    public T Value = default!;
    public T? MaybeValue;
    public T Item { get; set; } = default!;
}

#nullable disable
class Oblivious
{
    public string Name;
    public List<string> Items;
    public int Number;
    public string Prop { get; set; }
}
#nullable enable

static class Program
{
    static string Describe(NullabilityInfo info)
    {
        var sb = new StringBuilder();
        sb.Append(info.Type.Name).Append(" read=").Append(info.ReadState).Append(" write=").Append(info.WriteState);
        if (info.ElementType != null) sb.Append(" element=[").Append(Describe(info.ElementType)).Append(']');
        if (info.GenericTypeArguments.Length > 0)
        {
            sb.Append(" args=[");
            for (int i = 0; i < info.GenericTypeArguments.Length; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(Describe(info.GenericTypeArguments[i]));
            }
            sb.Append(']');
        }
        return sb.ToString();
    }

    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    static void Fields([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var context = new NullabilityInfoContext();
        foreach (var name in Names(type, fields: true))
            Console.WriteLine($"{type.Name}.{name}: {Describe(context.Create(type.GetField(name, All)!))}");
    }

    static void Properties([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var context = new NullabilityInfoContext();
        foreach (var name in Names(type, fields: false))
            Console.WriteLine($"{type.Name}.{name}: {Describe(context.Create(type.GetProperty(name, All)!))}");
    }

    static List<string> Names([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, bool fields)
    {
        var names = new List<string>();
        if (fields) foreach (var f in type.GetFields(All)) { if (!f.Name.Contains('<') && f.FieldType != typeof(EventHandler)) names.Add(f.Name); }
        else foreach (var p in type.GetProperties(All)) names.Add(p.Name);
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    static void Main()
    {
        Fields(typeof(Sample));
        Properties(typeof(Sample));

        var context = new NullabilityInfoContext();
        foreach (var name in new[] { "Changed", "Ready" })
            Console.WriteLine($"event {name}: {Describe(context.Create(typeof(Sample).GetEvent(name)!))}");

        foreach (var parameter in typeof(Sample).GetMethod("Echo")!.GetParameters())
            Console.WriteLine($"Echo({parameter.Name}): {Describe(context.Create(parameter))}");
        Console.WriteLine($"Echo return: {Describe(context.Create(typeof(Sample).GetMethod("Echo")!.ReturnParameter))}");
        foreach (var parameter in typeof(Sample).GetMethod("Find")!.GetParameters())
            Console.WriteLine($"Find({parameter.Name}): {Describe(context.Create(parameter))}");
        Console.WriteLine($"Find return: {Describe(context.Create(typeof(Sample).GetMethod("Find")!.ReturnParameter))}");
        foreach (var parameter in typeof(Sample).GetConstructors()[0].GetParameters())
            Console.WriteLine($"ctor({parameter.Name}): {Describe(context.Create(parameter))}");

        Fields(typeof(Box<>));
        Fields(typeof(Box<string>));
        Fields(typeof(Box<string?>));
        Fields(typeof(Box<int>));
        Properties(typeof(Box<string>));
        Fields(typeof(Oblivious));
        Properties(typeof(Oblivious));

        // The same context answers repeated and interleaved questions.
        var again = context.Create(typeof(Sample).GetField("NullableString")!);
        Console.WriteLine($"again: {Describe(again)}");

        try { context.Create((PropertyInfo)null!); } catch (ArgumentNullException e) { Console.WriteLine($"null property: {e.ParamName}"); }
        try { context.Create((FieldInfo)null!); } catch (ArgumentNullException e) { Console.WriteLine($"null field: {e.ParamName}"); }
        try { context.Create((ParameterInfo)null!); } catch (ArgumentNullException e) { Console.WriteLine($"null parameter: {e.ParamName}"); }
        try { context.Create((EventInfo)null!); } catch (ArgumentNullException e) { Console.WriteLine($"null event: {e.ParamName}"); }
    }
}
