using System.Globalization;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// CoreIntrinsics.EventSourceAttribute(type, which): the Name (0) or Guid (1) of [EventSource] on the
/// class a Type object describes, and (2) the [Event] methods of an EventSource class, for EventSource.GetName/
/// GetGuid, the EventSource constructors and the listener side (corelib/Tracing/EventSource.cs,
/// EventListener.cs). A compile-time table, not reflection: the function compares the Type's method table
/// with every class the program has a layout for, so its body is generated last, once all classes are
/// known. Programs that never construct an EventSource don't have the function.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private LoweredFunction? _eventSourceAttribute;

    internal LoweredFunction EventSourceAttributeFunction()
    {
        if (_eventSourceAttribute is { } existing) return existing;
        var type = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.I32);
        var fn = Module.AddFunction("csharpc.eventsource.attribute", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        _eventSourceAttribute = new LoweredFunction(fn, type);
        return _eventSourceAttribute.Value;
    }

    private void FinishEventSourceAttributes()
    {
        if (_eventSourceAttribute is not { } lowered) return;
        var fn = lowered.Fn;
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        // A Type object is { Type_MT, described method table } (runtime/type.c): the second pointer.
        var target = b.Load(Llvm.Ptr, b.InBoundsGep(Llvm.Ptr, fn.Param(0), [Llvm.ConstInt(Llvm.I64, 1)]));
        var which = fn.Param(1);
        foreach (var (type, layout) in _layouts.ToList())
        {
            if (layout.MethodTable.IsNull) continue;
            var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "EventSourceAttribute" } c
                && c.ContainingNamespace?.ToDisplayString() == "System.Diagnostics.Tracing");
            string? Named(string name) => attribute?.NamedArguments.FirstOrDefault(n => n.Key == name).Value.Value as string;
            var (name, guid) = (Named("Name"), Named("Guid"));
            var events = IsEventSourceClass(type) ? EventsBlob(type) : null;
            if (name is null && guid is null && events is null) continue;
            var hit = b.AppendBlock(fn, "eventsource.hit");
            var next = b.AppendBlock(fn, "eventsource.next");
            b.CondBr(b.ICmp(LLVMIntPredicate.EQ, target, layout.MethodTable), hit, next);
            b.PositionAtEnd(hit);
            LLVMValueRef Text(string? s) => s is null ? LlvmContext.Null(Llvm.Ptr) : StringLiteral(s);
            var isName = b.ICmp(LLVMIntPredicate.EQ, which, Llvm.ConstInt(Llvm.I32, 0));
            var isGuid = b.ICmp(LLVMIntPredicate.EQ, which, Llvm.ConstInt(Llvm.I32, 1));
            b.Ret(b.Select(isName, Text(name), b.Select(isGuid, Text(guid), Text(events))));
            b.PositionAtEnd(next);
        }
        b.Ret(LlvmContext.Null(Llvm.Ptr));
    }

    private static bool IsEventSourceClass(INamedTypeSymbol type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            if (t is { Name: "EventSource", ContainingNamespace: { } ns } && ns.ToDisplayString() == "System.Diagnostics.Tracing") return true;
        return false;
    }

    private const char RecordSeparator = '\u001e', FieldSeparator = '\u001f';

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The [Event] methods of an EventSource class as the text corelib/Tracing/EventListener.cs parses (what .NET
    /// reads by reflection when it builds an EventSource's manifest): records separated by U+001E, fields by
    /// U+001F. "k" lists the values of the constants in the nested Keywords class, "o" those of Opcodes; each "e"
    /// is an event: id, method name, level, keywords, opcode, task, version, channel, tags, activity options,
    /// whether it has a message and the message, the number of parameters and a (type code, name) pair for each.
    /// </summary>
    private static string EventsBlob(INamedTypeSymbol type)
    {
        var records = new List<string>();
        string Defined(string nested) => string.Join(FieldSeparator,
            type.GetTypeMembers(nested).SelectMany(t => t.GetMembers().OfType<IFieldSymbol>())
                .Where(f => f.IsConst && f.ConstantValue is not null)
                .Select(f => Invariant(Convert.ToInt64(f.ConstantValue, CultureInfo.InvariantCulture))));
        records.Add("k" + FieldSeparator + Defined("Keywords"));
        records.Add("o" + FieldSeparator + Defined("Opcodes"));
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary || method.IsStatic) continue;
            var attribute = method.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "EventAttribute" } c
                && c.ContainingNamespace?.ToDisplayString() == "System.Diagnostics.Tracing");
            if (attribute is null || attribute.ConstructorArguments.Length != 1) continue;
            long Number(string name, long fallback) =>
                attribute.NamedArguments.FirstOrDefault(n => n.Key == name) is { Key: not null } n && n.Value.Value is { } v
                    ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : fallback;
            var message = attribute.NamedArguments.FirstOrDefault(n => n.Key == "Message").Value.Value as string;
            var fields = new List<string>
            {
                "e", Invariant(Convert.ToInt64(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture)), method.Name,
                Invariant(Number("Level", 4)), Invariant(Number("Keywords", 0)), Invariant(Number("Opcode", 0)), Invariant(Number("Task", 0)),
                Invariant(Number("Version", 0)), Invariant(Number("Channel", 0)), Invariant(Number("Tags", 0)), Invariant(Number("ActivityOptions", 0)),
                message is null ? "0" : "1", message ?? "", Invariant(method.Parameters.Length),
            };
            foreach (var parameter in method.Parameters)
            {
                fields.Add(EventParameterCode(parameter.Type));
                fields.Add(parameter.Name);
            }
            records.Add(string.Join(FieldSeparator, fields));
        }
        return string.Join(RecordSeparator, records);
    }

    /// <summary>The kind of an event parameter as the listener side decodes it; "!Name" for a type .NET rejects in an event source.</summary>
    private static string EventParameterCode(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }) return "E" + EventParameterCode(underlying);
        return type.SpecialType switch
        {
            SpecialType.System_Boolean => "b", SpecialType.System_Byte => "B", SpecialType.System_SByte => "sb", SpecialType.System_Char => "c",
            SpecialType.System_Int16 => "s", SpecialType.System_UInt16 => "us", SpecialType.System_Int32 => "i", SpecialType.System_UInt32 => "ui",
            SpecialType.System_Int64 => "l", SpecialType.System_UInt64 => "ul", SpecialType.System_Single => "f", SpecialType.System_Double => "d",
            SpecialType.System_String => "S", SpecialType.System_IntPtr => "p", SpecialType.System_UIntPtr => "up", SpecialType.System_DateTime => "D",
            _ => type switch
            {
                { Name: "Guid", ContainingNamespace.Name: "System" } => "G",
                IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte } => "Y",
                _ => "!" + type.Name,
            },
        };
    }
}
