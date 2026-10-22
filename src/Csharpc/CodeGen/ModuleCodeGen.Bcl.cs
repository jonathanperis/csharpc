using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<string, LLVMValueRef> _arrayMethodTables = [];

    /// <summary>CLR full name as Type.FullName reports it: "System.Int32", "Program+Nested", "System.String[]".</summary>
    public static string ClrName(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol { Rank: 1 } a => ClrName(a.ElementType) + "[]",
        IArrayTypeSymbol a => ClrName(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]",
        INamedTypeSymbol named => Mangler.MetadataName(named.OriginalDefinition),
        _ => type.Name,
    };

    /// <summary>
    /// Per-element-type array method table: { csrt_Array_MT, "System.Int32[]", 0, 0, [Object virtuals] }.
    /// Arrays get it right after allocation so GetType() and ToString() report the element type.
    /// </summary>
    public LLVMValueRef ArrayMethodTable(ITypeSymbol elementType)
    {
        var name = ClrName(elementType) + "[]";
        if (_arrayMethodTables.TryGetValue(name, out var existing)) return existing;
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        var slots = defaults.Select(d => Runtime(d, Llvm.Ptr, Llvm.Ptr).Fn).ToArray();
        var init = Llvm.ConstStruct(ExternalGlobal("csrt_Array_MT"), CString(name), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, slots));
        var global = Module.AddGlobal("csrt.arraymt." + name, Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        return _arrayMethodTables[name] = global;
    }

    /// <summary>The method table that identifies a type at runtime (for typeof / Type objects).</summary>
    public LLVMValueRef TypeMethodTable(ITypeSymbol type, IOperation at)
    {
        if (BclValueTypes.IsDecimal(type)) return ExternalGlobal("csrt_Boxed_Decimal_MT");
        if (BclValueTypes.IsTimeSpan(type)) return ExternalGlobal("csrt_Boxed_TimeSpan_MT");
        if (BclValueTypes.IsDateTime(type)) return ExternalGlobal("csrt_Boxed_DateTime_MT");
        switch (type.SpecialType)
        {
            case SpecialType.System_String: return ExternalGlobal("csrt_String_MT");
            case SpecialType.System_Object: return ExternalGlobal("csrt_Object_MT");
            case SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_UInt32 or SpecialType.System_UInt64
                or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Boolean or SpecialType.System_Char:
                return ExternalGlobal($"csrt_Boxed_{type.MetadataName}_MT");
        }
        return type switch
        {
            IArrayTypeSymbol array => ArrayMethodTable(array.ElementType),
            INamedTypeSymbol { TypeKind: TypeKind.Class } c when IsUserType(c) && !c.IsStatic && !c.IsGenericType => GetLayout(c).MethodTable,
            INamedTypeSymbol { TypeKind: TypeKind.Struct } s when IsUserType(s) && !s.IsGenericType => StructMethodTable(s),
            INamedTypeSymbol named => LibraryTypeMethodTable(named.OriginalDefinition),
            _ => throw CompileError.Unsupported($"typeof({type})", at),
        };
    }
}
