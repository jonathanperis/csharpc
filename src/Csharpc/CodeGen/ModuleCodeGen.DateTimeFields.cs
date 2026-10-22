using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Boxed structs compare and hash their payload bytes (csrt_valuetype_equals / _get_hash_code). A
/// DateTime field holds its Kind in the top two bits, which .NET's ValueType.Equals ignores (DateTime
/// overrides Equals, so the CLR compares such fields with DateTime.Equals: ticks only). Structs with
/// DateTime fields therefore get thunks that mask those bits.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>Default Equals / GetHashCode slots for a boxed struct, or null when its bytes compare as they are.</summary>
    private (LLVMValueRef Eq, LLVMValueRef Hash)? DateTimeMaskedSlots(StructLayout layout)
    {
        List<ulong> offsets = [];
        CollectDateTimeOffsets(layout.Type, layout.LlvmType, 0, offsets, 0);
        if (offsets.Count == 0) return null;

        var init = LlvmContext.ConstArray(Llvm.I32, [.. offsets.Select(o => Llvm.ConstInt(Llvm.I32, (long)o))]);
        var table = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "DateTimeOffsets"), Native.LLVMTypeOf(init), init,
            constant: true, linkage: LLVMLinkage.Private);
        var count = Llvm.ConstInt(Llvm.I32, offsets.Count);

        var equalsRt = Runtime("csrt_valuetype_equals_masked", Llvm.I1, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.I32);
        var equals = Module.AddFunction(Mangler.TypeSymbol(layout.Type, "BoxEquals"), Llvm.FunctionType(Llvm.I1, Llvm.Ptr, Llvm.Ptr));
        Native.LLVMSetLinkage(equals, LLVMLinkage.Internal);
        using (var b = new IrBuilder(Llvm))
        {
            b.PositionAtEnd(b.AppendBlock(equals, "entry"));
            b.Ret(b.Call(equalsRt.Type, equalsRt.Fn, [equals.Param(0), equals.Param(1), table, count]));
        }

        var hashRt = Runtime("csrt_valuetype_get_hash_code_masked", Llvm.I32, Llvm.Ptr, Llvm.Ptr, Llvm.I32);
        var hash = Module.AddFunction(Mangler.TypeSymbol(layout.Type, "BoxHash"), Llvm.FunctionType(Llvm.I32, Llvm.Ptr));
        Native.LLVMSetLinkage(hash, LLVMLinkage.Internal);
        using (var b = new IrBuilder(Llvm))
        {
            b.PositionAtEnd(b.AppendBlock(hash, "entry"));
            b.Ret(b.Call(hashRt.Type, hashRt.Fn, [hash.Param(0), table, count]));
        }
        return (equals, hash);
    }

    /// <summary>Byte offsets of the DateTime values stored inline in a value of this type (nested structs, nullables, tuples).</summary>
    private void CollectDateTimeOffsets(ITypeSymbol type, LLVMTypeRef llvmType, ulong offset, List<ulong> offsets, int depth)
    {
        if (depth > 16) return;
        var td = Types.Target.DataLayout;
        if (BclValueTypes.IsDateTime(type)) { offsets.Add(offset); return; }
        if (TypeMapper.IsNullable(type))
        {
            CollectDateTimeOffsets(TypeMapper.NullableUnderlying(type), Native.LLVMStructGetTypeAtIndex(llvmType, 1),
                offset + Native.LLVMOffsetOfElement(td, llvmType, 1), offsets, depth + 1);
            return;
        }
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            for (var i = 0; i < tuple.TupleElements.Length; i++)
                CollectDateTimeOffsets(tuple.TupleElements[i].Type, Native.LLVMStructGetTypeAtIndex(llvmType, (uint)i),
                    offset + Native.LLVMOffsetOfElement(td, llvmType, (uint)i), offsets, depth + 1);
            return;
        }
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named && ResolveStructType(named) is not null)
        {
            var layout = GetStructLayout(named);
            foreach (var (field, index) in layout.FieldIndex)
            {
                if (!SymbolEqualityComparer.Default.Equals(field.ContainingType, layout.Type)) continue;  // the OriginalDefinition aliases
                CollectDateTimeOffsets(field.Type, Native.LLVMStructGetTypeAtIndex(layout.LlvmType, index),
                    offset + Native.LLVMOffsetOfElement(td, layout.LlvmType, index), offsets, depth + 1);
            }
        }
    }
}
