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

    private LLVMValueRef PrimitiveSlot(ITypeSymbol primitive, INamedTypeSymbol itf, IMethodSymbol member)
    {
        switch (itf.Name, member.Name)
        {
            case ("IComparable", "CompareTo") when !itf.IsGenericType:
                return Runtime("csrt_boxed_compare_object", Llvm.I32, Llvm.Ptr, Llvm.Ptr).Fn;
            case ("IFormattable", "ToString"):
                return Runtime("csrt_boxed_format", Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr).Fn;
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
}
