using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Delegates handed to native code (Marshal.GetFunctionPointerForDelegate) and native function pointers
/// wrapped as delegates (Marshal.GetDelegateForFunctionPointer). A native callback is a plain code
/// address, and csharpc generates no code at run time (as NativeAOT does not either), so each delegate
/// type used this way gets a fixed pool of reverse P/Invoke thunks: thunk i of the pool loads the delegate
/// from slot i (a weak GC handle: the delegate must stay reachable while native code can call it, as in
/// .NET) and invokes it with the native arguments. corelib/IO/DelegateThunks.cs hands the slots out. A
/// delegate wrapping a native pointer is `csrt_delegate_create(mt, boxed pointer, native-invoke thunk)`.
///
/// The pool of a type is one global, { native count, [count x thunk address], [count x weak handle] }, whose
/// address the corelib reads through CoreIntrinsics.ReverseThunkTable&lt;TDelegate&gt;.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>Distinct delegates of one type that can be live native callbacks at once.</summary>
    internal const int ReverseThunkSlots = 32;

    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _reverseThunkTables = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _nativeInvokeThunks = new(SymbolEqualityComparer.Default);

    /// <summary>A delegate type of the program the reverse thunks can be made for (a concrete, non-generic type).</summary>
    internal static bool CanMarshalDelegate(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Delegate, IsGenericType: false, DelegateInvokeMethod: not null };

    /// <summary>Address of the type's pool; see the class comment for the layout.</summary>
    internal LLVMValueRef ReverseThunkTable(INamedTypeSymbol type, IOperation at)
    {
        if (_reverseThunkTables.TryGetValue(type, out var existing)) return existing;
        var invoke = type.DelegateInvokeMethod!;
        var shape = MarshalledSignature(invoke, type, at);
        var entry = Llvm.Ptr;
        var tableType = Llvm.Struct(Types.NativeInt, LlvmContext.Array(entry, ReverseThunkSlots), LlvmContext.Array(entry, ReverseThunkSlots));
        var name = "csharpc.reversethunks." + Mangler.MetadataName(type);
        var table = Module.AddGlobal(name, tableType, linkage: LLVMLinkage.Internal);
        _reverseThunkTables[type] = table;
        try
        {
            var fnType = Llvm.FunctionType(shape.NativeReturn, [.. shape.NativeParameters]);
            var thunks = new List<LLVMValueRef>();
            for (var i = 0; i < ReverseThunkSlots; i++)
            {
                var slot = i;
                var thunk = SyntheticFunction($"{name}.thunk{i}", fnType, (g, syntax) => g.EmitReverseThunk(type, shape, table, slot, syntax));
                AddNativeAbiAttributes(thunk, shape);
                thunks.Add(thunk);
            }
            var zeros = Enumerable.Repeat(LlvmContext.Null(entry), ReverseThunkSlots).ToArray();
            Native.LLVMSetInitializer(table, Llvm.ConstStruct(Llvm.ConstInt(Types.NativeInt, ReverseThunkSlots),
                LlvmContext.ConstArray(entry, [.. thunks]), LlvmContext.ConstArray(entry, zeros)));
        }
        catch
        {
            _reverseThunkTables.Remove(type);
            Native.LLVMDeleteGlobal(table);
            throw;
        }
        return table;
    }

    /// <summary>`R thunk(ptr target, args...)`: calls the native function pointer boxed in the delegate's target.</summary>
    internal LLVMValueRef NativeInvokeThunk(INamedTypeSymbol type, IOperation at)
    {
        if (_nativeInvokeThunks.TryGetValue(type, out var existing)) return existing;
        var invoke = type.DelegateInvokeMethod!;
        var shape = MarshalledSignature(invoke, type, at);
        // The managed side calls delegates as fn(target, managed args...): the managed parameter types, not the C ones.
        List<LLVMTypeRef> managed = [Llvm.Ptr];
        foreach (var p in invoke.Parameters) managed.Add(p.RefKind == RefKind.None ? Types.Value(p.Type) : Llvm.Ptr);
        var managedReturn = invoke.ReturnsVoid ? Llvm.Void : Types.Value(invoke.ReturnType);
        var fnType = Llvm.FunctionType(managedReturn, [.. managed]);
        var thunk = SyntheticFunction("csharpc.nativeinvoke." + Mangler.MetadataName(type), fnType, (g, syntax) => g.EmitNativeInvokeThunk(type, shape, syntax));
        _nativeInvokeThunks[type] = thunk;
        return thunk;
    }

    /// <summary>What a delegate's Invoke looks like to C: the native parameter and return types, and which of them are .NET bools (4-byte BOOL).</summary>
    internal sealed record MarshalledShape(LLVMTypeRef[] NativeParameters, LLVMTypeRef NativeReturn, bool[] BoolParameters, bool BoolReturn,
        IMethodSymbol Invoke);

    private MarshalledShape MarshalledSignature(IMethodSymbol invoke, INamedTypeSymbol type, IOperation at)
    {
        List<LLVMTypeRef> native = [];
        List<bool> bools = [];
        foreach (var p in invoke.Parameters)
        {
            if (p.RefKind != RefKind.None)
            {
                if (!IsBlittable(p.Type)) throw CompileError.Unsupported($"delegate '{type}' marshalled to native code: by-reference parameter '{p.Name}' of non-blittable type '{p.Type}'", at);
                native.Add(Llvm.Ptr);
                bools.Add(false);
            }
            else if (p.Type.SpecialType == SpecialType.System_Boolean)
            {
                native.Add(Llvm.I32); // .NET's default bool marshalling: a 4-byte BOOL
                bools.Add(true);
            }
            else
            {
                if (!IsBlittable(p.Type) || p.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } ps && !IsPrimitiveLike(ps))
                    throw CompileError.Unsupported($"delegate '{type}' marshalled to native code: parameter '{p.Name}' of type '{p.Type}'", at);
                native.Add(Types.Value(p.Type));
                bools.Add(false);
            }
        }
        LLVMTypeRef ret;
        var boolReturn = false;
        if (invoke.ReturnsVoid) ret = Llvm.Void;
        else if (invoke.RefKind != RefKind.None) throw CompileError.Unsupported($"delegate '{type}' marshalled to native code: by-reference return", at);
        else if (invoke.ReturnType.SpecialType == SpecialType.System_Boolean) { ret = Llvm.I32; boolReturn = true; }
        else
        {
            if (!IsBlittable(invoke.ReturnType) || invoke.ReturnType is INamedTypeSymbol { TypeKind: TypeKind.Struct } rs && !IsPrimitiveLike(rs))
                throw CompileError.Unsupported($"delegate '{type}' marshalled to native code: return type '{invoke.ReturnType}'", at);
            ret = Types.Value(invoke.ReturnType);
        }
        return new MarshalledShape([.. native], ret, [.. bools], boolReturn, invoke);
    }

    /// <summary>Sub-int arguments and results are extended by the caller (zeroext/signext), as clang declares them.</summary>
    private void AddNativeAbiAttributes(LLVMValueRef fn, MarshalledShape shape)
    {
        LLVMAttributeRef Enum(string name) =>
            Native.LLVMCreateEnumAttribute(Llvm.Ref, Native.LLVMGetEnumAttributeKindForName(name, (nuint)name.Length), 0);
        for (var i = 0; i < shape.NativeParameters.Length; i++)
            if (shape.NativeParameters[i] is { IsInteger: true, IntWidth: < 32 })
                Native.LLVMAddAttributeAtIndex(fn, (uint)i + 1, Enum(TypeMapper.IsSigned(shape.Invoke.Parameters[i].Type) ? "signext" : "zeroext"));
        if (!shape.Invoke.ReturnsVoid && shape.NativeReturn is { IsInteger: true, IntWidth: < 32 })
            Native.LLVMAddAttributeAtIndex(fn, 0, Enum(TypeMapper.IsSigned(shape.Invoke.ReturnType) ? "signext" : "zeroext"));
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>CoreIntrinsics.ReverseThunkTable / NativeInvokeThunk / DelegateTypeTable of one delegate type.</summary>
    private LLVMValueRef DelegateThunkIntrinsic(string name, ITypeSymbol type, IOperation at)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType) return L.ConstInt(T.NativeInt, -1);
        if (!ModuleCodeGen.CanMarshalDelegate(delegateType)) return L.ConstInt(T.NativeInt, 0);
        var address = name switch
        {
            "ReverseThunkTable" => _m.ReverseThunkTable(delegateType, at),
            "NativeInvokeThunk" => _m.NativeInvokeThunk(delegateType, at),
            _ => _m.DelegateMethodTable(delegateType),
        };
        return _b.Cast(LLVMOpcode.PtrToInt, address, T.NativeInt);
    }

    /// <summary>The native-ABI function for slot <paramref name="slot"/> of a pool: calls the delegate its weak handle names.</summary>
    internal void EmitReverseThunk(INamedTypeSymbol type, ModuleCodeGen.MarshalledShape shape, LLVMValueRef table, int slot, SyntaxNode syntax)
    {
        var invoke = shape.Invoke;
        var tableType = L.Struct(T.NativeInt, LlvmContext.Array(L.Ptr, ModuleCodeGen.ReverseThunkSlots), LlvmContext.Array(L.Ptr, ModuleCodeGen.ReverseThunkSlots));
        var handleSlot = _b.InBoundsGep(tableType, table, [L.ConstInt(L.I32, 0), L.ConstInt(L.I32, 2), L.ConstInt(L.I32, slot)]);
        var handle = _b.Cast(LLVMOpcode.PtrToInt, _b.Load(L.Ptr, handleSlot, "handle"), T.NativeInt);
        var lookup = NewBlock("thunk.lookup");
        var dead = NewBlock("thunk.dead");
        var call = NewBlock("thunk.call");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, handle, L.ConstInt(T.NativeInt, 0)), dead, lookup);
        _b.PositionAtEnd(lookup);
        var del = CallRuntime("csrt_weak_target", L.Ptr, [T.NativeInt], handle);
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, del, LlvmContext.Null(L.Ptr)), dead, call);
        _b.PositionAtEnd(dead);
        // .NET fails the same way (a callback on a collected delegate): there is no managed caller to throw to.
        CallRuntime("csrt_unsupported_method", L.Void, [L.Ptr],
            _m.CString("a native callback was made on a delegate that has been garbage collected: keep the delegate passed to Marshal.GetFunctionPointerForDelegate reachable while native code can call it"));
        _b.Unreachable();
        _b.PositionAtEnd(call);

        var args = new List<LLVMValueRef>();
        for (var i = 0; i < invoke.Parameters.Length; i++)
        {
            var value = ParamOf((uint)i);
            args.Add(shape.BoolParameters[i] ? _b.ICmp(LLVMIntPredicate.NE, value, L.ConstInt(L.I32, 0)) : value);
        }
        List<LLVMTypeRef> managed = [L.Ptr];
        foreach (var p in invoke.Parameters) managed.Add(p.RefKind == RefKind.None ? T.Value(p.Type) : L.Ptr);
        var returnType = invoke.ReturnsVoid ? L.Void : T.Value(invoke.ReturnType);
        var fnType = L.FunctionType(returnType, [.. managed]);
        var result = InvokeDelegate(del, returnType, (target, fn) => _b.Call(fnType, fn, [target, .. args]));
        if (invoke.ReturnsVoid) _b.RetVoid();
        else _b.Ret(shape.BoolReturn ? _b.Cast(LLVMOpcode.ZExt, result, L.I32) : result);
        Finish();
    }

    /// <summary>R thunk(boxed native pointer, args...): the delegate wrapping a native function pointer calls it with the C ABI.</summary>
    internal void EmitNativeInvokeThunk(INamedTypeSymbol type, ModuleCodeGen.MarshalledShape shape, SyntaxNode syntax)
    {
        var invoke = shape.Invoke;
        var box = ParamOf(0);
        var native = _b.Load(L.Ptr, _b.InBoundsGep(L.I8, box, [L.ConstInt(L.I64, 8)]), "native");
        var args = new List<LLVMValueRef>();
        for (var i = 0; i < invoke.Parameters.Length; i++)
        {
            var value = ParamOf((uint)i + 1);
            args.Add(shape.BoolParameters[i] ? _b.Cast(LLVMOpcode.ZExt, value, L.I32) : value);
        }
        var nativeType = L.FunctionType(shape.NativeReturn, [.. shape.NativeParameters]);
        var call = _b.Call(nativeType, native, [.. args]);
        // Sub-int arguments are extended by the caller (zeroext/signext), as clang declares them.
        LLVMAttributeRef Enum(string name) =>
            Native.LLVMCreateEnumAttribute(L.Ref, Native.LLVMGetEnumAttributeKindForName(name, (nuint)name.Length), 0);
        for (var i = 0; i < shape.NativeParameters.Length; i++)
            if (shape.NativeParameters[i] is { IsInteger: true, IntWidth: < 32 })
                Native.LLVMAddCallSiteAttribute(call, (uint)i + 1, Enum(TypeMapper.IsSigned(invoke.Parameters[i].Type) ? "signext" : "zeroext"));
        if (!invoke.ReturnsVoid && shape.NativeReturn is { IsInteger: true, IntWidth: < 32 })
            Native.LLVMAddCallSiteAttribute(call, 0, Enum(TypeMapper.IsSigned(invoke.ReturnType) ? "signext" : "zeroext"));
        if (invoke.ReturnsVoid) _b.RetVoid();
        else _b.Ret(shape.BoolReturn ? _b.ICmp(LLVMIntPredicate.NE, call, L.ConstInt(L.I32, 0)) : call);
        Finish();
    }
}
