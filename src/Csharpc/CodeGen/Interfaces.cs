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
        itf = (INamedTypeSymbol)CoreLib.Unmap(TypeSubstitution.EraseTupleNames(itf, Compilation));
        if (_interfaceTables.TryGetValue(itf, out var g)) return g;
        if (TypeSubstitution.IsOpen(itf)) throw CompileError.Unsupported($"open generic interface '{itf}'", itf);
        var init = Llvm.ConstStruct(LlvmContext.Null(Llvm.Ptr), CString(Mangler.MetadataName(itf)),
            Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 2 /* CSRT_MT_INTERFACE */),
            LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        g = Module.AddGlobal(Mangler.TypeSymbol(itf, "IMT"), Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        Native.LLVMSetAlignment(g, 8);
        NoteInterfaceInstantiation(itf);
        return _interfaceTables[itf] = g;
    }

    /// <summary>Dispatchable members of one interface (not inherited ones), in slot order.</summary>
    public static List<IMethodSymbol> InterfaceSlots(INamedTypeSymbol itf) =>
        // Generic interface methods (generic virtual methods) have no slots: they'd need one per instantiation.
        [.. itf.GetMembers().OfType<IMethodSymbol>().Where(m => !m.IsStatic && (m.IsAbstract || m.IsVirtual) && !m.IsGenericMethod)];

    public static int InterfaceSlotIndex(INamedTypeSymbol itf, IMethodSymbol member)
    {
        var slots = InterfaceSlots(itf);
        var index = slots.FindIndex(m => SymbolEqualityComparer.Default.Equals(m, member));
        if (index < 0) index = slots.FindIndex(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, member.OriginalDefinition));
        return index >= 0 ? index : throw CompileError.Unsupported($"interface member '{member}'", member);
    }

    /// <summary>
    /// A class method table's interface map, whose slot tables FinishMethodTables fills: the interface
    /// method tables are created now, so the builtin-type and variance registrations in main see them.
    /// </summary>
    private void EmitInterfaceMap(PendingMethodTable table)
    {
        var layout = table.Layout;
        var interfaces = layout.Type.AllInterfaces;
        if (interfaces.IsEmpty) return;
        var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
        List<LLVMValueRef> entries = [];
        foreach (var itf in interfaces)
        {
            var members = InterfaceSlots(itf);
            var slotTable = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "ITBL" + entries.Count),
                LlvmContext.Array(Llvm.Ptr, (ulong)members.Count), constant: true, linkage: LLVMLinkage.Internal);
            table.Interfaces.Add((itf, members, slotTable));
            entries.Add(LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(itf), slotTable));
        }
        var mapInit = LlvmContext.ConstArray(entryType, [.. entries]);
        table.InterfaceMap = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "IMAP"), Native.LLVMTypeOf(mapInit), mapInit,
            constant: true, linkage: LLVMLinkage.Internal);
    }

    private INamedTypeSymbol? ShadowedBaseOf(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (CoreLib.TryMap(b) is { } shadow) return shadow;
        return null;
    }

    private LLVMValueRef ImplementationFor(ClassLayout layout, IMethodSymbol member)
    {
        var impl = layout.Type.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        if (impl is not null) impl = CoreLib.Map(impl); // implemented by a shadowed library base class
        // A class deriving from a shadowed library class (class Names : List<string>): reference assemblies
        // don't list the base's explicit implementations, so ask the corelib shadow that implements it.
        if ((impl is null || !IsUserMethod(impl)) && ShadowedBaseOf(layout.Type) is { } shadowBase
            && shadowBase.FindImplementationForInterfaceMember(member) is IMethodSymbol shadowImpl)
            impl = shadowImpl;
        if (impl is null)
            return member.IsAbstract ? LlvmContext.Null(Llvm.Ptr) : GetFunction(member).Fn; // default interface method
        // A virtual implementation may be overridden further down: take this class's vtable entry.
        if (impl.ContainingType.TypeKind != TypeKind.Interface && (impl.IsVirtual || impl.IsOverride || impl.IsAbstract)
            && layout.SlotOf.TryGetValue(RootOf(impl), out var slot))
            impl = layout.VTable[slot];
        if (impl is null || impl.IsAbstract) return LlvmContext.Null(Llvm.Ptr);
        // A class deriving from CancellationTokenSource: IDisposable.Dispose is Dispose(true) through its vtable.
        if (!IsUserMethod(impl) && IsRuntimeCancellationSource(impl.ContainingType) && impl.Name == "Dispose" && impl.Parameters.IsEmpty)
            return RuntimeSlotFunction("csrt_cts_dispose_virtual");
        if (!IsUserMethod(impl)) return LlvmContext.Null(Llvm.Ptr); // e.g. object.ToString satisfying an interface
        return SlotFunction(impl);
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

    /// <summary>
    /// A struct's public method matching an interface member by name and arity. Corelib shadow structs
    /// (List&lt;T&gt;.Enumerator...) implement the BCL interfaces by pattern rather than declaration.
    /// </summary>
    private static IMethodSymbol? PatternImplementation(ITypeSymbol type, IMethodSymbol member) =>
        type.GetMembers(member.Name).OfType<IMethodSymbol>()
            .FirstOrDefault(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == member.Parameters.Length);

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
            && (staticType.FindImplementationForInterfaceMember(target) as IMethodSymbol
                ?? (staticType.IsValueType ? PatternImplementation(staticType, target) : null)) is IMethodSymbol impl)
        {
            if (_m.IsUserMethod(impl) && impl.ContainingType.TypeKind == TypeKind.Class)
            {
                var self = EmitValue(concrete);
                return EmitUserCall(impl, self, concrete is IInstanceReferenceOperation, arguments, isVirtual: true, extra.Length > 0 ? extra : null);
            }
            // Constrained call on a struct (e.g. `using`/foreach disposing a struct enumerator): no boxing,
            // `this` is the receiver's address.
            if (_m.IsUserMethod(impl) && impl.ContainingType.IsValueType)
                return EmitUserCall(impl, AddressOrSpill(concrete), selfIsNonNull: true, arguments, isVirtual: false, extra.Length > 0 ? extra : null);
            if (invocation is not null && Intrinsics.TryGet(impl, out var handler)) return handler(this, invocation);
            if (invocation is not null && BclValueTypes.TryGet(impl, out var bcl)) return CallBcl(bcl, impl, concrete, invocation.Arguments, invocation);
            // A corelib [ShadowInstance] member implementing it (int.TryFormat for ISpanFormattable...).
            // (Value types only: a class's shadow member may itself make the interface call, as Array.GetEnumerator does.)
            if (invocation is not null && staticType.IsValueType && !_m.IsUserMethod(impl) && _m.CoreLib.Map(impl) is var mappedImpl
                && !SymbolEqualityComparer.Default.Equals(mappedImpl, impl) && _m.IsUserMethod(mappedImpl) && CoreLib.IsInstanceShadow(mappedImpl))
                return EmitUserCall(mappedImpl, EmitValue(concrete), selfIsNonNull: true, invocation.Arguments, isVirtual: false, extra.Length > 0 ? extra : null);
            if (staticType.IsValueType && !ModuleCodeGen.PrimitiveSlotSupported(staticType, target.ContainingType, target))
                throw CompileError.Unsupported($"interface call '{target}' on value type '{staticType}'", receiver);
        }
        // A primitive used through an interface csharpc gives boxed primitives (IComparable, IFormattable...):
        // what `constrained.` does for a value type without a direct implementation path here.
        LLVMValueRef? boxed = staticType is { IsValueType: true } primitive && ModuleCodeGen.PrimitiveSlotSupported(primitive, target.ContainingType, target)
            ? Box(EmitValue(concrete), primitive, receiver) : null;

        // Arrays / strings statically known: call the corelib helper directly (no runtime lookup).
        if (staticType is IArrayTypeSymbol { Rank: 1 } staticArray && target.ContainingType is { } arrayItf
            && _m.ArrayInterfaceHelper(arrayItf, target, staticArray.ElementType) is { } arrayHelper)
            return CallBuiltinHelper(arrayHelper, concrete, arguments, extra, target);
        if (staticType?.SpecialType == SpecialType.System_String && _m.StringInterfaceHelper(target.ContainingType, target) is { } stringHelper)
            return CallBuiltinHelper(stringHelper, concrete, arguments, extra, target);

        var receiverValue = boxed ?? EmitValue(receiver);
        if (boxed is null) NullCheck(receiverValue);
        var args = new LLVMValueRef[1 + target.Parameters.Length];
        args[0] = receiverValue;
        foreach (var argument in arguments) args[1 + argument.Parameter!.Ordinal] = EmitArgument(argument, target);
        for (var i = 0; i < extra.Length; i++) args[args.Length - extra.Length + i] = extra[i];
        return InterfaceDispatch(target, receiverValue, args);
    }

    private LLVMValueRef CallBuiltinHelper(IMethodSymbol helper, IOperation receiver, ImmutableArray<IArgumentOperation> arguments,
        LLVMValueRef[] extra, IMethodSymbol target)
    {
        var self = EmitValue(receiver);
        NullCheck(self);
        var args = new LLVMValueRef[1 + target.Parameters.Length];
        args[0] = self;
        foreach (var argument in arguments) args[1 + argument.Parameter!.Ordinal] = EmitArgument(argument, target);
        for (var i = 0; i < extra.Length; i++) args[args.Length - extra.Length + i] = extra[i];
        var f = _m.GetFunction(helper);
        return _b.Call(f.Type, f.Fn, args);
    }

    /// <summary>A generic interface method: a closed-world type switch (ModuleCodeGen.GenericInterfaces.cs).</summary>
    private LLVMValueRef GenericInterfaceCall(IMethodSymbol target, LLVMValueRef self, LLVMValueRef[] args)
    {
        _m.NoteVirtualCall(target);
        var dispatcher = _m.GenericInterfaceDispatcherFor(target);
        return _b.Call(dispatcher.Type, dispatcher.Fn, args);
    }

    private LLVMValueRef InterfaceDispatch(IMethodSymbol target, LLVMValueRef self, LLVMValueRef[] args)
    {
        if (target.IsGenericMethod) return GenericInterfaceCall(target, self, args);
        var itf = target.ContainingType;
        var index = ModuleCodeGen.InterfaceSlotIndex(itf, target);
        _m.NoteInterfaceDispatch(itf, index);
        _m.NoteVirtualCall(target);
        var slots = CallRuntime("csrt_interface_slots", L.Ptr, [L.Ptr, L.Ptr], self, _m.InterfaceMethodTable(itf));
        var slotAddress = _b.InBoundsGep(L.I8, slots, [L.ConstUInt(L.I64, (ulong)index * T.PointerSize)], "islot");
        var fn = _b.Load(L.Ptr, slotAddress, "ifn");
        return _b.Call(_m.FunctionTypeOf(target), fn, args);
    }
}
