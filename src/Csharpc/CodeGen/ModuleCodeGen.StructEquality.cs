using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// ValueType.Equals / ValueType.GetHashCode for structs that don't override them, generated per struct
/// type (closed world: every struct is known), see FunctionCodeGen.StructEquality.cs for the semantics.
/// Each struct gets `i1 Equals(T* a, T* b)` and `i32 Hash(T* a)` on the unboxed payload — called
/// directly by EqualityComparer&lt;T&gt;.Default (no boxing) — and boxed-receiver thunks for its method table.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>The user Equals(object) / GetHashCode() override declared by a struct, if any.</summary>
    public static IMethodSymbol? StructOverride(INamedTypeSymbol type, string name) =>
        type.GetMembers(name).OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.IsOverride && ClassLayout.RootOf(m).ContainingType.SpecialType == SpecialType.System_Object);

    /// <summary>bool ValueType.Equals semantics on two unboxed values of <paramref name="type"/> (by address).</summary>
    public LoweredFunction StructDefaultEquals(INamedTypeSymbol type)
    {
        var layout = GetStructLayout(type);
        if (layout.DefaultEquals is { } existing) return existing;
        var fnType = Llvm.FunctionType(Llvm.I1, Llvm.Ptr, Llvm.Ptr);
        var fn = Module.AddFunction(Mangler.TypeSymbol(layout.Type, "ValueTypeEquals"), fnType);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        var lowered = new LoweredFunction(fn, fnType);
        layout.DefaultEquals = lowered; // before the body: nested types may refer back through boxes
        new FunctionCodeGen(this, null, lowered).EmitStructDefaultEquals(layout);
        return lowered;
    }

    /// <summary>int ValueType.GetHashCode semantics on an unboxed value of <paramref name="type"/> (by address).</summary>
    public LoweredFunction StructDefaultHash(INamedTypeSymbol type)
    {
        var layout = GetStructLayout(type);
        if (layout.DefaultHash is { } existing) return existing;
        var fnType = Llvm.FunctionType(Llvm.I32, Llvm.Ptr);
        var fn = Module.AddFunction(Mangler.TypeSymbol(layout.Type, "ValueTypeHash"), fnType);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        var lowered = new LoweredFunction(fn, fnType);
        layout.DefaultHash = lowered;
        new FunctionCodeGen(this, null, lowered).EmitStructDefaultHash(layout);
        return lowered;
    }

    /// <summary>
    /// Method-table slots of a boxed struct without overrides: Equals(this box, object other) is false
    /// unless other is a box of the same type (GetType() != obj.GetType()), then compares the payloads.
    /// </summary>
    private LLVMValueRef BoxedDefaultEquals(StructLayout layout)
    {
        var target = StructDefaultEquals(layout.Type);
        var fn = Module.AddFunction(Mangler.TypeSymbol(layout.Type, "BoxEquals"), Llvm.FunctionType(Llvm.I1, Llvm.Ptr, Llvm.Ptr));
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        using var b = new IrBuilder(Llvm);
        var entry = b.AppendBlock(fn, "entry");
        var check = b.AppendBlock(fn, "check");
        var compare = b.AppendBlock(fn, "compare");
        var differ = b.AppendBlock(fn, "differ");
        b.PositionAtEnd(entry);
        var (self, other) = (fn.Param(0), fn.Param(1));
        b.CondBr(b.ICmp(LLVMIntPredicate.EQ, other, LlvmContext.Null(Llvm.Ptr)), differ, check);
        b.PositionAtEnd(check);
        b.CondBr(b.ICmp(LLVMIntPredicate.EQ, b.Load(Llvm.Ptr, self), b.Load(Llvm.Ptr, other)), compare, differ);
        b.PositionAtEnd(compare);
        var eight = Llvm.ConstInt(Llvm.I64, 8);
        b.Ret(b.Call(target.Type, target.Fn, [b.InBoundsGep(Llvm.I8, self, [eight]), b.InBoundsGep(Llvm.I8, other, [eight])]));
        b.PositionAtEnd(differ);
        b.Ret(Llvm.False);
        return fn;
    }

    private LLVMValueRef BoxedDefaultHash(StructLayout layout)
    {
        var target = StructDefaultHash(layout.Type);
        var fn = Module.AddFunction(Mangler.TypeSymbol(layout.Type, "BoxHash"), Llvm.FunctionType(Llvm.I32, Llvm.Ptr));
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        b.Ret(b.Call(target.Type, target.Fn, [b.InBoundsGep(Llvm.I8, fn.Param(0), [Llvm.ConstInt(Llvm.I64, 8)])]));
        return fn;
    }
}
