using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Interface slot tables for boxed primitives (IComparable, IComparable&lt;T&gt;, IEquatable&lt;T&gt;,
/// IFormattable), registered with csrt at startup so `IComparable c = 5; c.CompareTo(3)` dispatches.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<(ITypeSymbol, INamedTypeSymbol), LLVMValueRef> _primitiveSlots = [];

    internal LLVMValueRef PrimitiveInterfaceSlots(ITypeSymbol primitive, INamedTypeSymbol itf)
    {
        var key = (primitive, itf);
        if (_primitiveSlots.TryGetValue(key, out var existing)) return existing;
        var members = InterfaceSlots(itf).ToList();
        var slots = members.Select(m => PrimitiveSlot(primitive, itf, m)).ToArray();
        if (slots.All(s => s.IsNull)) return _primitiveSlots[key] = LlvmContext.Null(Llvm.Ptr);
        var init = LlvmContext.ConstArray(Llvm.Ptr, [.. slots.Select(s => s.IsNull ? LlvmContext.Null(Llvm.Ptr) : s)]);
        var global = Module.AddGlobal(".primslots", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        return _primitiveSlots[key] = global;
    }

    /// <summary>Does <see cref="PrimitiveSlot"/> implement this member for boxed values of the primitive?</summary>
    internal static bool PrimitiveSlotSupported(ITypeSymbol primitive, INamedTypeSymbol itf, IMethodSymbol member) =>
        primitive.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Double && primitive.SpecialType != SpecialType.System_Decimal
        && (itf.Name, member.Name) switch
        {
            ("IComparable", "CompareTo") when !itf.IsGenericType => true,
            ("IFormattable", "ToString") => true,
            ("IComparable", "CompareTo") or ("IEquatable", "Equals") => itf.IsGenericType && SymbolEqualityComparer.Default.Equals(itf.TypeArguments[0], primitive),
            _ => false,
        };

    private LLVMValueRef PrimitiveSlot(ITypeSymbol primitive, INamedTypeSymbol itf, IMethodSymbol member)
    {
        switch (itf.Name, member.Name)
        {
            case ("IComparable", "CompareTo") when !itf.IsGenericType:
            {
                var fnType = Llvm.FunctionType(Llvm.I32, Llvm.Ptr, Llvm.Ptr);
                var fn = Module.AddFunction($".prim.{primitive.MetadataName}.IComparable.object", fnType);
                Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
                new FunctionCodeGen(this, null, new LoweredFunction(fn, fnType)).EmitPrimitiveCompareObjectThunk(primitive);
                return fn;
            }
            case ("IFormattable", "ToString"):
            {
                var fnType = Llvm.FunctionType(Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
                var fn = Module.AddFunction($".prim.{primitive.MetadataName}.IFormattable", fnType);
                Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
                new FunctionCodeGen(this, null, new LoweredFunction(fn, fnType)).EmitPrimitiveFormatThunk();
                return fn;
            }
            case ("IComparable", "CompareTo") or ("IEquatable", "Equals") when itf.IsGenericType
                && SymbolEqualityComparer.Default.Equals(itf.TypeArguments[0], primitive):
            {
                var isCompare = member.Name == "CompareTo";
                var valueType = Types.Value(primitive);
                var fnType = Llvm.FunctionType(isCompare ? Llvm.I32 : Llvm.I1, Llvm.Ptr, valueType);
                var fn = Module.AddFunction($".prim.{primitive.MetadataName}.{itf.Name}", fnType);
                Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
                new FunctionCodeGen(this, null, new LoweredFunction(fn, fnType)).EmitPrimitiveInterfaceThunk(primitive, isCompare);
                return fn;
            }
        }
        return default;
    }
}

public sealed partial class FunctionCodeGen
{
    /// <summary>thunk(box, T other): unbox the receiver, then CompareTo / Equals.</summary>
    internal void EmitPrimitiveInterfaceThunk(ITypeSymbol primitive, bool isCompare)
    {
        var self = LoadValue(_b.InBoundsGep(L.I8, _fn.Fn.Param(0), [L.ConstInt(L.I64, 8)]), primitive);
        var other = _fn.Fn.Param(1);
        _b.Ret(isCompare ? CompareValues(self, other, primitive, null!) : FieldEquals(self, other, primitive));
        Finish();
    }

    /// <summary>
    /// IComparable.CompareTo(object) on a boxed primitive: null sorts first; a different boxed type throws
    /// ArgumentException("Object must be of type Int32."), as .NET.
    /// </summary>
    internal void EmitPrimitiveCompareObjectThunk(ITypeSymbol primitive)
    {
        var self = _fn.Fn.Param(0);
        var other = _fn.Fn.Param(1);
        var isNull = NewBlock("other.null");
        var notNull = NewBlock("other.nonnull");
        var mismatch = NewBlock("other.mismatch");
        var compare = NewBlock("other.compare");
        _b.CondBr(_b.ICmp(LLVMIntPredicate.EQ, other, LlvmContext.Null(L.Ptr)), isNull, notNull);
        _b.PositionAtEnd(isNull);
        _b.Ret(L.ConstInt(L.I32, 1));
        _b.PositionAtEnd(notNull);
        var sameType = _b.ICmp(LLVMIntPredicate.EQ, _b.Load(L.Ptr, self), _b.Load(L.Ptr, other));
        _b.CondBr(sameType, compare, mismatch);
        _b.PositionAtEnd(mismatch);
        ThrowNewLibraryException(_m.Compilation.GetTypeByMetadataName("System.ArgumentException")!,
            $"Object must be of type {primitive.MetadataName}.");
        _b.PositionAtEnd(compare);
        LLVMValueRef Payload(LLVMValueRef box) => LoadValue(_b.InBoundsGep(L.I8, box, [L.ConstInt(L.I64, 8)]), primitive);
        _b.Ret(CompareValues(Payload(self), Payload(other), primitive, null!));
        Finish();
    }

    /// <summary>IFormattable.ToString(format, provider) on a boxed primitive (invariant culture: provider ignored).</summary>
    internal void EmitPrimitiveFormatThunk()
    {
        _b.Ret(CallRuntime("csrt_format_object", L.Ptr, [L.Ptr, L.Ptr], _fn.Fn.Param(0), _fn.Fn.Param(1)));
        Finish();
    }
}
