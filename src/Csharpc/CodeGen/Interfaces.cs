using System.Collections.Immutable;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Interface dispatch (docs/03-lowering.md#interfaces).
///
/// Every interface (user or library, each constructed generic interface separately) gets an
/// identity "method table" global flagged CSRT_MT_INTERFACE. Every class method table carries an
/// interface map: one { interface MT, slot table } entry per implemented interface, flattened over
/// the class hierarchy and interface inheritance, where the slot table lists the implementing
/// functions in the interface's member order.
///
///   call:  slots = csrt_interface_slots(obj, @IFoo.MT)   ; linear scan of a short map
///          fn    = slots[index of member in IFoo]
///
/// When the receiver's static type is a concrete class (very common after monomorphization:
/// `T : IComparable&lt;T&gt;` with T = Person) the call is devirtualized at compile time instead.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _interfaceTables = new(SymbolEqualityComparer.Default);

    public LLVMValueRef InterfaceMethodTable(INamedTypeSymbol itf)
    {
        if (_interfaceTables.TryGetValue(itf, out var g)) return g;
        if (TypeSubstitution.IsOpen(itf)) throw CompileError.Unsupported($"open generic interface '{itf}'", itf);
        var init = Llvm.ConstStruct(LlvmContext.Null(Llvm.Ptr), CString(Mangler.MetadataName(itf)),
            Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 2 /* CSRT_MT_INTERFACE */),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        g = Module.AddGlobal(Mangler.TypeSymbol(itf, "IMT"), Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        Native.LLVMSetAlignment(g, 8);
        return _interfaceTables[itf] = g;
    }

    /// <summary>Dispatchable members of one interface (not inherited ones), in slot order.</summary>
    public static List<IMethodSymbol> InterfaceSlots(INamedTypeSymbol itf) =>
        [.. itf.GetMembers().OfType<IMethodSymbol>().Where(m => !m.IsStatic && (m.IsAbstract || m.IsVirtual))];

    public static int InterfaceSlotIndex(INamedTypeSymbol itf, IMethodSymbol member)
    {
        var slots = InterfaceSlots(itf);
        var index = slots.FindIndex(m => SymbolEqualityComparer.Default.Equals(m, member));
        if (index < 0) index = slots.FindIndex(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, member.OriginalDefinition));
        return index >= 0 ? index : throw CompileError.Unsupported($"interface member '{member}'", member);
    }

    /// <summary>{ ptr interfaceMap, i32 count } for a class's method table.</summary>
    private (LLVMValueRef Map, int Count) EmitInterfaceMap(ClassLayout layout)
    {
        var interfaces = layout.Type.AllInterfaces;
        if (interfaces.IsEmpty) return (LlvmContext.Null(Llvm.Ptr), 0);
        var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
        List<LLVMValueRef> entries = [];
        foreach (var itf in interfaces)
        {
            var slots = InterfaceSlots(itf).Select(member => ImplementationFor(layout, member)).ToArray();
            var slotsInit = LlvmContext.ConstArray(Llvm.Ptr, slots);
            var slotTable = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "ITBL" + entries.Count), Native.LLVMTypeOf(slotsInit), slotsInit,
                constant: true, linkage: LLVMLinkage.Internal);
            entries.Add(LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(itf), slotTable));
        }
        var mapInit = LlvmContext.ConstArray(entryType, [.. entries]);
        var map = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "IMAP"), Native.LLVMTypeOf(mapInit), mapInit,
            constant: true, linkage: LLVMLinkage.Internal);
        return (map, entries.Count);
    }

    private LLVMValueRef ImplementationFor(ClassLayout layout, IMethodSymbol member)
    {
        var impl = layout.Type.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        if (impl is null)
            return member.IsAbstract ? LlvmContext.Null(Llvm.Ptr) : GetFunction(member).Fn; // default interface method
        // A virtual implementation may be overridden further down: take this class's vtable entry.
        if (impl.ContainingType.TypeKind != TypeKind.Interface && (impl.IsVirtual || impl.IsOverride || impl.IsAbstract)
            && layout.SlotOf.TryGetValue(ClassLayout.RootOf(impl), out var slot))
            impl = layout.VTable[slot];
        if (impl is null || impl.IsAbstract) return LlvmContext.Null(Llvm.Ptr);
        if (!IsUserMethod(impl)) return LlvmContext.Null(Llvm.Ptr); // e.g. object.ToString satisfying an interface
        return GetFunction(impl).Fn;
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>foreach-over-array plumbing goes through the array intrinsics, not interface dispatch.</summary>
    private bool IsArrayEnumeration(IOperation receiver)
    {
        if (TryArrayEnumerator(receiver, out _)) return true;
        var source = receiver;
        while (source is IConversionOperation { Conversion.IsImplicit: true } c) source = c.Operand;
        return TypeOf(source) is IArrayTypeSymbol;
    }

    /// <summary>Peels implicit conversions (boxing, upcasts to the interface) off a receiver.</summary>
    internal IOperation ConcreteReceiver(IOperation receiver)
    {
        var source = receiver;
        while (source is IConversionOperation { Conversion.IsImplicit: true, OperatorMethod: null } c && TypeOf(c.Operand) is { } t
               && t.TypeKind is not TypeKind.TypeParameter)
            source = c.Operand;
        return source;
    }

    internal LLVMValueRef EmitInterfaceCall(IMethodSymbol target, IOperation receiver, ImmutableArray<IArgumentOperation> arguments,
        LLVMValueRef[] extra, IInvocationOperation? invocation)
    {
        var concrete = ConcreteReceiver(receiver);
        var staticType = TypeOf(concrete);

        // Devirtualize when the receiver's type is known to be a class / primitive.
        if (staticType is not null && staticType.TypeKind is not (TypeKind.Interface or TypeKind.TypeParameter)
            && staticType.FindImplementationForInterfaceMember(target) is IMethodSymbol impl)
        {
            if (_m.IsUserMethod(impl) && impl.ContainingType.TypeKind == TypeKind.Class)
            {
                var self = EmitValue(concrete);
                return EmitUserCall(impl, self, concrete is IInstanceReferenceOperation, arguments, isVirtual: true, extra.Length > 0 ? extra : null);
            }
            if (invocation is not null && Intrinsics.TryGet(impl, out var handler)) return handler(this, invocation);
            if (invocation is not null && BclValueTypes.TryGet(impl, out var bcl)) return CallBcl(bcl, impl, concrete, invocation.Arguments, invocation);
            if (staticType.IsValueType) throw CompileError.Unsupported($"interface call '{target}' on value type '{staticType}'", receiver);
        }

        var receiverValue = EmitValue(receiver);
        NullCheck(receiverValue);
        var args = new LLVMValueRef[1 + target.Parameters.Length];
        args[0] = receiverValue;
        foreach (var argument in arguments) args[1 + argument.Parameter!.Ordinal] = EmitArgument(argument, target);
        for (var i = 0; i < extra.Length; i++) args[args.Length - extra.Length + i] = extra[i];
        return InterfaceDispatch(target, receiverValue, args);
    }

    private LLVMValueRef InterfaceDispatch(IMethodSymbol target, LLVMValueRef self, LLVMValueRef[] args)
    {
        var itf = target.ContainingType;
        var index = ModuleCodeGen.InterfaceSlotIndex(itf, target);
        var slots = CallRuntime("csrt_interface_slots", L.Ptr, [L.Ptr, L.Ptr], self, _m.InterfaceMethodTable(itf));
        var slotAddress = _b.InBoundsGep(L.I8, slots, [L.ConstUInt(L.I64, (ulong)index * T.PointerSize)], "islot");
        var fn = _b.Load(L.Ptr, slotAddress, "ifn");
        return _b.Call(_m.FunctionTypeOf(target), fn, args);
    }
}
