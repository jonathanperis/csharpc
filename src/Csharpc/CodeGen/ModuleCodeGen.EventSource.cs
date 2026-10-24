using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// CoreIntrinsics.EventSourceAttribute(type, which): the Name (0) or Guid (1) of [EventSource] on the
/// class a Type object describes, for EventSource.GetName/GetGuid and the EventSource constructors
/// (corelib/Tracing/EventSource.cs). A compile-time table, not reflection: the function compares the
/// Type's method table with every class the program has a layout for, so its body is generated last,
/// once all classes are known. Programs that never construct an EventSource don't have the function.
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
            var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "EventSourceAttribute" } c
                && c.ContainingNamespace?.ToDisplayString() == "System.Diagnostics.Tracing");
            if (attribute is null || layout.MethodTable.IsNull) continue;
            string? Named(string name) => attribute.NamedArguments.FirstOrDefault(n => n.Key == name).Value.Value as string;
            var (name, guid) = (Named("Name"), Named("Guid"));
            if (name is null && guid is null) continue;
            var hit = b.AppendBlock(fn, "eventsource.hit");
            var next = b.AppendBlock(fn, "eventsource.next");
            b.CondBr(b.ICmp(LLVMIntPredicate.EQ, target, layout.MethodTable), hit, next);
            b.PositionAtEnd(hit);
            LLVMValueRef Text(string? s) => s is null ? LlvmContext.Null(Llvm.Ptr) : StringLiteral(s);
            b.Ret(b.Select(b.ICmp(LLVMIntPredicate.EQ, which, Llvm.ConstInt(Llvm.I32, 0)), Text(name), Text(guid)));
            b.PositionAtEnd(next);
        }
        b.Ret(LlvmContext.Null(Llvm.Ptr));
    }
}
