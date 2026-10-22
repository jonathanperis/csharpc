using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Multicast delegates and events (docs/03-lowering.md#delegates).
///
/// A delegate is { mt, target, fn, invocationList }. Single-cast delegates have a null list.
/// Delegate.Combine / Remove (runtime/delegates.c) produce a new delegate whose list is a flat
/// array of single-cast delegates; invoking it calls each in order and yields the last result.
/// Field-like events are their (synthesized) backing delegate field; `+=`/`-=` on them combine
/// and remove in place. Events with custom accessors call the user's add/remove methods.
/// </summary>
public sealed partial class FunctionCodeGen
{
    // ======================================================================================
    // Small library surface used around events and locking (single-threaded runtime)
    // ======================================================================================

    private static bool IsLibraryClass(ITypeSymbol? type, string ns, string name) =>
        type is INamedTypeSymbol { ContainingType: null } n && n.Name == name && n.ContainingNamespace.ToDisplayString() == ns;

    /// <summary>`new EventArgs()`, `new object()`-like library classes with no state.</summary>
    private bool TryStatelessLibraryCreation(IObjectCreationOperation creation, out LLVMValueRef result)
    {
        result = default;
        if (creation.Type is not INamedTypeSymbol type || creation.Arguments.Length != 0) return false;
        if (!IsLibraryClass(type, "System", "EventArgs")) return false;
        result = CallRuntime("csrt_alloc", L.Ptr, [L.Ptr, L.I64], _m.DelegateMethodTable(type), L.ConstInt(L.I64, 16));
        return true;
    }

    private bool TryLowLevelLibraryMember(IOperation op, out LLVMValueRef result)
    {
        if (TryThreadingMember(op, out result)) return true;
        result = default;
        switch (op)
        {
            case IFieldReferenceOperation { Field: { IsStatic: true, Name: "Empty" } field } when IsLibraryClass(field.ContainingType, "System", "EventArgs"):
                result = _m.LibrarySingleton(field.ContainingType);
                return true;
            case IPropertyReferenceOperation { Property: { IsStatic: true, Name: "Size" } property }
                when property.ContainingType.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr:
                result = L.ConstInt(L.I32, (long)T.PointerSize);
                return true;
        }
        return false;
    }

    internal LLVMValueRef CombineDelegates(LLVMValueRef a, LLVMValueRef b) =>
        CallRuntime("csrt_delegate_combine", L.Ptr, [L.Ptr, L.Ptr], a, b);

    internal LLVMValueRef RemoveDelegate(LLVMValueRef source, LLVMValueRef value) =>
        CallRuntime("csrt_delegate_remove", L.Ptr, [L.Ptr, L.Ptr], source, value);

    private static bool IsDelegate(ITypeSymbol? type) => type?.TypeKind == TypeKind.Delegate;

    /// <summary>`a + b` / `a - b` on delegates.</summary>
    private LLVMValueRef? TryDelegateBinary(IBinaryOperation op)
    {
        if (!IsDelegate(op.Type) || op.OperatorKind is not (BinaryOperatorKind.Add or BinaryOperatorKind.Subtract)) return null;
        var left = EmitValue(op.LeftOperand);
        var right = EmitValue(op.RightOperand);
        return op.OperatorKind == BinaryOperatorKind.Add ? CombineDelegates(left, right) : RemoveDelegate(left, right);
    }

    /// <summary>`d += x` / `d -= x` on delegate-typed variables, fields and properties.</summary>
    private LLVMValueRef? TryDelegateCompound(ICompoundAssignmentOperation c)
    {
        if (!IsDelegate(c.Target.Type) || c.OperatorKind is not (BinaryOperatorKind.Add or BinaryOperatorKind.Subtract)) return null;
        return ReadModifyWrite(c.Target, old =>
        {
            var value = EmitValue(c.Value);
            var updated = c.OperatorKind == BinaryOperatorKind.Add ? CombineDelegates(old, value) : RemoveDelegate(old, value);
            return (updated, updated);
        });
    }

    private static IFieldSymbol? EventBackingField(IEventSymbol e) =>
        e.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, e));

    private static bool IsFieldLikeEvent(IEventSymbol e) => e.AddMethod is null or { IsImplicitlyDeclared: true };

    private LLVMValueRef EventFieldAddress(IEventReferenceOperation reference)
    {
        var e = reference.Event;
        if (!_m.IsUserType(e.ContainingType)) throw CompileError.Unsupported($"event '{e}' of a library type", reference);
        if (e.IsStatic) return EventBackingField(e) is { } staticField ? _m.StaticField(staticField) : _m.StaticEventField(e);
        var instance = EmitValue(reference.Instance!);
        if (reference.Instance is not IInstanceReferenceOperation) NullCheck(instance);
        if (EventBackingField(e) is { } field) return FieldAddressOn(instance, field);
        var layout = _m.GetLayout(e.ContainingType);
        if (!layout.EventFieldIndex.TryGetValue(e.OriginalDefinition, out var index))
            throw CompileError.Unsupported($"event '{e.Name}' without a backing field", reference);
        return _b.StructGep(layout.InstanceType, instance, index, e.Name);
    }

    /// <summary>Reading a field-like event inside its class (`Changed?.Invoke()`) reads the backing field.</summary>
    private LLVMValueRef EmitEventReference(IEventReferenceOperation reference) =>
        LoadValue(EventFieldAddress(reference), reference.Event.Type);

    private LLVMValueRef EmitEventAssignment(IEventAssignmentOperation assignment)
    {
        var reference = (IEventReferenceOperation)assignment.EventReference;
        var e = reference.Event;
        if (IsFieldLikeEvent(e))
        {
            var address = EventFieldAddress(reference);
            var handler = EmitValue(assignment.HandlerValue);
            var old = _b.Load(L.Ptr, address);
            _b.Store(assignment.Adds ? CombineDelegates(old, handler) : RemoveDelegate(old, handler), address);
            return default;
        }
        var accessor = (assignment.Adds ? e.AddMethod : e.RemoveMethod)!;
        if (!_m.IsUserMethod(accessor)) throw CompileError.Unsupported($"event '{e}' of a library type", assignment);
        var self = e.IsStatic ? default : EmitValue(reference.Instance!);
        var value = EmitValue(assignment.HandlerValue);
        return EmitUserCall(accessor, self, reference.Instance is IInstanceReferenceOperation, [], isVirtual: true, [value]);
    }

    /// <summary>
    /// Invokes `fn(target, args)` for a delegate, looping over the invocation list of a multicast
    /// delegate. <paramref name="call"/> emits one call given (target, fn) and returns its result.
    /// </summary>
    private LLVMValueRef InvokeDelegate(LLVMValueRef d, LLVMTypeRef returnType, Func<LLVMValueRef, LLVMValueRef, LLVMValueRef> call)
    {
        var list = _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 3), "invocation.list");
        var result = returnType.IsVoid ? default : NewTemp(returnType, "delegate.result");
        var single = NewBlock("delegate.single");
        var multi = NewBlock("delegate.multi");
        var done = NewBlock("delegate.done");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, list, LlvmContext.Null(L.Ptr)), single, multi);

        _b.PositionAtEnd(single);
        var r = call(_b.Load(L.Ptr, _b.StructGep(DelegateType, d, 1), "target"), _b.Load(L.Ptr, _b.StructGep(DelegateType, d, 2), "fn"));
        if (!returnType.IsVoid) _b.Store(r, result);
        _b.Br(done);

        _b.PositionAtEnd(multi);
        var header = T.ArrayHeader(L.Ptr);
        var count = _b.Load(L.I64, _b.StructGep(header, list, 1));
        ForEachIndex(_b.Cast(LLVMOpcode.Trunc, count, L.I32), i =>
        {
            var entry = _b.Load(L.Ptr, _b.InBoundsGep(header, list, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), i]));
            var each = call(_b.Load(L.Ptr, _b.StructGep(DelegateType, entry, 1)), _b.Load(L.Ptr, _b.StructGep(DelegateType, entry, 2)));
            if (!returnType.IsVoid) _b.Store(each, result);
        });
        _b.Br(done);

        _b.PositionAtEnd(done);
        return returnType.IsVoid ? default : _b.Load(returnType, result);
    }
}
