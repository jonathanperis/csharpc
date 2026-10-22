using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>Type facts used for performance decisions (see bench/RESULTS.md).</summary>
internal static class Optimizations
{
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
        _ => false,
    };
}
