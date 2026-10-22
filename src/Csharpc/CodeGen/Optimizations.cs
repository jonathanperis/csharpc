using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>Type facts used for performance decisions (see bench/RESULTS.md).</summary>
internal static class Optimizations
{
    /// <summary>
    /// ABI and aliasing facts for calls into csrt (C):
    ///  * small integers in the csrt API are all unsigned (bool, uint8_t, uint16_t), and the SysV
    ///    ABI makes the *caller* extend them — clang-compiled callees rely on it — so mark them zeroext;
    ///  * allocation helpers return fresh, non-null memory (noalias + nonnull), which lets LLVM keep
    ///    values in registers across them and drop null checks on new objects when the runtime
    ///    isn't LTO-linked (with runtime LTO, the definitions are visible anyway).
    /// </summary>
    public static void AnnotateRuntimeDeclaration(LlvmContext llvm, LLVMValueRef fn, LLVMTypeRef ret, LLVMTypeRef[] parameters)
    {
        static bool IsSmallInt(LLVMTypeRef t) => t.IsInteger && t.IntWidth < 32;
        if (IsSmallInt(ret)) llvm.AddAttributeAt(fn, 0, "zeroext");
        for (var i = 0; i < parameters.Length; i++)
            if (IsSmallInt(parameters[i])) llvm.AddAttributeAt(fn, (uint)i + 1, "zeroext");
        if (s_allocators.Contains(Native.TakeName(fn)))
        {
            llvm.AddAttributeAt(fn, 0, "noalias");
            llvm.AddAttributeAt(fn, 0, "nonnull");
        }
    }

    private static readonly HashSet<string> s_allocators =
        ["csrt_alloc", "csrt_new_array", "csrt_new_array_primitive", "csrt_alloc_noclear"];

    /// <summary>
    /// Primitive and enum values contain no object references, so containers of them (arrays)
    /// can be skipped entirely by the GC's marker.
    /// </summary>
    public static bool HoldsNoReferences(ITypeSymbol type) => type switch
    {
        { SpecialType: SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte
            or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_IntPtr or SpecialType.System_UIntPtr } => true,
        { TypeKind: TypeKind.Enum } => true,
        _ when BclValueTypes.IsDecimal(type) || BclValueTypes.IsTicks(type) => true, // plain bits (BclValueTypes)
        _ => false,
    };
}
