using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Multicast delegates and events (docs/03-lowering.md#delegates).
///
/// A delegate is { mt, target, fn, invocationList }. Single-cast delegates have a null list.
/// Delegate.Combine / Remove (src/runtime/delegates.c) produce a new delegate whose list is a flat
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

    internal LLVMValueRef DelegateEquals(LLVMValueRef a, LLVMValueRef b) =>
        CallRuntime("csrt_delegate_equals", L.I1, [L.Ptr, L.Ptr], a, b);

    internal LLVMValueRef CombineDelegates(LLVMValueRef a, LLVMValueRef b) =>
        CallRuntime("csrt_delegate_combine", L.Ptr, [L.Ptr, L.Ptr], a, b);

    internal LLVMValueRef RemoveDelegate(LLVMValueRef source, LLVMValueRef value) =>
        CallRuntime("csrt_delegate_remove", L.Ptr, [L.Ptr, L.Ptr], source, value);

    private static bool IsDelegate(ITypeSymbol? type) => type?.TypeKind == TypeKind.Delegate;

    /// <summary>`a + b` / `a - b` on delegates.</summary>
    private LLVMValueRef? TryDelegateBinary(IBinaryOperation op)
    {
        // d1 == d2 / d1 != d2: Delegate's operators compare invocation lists, not references.
        static bool DelegateTyped(ITypeSymbol? t) =>
            t is { TypeKind: TypeKind.Delegate } || t?.SpecialType is SpecialType.System_Delegate or SpecialType.System_MulticastDelegate;
        if (op.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            && DelegateTyped(op.LeftOperand.Type) && DelegateTyped(op.RightOperand.Type)
            && (op.OperatorMethod is null || op.OperatorMethod.ContainingType.SpecialType is SpecialType.System_Delegate or SpecialType.System_MulticastDelegate))
        {
            var equal = DelegateEquals(EmitValue(op.LeftOperand), EmitValue(op.RightOperand));
            return op.OperatorKind == BinaryOperatorKind.Equals ? equal : _b.Not(equal);
        }
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
        if (e.IsStatic) return EventBackingField(e) is { } staticField ? StaticFieldAddress(staticField) : _m.StaticEventField(e);
        var instance = EmitValue(reference.Instance!);
        if (reference.Instance is not IInstanceReferenceOperation) NullCheck(instance);
        if (EventBackingField(e) is { } field) return FieldAddressOn(instance, field);
        var layout = _m.GetLayout(e.ContainingType);
        if (!layout.EventFieldIndex.TryGetValue(e.OriginalDefinition, out var index))
            throw CompileError.Unsupported($"event '{e.Name}' without a backing field", reference);
        return _b.StructGep(layout.InstanceType, instance, index, e.Name);
    }

    /// <summary>
    /// The synthesized add/remove accessor of a field-like event, as a function: `+=`/`-=` inside
    /// the program update the field inline, but interface slots and virtual calls (an event that
    /// implements INotifyPropertyChanged.PropertyChanged) need the accessor itself.
    /// </summary>
    public void EmitFieldLikeEventAccessor()
    {
        var e = (IEventSymbol)_method!.AssociatedSymbol!;
        LLVMValueRef address;
        if (e.IsStatic) address = EventBackingField(e) is { } staticField ? _m.StaticField(staticField) : _m.StaticEventField(e);
        else if (EventBackingField(e) is { } field) address = FieldAddressOn(_this, field);
        else address = _b.StructGep(_m.GetLayout(e.ContainingType).InstanceType, _this, _m.GetLayout(e.ContainingType).EventFieldIndex[e.OriginalDefinition], e.Name);
        var handler = _fn.Fn.Param(e.IsStatic ? 0u : 1u);
        var old = _b.Load(L.Ptr, address);
        _b.Store(_method.MethodKind == MethodKind.EventAdd ? CombineDelegates(old, handler) : RemoveDelegate(old, handler), address);
        _b.RetVoid();
        Finish();
    }

    /// <summary>Reading a field-like event inside its class (`Changed?.Invoke()`) reads the backing field.</summary>
    private LLVMValueRef EmitEventReference(IEventReferenceOperation reference) =>
        LoadValue(EventFieldAddress(reference), reference.Event.Type);

    private LLVMValueRef EmitEventAssignment(IEventAssignmentOperation assignment)
    {
        var reference = (IEventReferenceOperation)assignment.EventReference;
        var e = reference.Event;
        if (e.ContainingType.TypeKind == TypeKind.Interface && !e.IsStatic)
        {
            // `itf.Changed += handler`: an interface call to the add/remove accessor. The receiver is
            // evaluated inside EmitInterfaceCall, after the handler, which is only unobservable when
            // creating the handler has no side effects.
            if (assignment.HandlerValue is not (IDelegateCreationOperation or ILocalReferenceOperation or IParameterReferenceOperation
                or IFieldReferenceOperation { Instance: null or IInstanceReferenceOperation }))
                throw CompileError.Unsupported($"interface event '{e}' with a handler expression that may have side effects", assignment);
            var interfaceAccessor = (assignment.Adds ? e.AddMethod : e.RemoveMethod)!;
            EmitInterfaceCall(interfaceAccessor, reference.Instance!, [], [EmitValue(assignment.HandlerValue)], null);
            return default;
        }
        if (IsFieldLikeEvent(e))
        {
            var address = EventFieldAddress(reference);
            var handler = EmitValue(assignment.HandlerValue);
            var old = _b.Load(L.Ptr, address);
            _b.Store(assignment.Adds ? CombineDelegates(old, handler) : RemoveDelegate(old, handler), address);
            return default;
        }
        // A corelib shadow's event (FileSystemWatcher.Created) binds to the shadow's accessor.
        var accessor = S(_m.CoreLib.Map((assignment.Adds ? e.AddMethod : e.RemoveMethod)!));
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
