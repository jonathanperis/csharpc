using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Maps C# types to LLVM types. Like clang, we distinguish the SSA "value" type (bool is i1)
/// from the in-memory "storage" type (bool is i8) — see docs/03-lowering.md#primitive-types.
/// </summary>
public sealed class TypeMapper(LlvmContext llvm, TargetMachine target)
{
    public LlvmContext Llvm { get; } = llvm;
    public TargetMachine Target { get; } = target;
    public LLVMTypeRef NativeInt { get; } = target.SizeOf(llvm.Ptr) == 8 ? llvm.I64 : llvm.I32;
    public ulong PointerSize { get; } = target.SizeOf(llvm.Ptr);

    /// <summary>The method-table header, shared with csrt.h: { base, name, instance_size, flags, interfaces, interface_count, reserved }.</summary>
    public LLVMTypeRef MethodTableHeader => field.IsNull
        ? field = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32)
        : field;

    /// <summary>string: { mt, i32 length, [0 x i16] chars }.</summary>
    public LLVMTypeRef StringHeader => field.IsNull
        ? field = Llvm.Struct(Llvm.Ptr, Llvm.I32, LlvmContext.Array(Llvm.I16, 0))
        : field;

    /// <summary>T[]: { mt, i64 length, [0 x T] data } — element data starts at offset 16.</summary>
    public LLVMTypeRef ArrayHeader(LLVMTypeRef element) => Llvm.Struct(Llvm.Ptr, Llvm.I64, LlvmContext.Array(element, 0));

    public LLVMTypeRef Value(ITypeSymbol type) => type switch
    {
        { SpecialType: SpecialType.System_Void } => Llvm.Void,
        { SpecialType: SpecialType.System_Boolean } => Llvm.I1,
        { SpecialType: SpecialType.System_SByte or SpecialType.System_Byte } => Llvm.I8,
        { SpecialType: SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char } => Llvm.I16,
        { SpecialType: SpecialType.System_Int32 or SpecialType.System_UInt32 } => Llvm.I32,
        { SpecialType: SpecialType.System_Int64 or SpecialType.System_UInt64 } => Llvm.I64,
        { SpecialType: SpecialType.System_IntPtr or SpecialType.System_UIntPtr } => NativeInt,
        { SpecialType: SpecialType.System_Single } => Llvm.F32,
        { SpecialType: SpecialType.System_Double } => Llvm.F64,
        INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } => Value(underlying),
        IPointerTypeSymbol or IFunctionPointerTypeSymbol => Llvm.Ptr,
        { IsReferenceType: true } => Llvm.Ptr,
        // Tasks, awaiters and configured awaitables are all a pointer to the csrt task (docs/10-async.md).
        INamedTypeSymbol t when Coroutines.IsTaskHandleType(t) => Llvm.Ptr,
        // System.Decimal: the CLR layout { int flags; uint hi32; ulong lo64 } (runtime/decimal.c).
        { SpecialType: SpecialType.System_Decimal } => Llvm.Struct(Llvm.I32, Llvm.I32, Llvm.I64),
        // TimeSpan / DateTime: a tick count (BclValueTypes.cs, runtime/datetime.c).
        _ when BclValueTypes.IsTicks(type) => Llvm.I64,
        // BigInteger: pointer to an immutable runtime bignum, null == 0 (runtime/bigint.c).
        _ when BclValueTypes.IsBigInteger(type) => Llvm.Ptr,
        // Value types (docs/03-lowering.md#structs): first-class LLVM aggregates of storage types.
        INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n =>
            Llvm.Struct(Llvm.I8, Storage(n.TypeArguments[0])),              // { hasValue, value }
        INamedTypeSymbol { IsTupleType: true } t when t.TupleElements.Length <= 7 =>
            Llvm.Struct([.. t.TupleElements.Select(e => Storage(e.Type))]),  // ValueTuple<...>: flattened
        INamedTypeSymbol n when IsSystemStruct(n, "Index") => Llvm.I32,                       // .NET's own encoding: ~n = from end
        INamedTypeSymbol n when IsSystemStruct(n, "Range") => Llvm.Struct(Llvm.I32, Llvm.I32), // { Index start, Index end }
        // System.Threading.Lock.Scope: represented by the Lock itself (Scope.Dispose exits it).
        INamedTypeSymbol { Name: "Scope", ContainingType: { Name: "Lock", ContainingNamespace.Name: "Threading" } } => Llvm.Ptr,
        INamedTypeSymbol n when IsSpan(n) => Llvm.Struct(Llvm.Ptr, Llvm.I32),                  // { ref T, length }
        INamedTypeSymbol n when IsSpanEnumerator(n) => Llvm.Struct(Llvm.Ptr, Llvm.I32, Llvm.I32), // { ref T, length, index }
        INamedTypeSymbol { TypeKind: TypeKind.Struct } s when StructType?.Invoke(s) is { } user => user,
        { TypeKind: TypeKind.Struct } => throw CompileError.Unsupported($"struct '{type}'", type),
        { TypeKind: TypeKind.TypeParameter } => throw CompileError.Unsupported($"generic type parameter '{type}'", type),
        _ => throw CompileError.Unsupported($"type '{type}'", type),
    };

    /// <summary>Resolves user-defined structs to their named LLVM type (set by ModuleCodeGen); null if not user-defined.</summary>
    public Func<INamedTypeSymbol, LLVMTypeRef?>? StructType { get; set; }

    public static bool IsSystemStruct(ITypeSymbol? type, string name) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Struct, ContainingType: null } n && n.MetadataName == name
        && n.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };

    /// <summary>
    /// System.Span&lt;T&gt; / ReadOnlySpan&lt;T&gt; — and Memory&lt;T&gt; / ReadOnlyMemory&lt;T&gt;, which share the
    /// { ptr, length } representation: the GC is conservative and non-moving, so an interior data
    /// pointer keeps the owning array/string alive and `.Span` is the identity.
    /// </summary>
    public static bool IsSpan(ITypeSymbol? type) =>
        type is INamedTypeSymbol { MetadataName: "Span`1" or "ReadOnlySpan`1" or "Memory`1" or "ReadOnlyMemory`1", ContainingType: null } n
        && n.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };

    /// <summary>Span&lt;T&gt;.Enumerator / ReadOnlySpan&lt;T&gt;.Enumerator.</summary>
    public static bool IsSpanEnumerator(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Enumerator", ContainingType: { } owner } && IsSpan(owner);

    public static bool IsNullable(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    public static ITypeSymbol NullableUnderlying(ITypeSymbol type) => ((INamedTypeSymbol)type).TypeArguments[0];

    public LLVMTypeRef Storage(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Boolean ? Llvm.I8 : Value(type);

    public static bool IsSigned(ITypeSymbol type) => type switch
    {
        { SpecialType: SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32
            or SpecialType.System_Int64 or SpecialType.System_IntPtr } => true,
        INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } => IsSigned(underlying),
        _ => false,
    };

    public static bool IsFloatingPoint(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
}

/// <summary>
/// Instance layout + method table for one class:
///   instance: { ptr mt, &lt;base fields...&gt;, &lt;own fields...&gt; }
///   method table: { ptr base, ptr name, i32 size, i32 flags, [N x ptr] vtable }
/// vtable slots 0..2 are System.Object's ToString / Equals / GetHashCode (docs/03-lowering.md#classes).
/// </summary>
public sealed class ClassLayout
{
    public const int ToStringSlot = 0, EqualsSlot = 1, GetHashCodeSlot = 2, ObjectSlotCount = 3;

    public required INamedTypeSymbol Type { get; init; }
    public required LLVMTypeRef InstanceType { get; init; }
    public required ClassLayout? Base { get; init; }
    public Dictionary<IFieldSymbol, uint> FieldIndex { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>LLVM struct element types (header + inherited + own fields), so derived classes can extend it.</summary>
    public List<LLVMTypeRef> Elements { get; } = [];

    /// <summary>Slots implemented by a csrt function (library base classes such as System.Exception).</summary>
    public Dictionary<int, string> RuntimeSlots { get; } = [];

    /// <summary>A library class (e.g. System.Exception) whose layout/method table csrt defines.</summary>
    public bool IsLibrary { get; init; }
    public List<IFieldSymbol> Fields { get; } = [];
    /// <summary>Field-like events' synthesized delegate slots (Roslyn doesn't expose their backing fields).</summary>
    public Dictionary<IEventSymbol, uint> EventFieldIndex { get; } = new(SymbolEqualityComparer.Default);
    /// <summary>Fields and event slots in element order, so derived layouts stay prefix-compatible.</summary>
    public List<ISymbol> SlotOrder { get; } = [];

    /// <summary>Slot -> implementing method (null = System.Object default / abstract).</summary>
    public List<IMethodSymbol?> VTable { get; } = [];

    /// <summary>Root virtual method (the original "virtual" declaration) -> slot.</summary>
    public Dictionary<IMethodSymbol, int> SlotOf { get; } = new(SymbolEqualityComparer.Default);

    public LLVMTypeRef MethodTableType { get; set; }
    public LLVMValueRef MethodTable { get; set; }
    public ulong InstanceSize { get; set; }

    /// <summary>The method a virtual slot is keyed by: walk overrides back to the introducing declaration.</summary>
    public static IMethodSymbol RootOf(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        while (method is { IsOverride: true, OverriddenMethod: { } overridden }) method = overridden.OriginalDefinition;
        return method;
    }
}
