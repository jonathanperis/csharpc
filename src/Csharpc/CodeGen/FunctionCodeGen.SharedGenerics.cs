using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Shared generic code (ADR 0026, step 3b-1): where a canonical body needs the method table of a type that names one of its class's type
/// parameters (a cast, typeof, new T[], new Node&lt;T&gt;), it loads it from the generic dictionary of the object it runs on.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>
    /// The handle of <paramref name="type"/> read from the dictionary, or null when the type names no marker (exact code, or a type that
    /// is the same in every instantiation): this -> method table -> the dictionary slot of this method's class -> entry.
    /// </summary>
    internal LLVMValueRef? SharedHandleOf(SharedHandle kind, ITypeSymbol type)
    {
        if (!_m.IsCanonDependent(type)) return null;
        if (_method is not { IsStatic: false, ContainingType: { TypeKind: TypeKind.Class } owner } || _nested is not null || _filterParent is not null || _this.IsNull
            || !_m.IsCanonDependent(owner))
            throw new CompileError("CSLLVM0004", $"internal error: '{_method}' needs the {kind} handle of '{type}' and has no generic dictionary", null);
        var layout = _m.GetLayout(owner);
        var index = _m.SharedSlot(layout.Type, _method, kind, type);
        var mt = _b.Load(L.Ptr, _this, "shared.mt");
        _m.Facts.MethodTableLoad(mt);
        var slot = T.Target.SizeOf(T.MethodTableHeader) + (ulong)layout.DictionarySlots[layout.Type.OriginalDefinition] * T.PointerSize;
        var dictionary = _b.Load(L.Ptr, _b.InBoundsGep(L.I8, mt, [L.ConstUInt(L.I64, slot)]), "shared.dictionary");
        return _b.Load(L.Ptr, _b.InBoundsGep(L.I8, dictionary, [L.ConstUInt(L.I64, (ulong)index * T.PointerSize)]), "shared.handle");
    }

    /// <summary>
    /// stelem of a T in shared code: the array may be covariant whatever T is (a string[] behind an object[] when T is object), and the
    /// static element type is a marker, so the test is on the run-time types: a null or a value of exactly the array's element type
    /// stores; anything else goes to the runtime's full check. No dictionary read.
    /// </summary>
    private void SharedArrayStoreCheck(LLVMValueRef array, LLVMValueRef value)
    {
        var test = NewBlock("store.test");
        var slow = NewBlock("store.covariant");
        var done = NewBlock("store.checked");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, value, LlvmContext.Null(L.Ptr)), done, test);
        _b.PositionAtEnd(test);
        var arrayMt = _b.Load(L.Ptr, array, "array.mt");
        _m.Facts.MethodTableLoad(arrayMt);
        // csrt_ArrayMethodTable: the header, Object's three virtuals, then the element type's method table.
        var elementOffset = T.Target.SizeOf(T.MethodTableHeader) + (ulong)ClassLayout.ObjectSlotCount * T.PointerSize;
        var element = _b.Load(L.Ptr, _b.InBoundsGep(L.I8, arrayMt, [L.ConstUInt(L.I64, elementOffset)]), "array.element");
        var valueMt = _b.Load(L.Ptr, value, "value.mt");
        _m.Facts.MethodTableLoad(valueMt);
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, element, valueMt), done, slow);
        _b.PositionAtEnd(slow);
        CallRuntime("csrt_array_store_check", L.Void, [L.Ptr, L.Ptr], array, value);
        _b.Br(done);
        _b.PositionAtEnd(done);
    }
}
