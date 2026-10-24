using System.Reflection;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>Layout of a user-defined struct: a named LLVM struct of storage types, no header.</summary>
public sealed class StructLayout
{
    public required INamedTypeSymbol Type { get; init; }
    public required LLVMTypeRef LlvmType { get; init; }
    public Dictionary<IFieldSymbol, uint> FieldIndex { get; } = new(SymbolEqualityComparer.Default);
    /// <summary>Method table used only when the struct is boxed (created lazily).</summary>
    public LLVMValueRef MethodTable { get; set; }
    /// <summary>Generated ValueType.Equals / GetHashCode on the unboxed value (ModuleCodeGen.StructEquality.cs).</summary>
    public LoweredFunction? DefaultEquals { get; set; }
    public LoweredFunction? DefaultHash { get; set; }
}

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<INamedTypeSymbol, StructLayout> _structs = new(SymbolEqualityComparer.Default);

    private LLVMTypeRef? ResolveStructType(INamedTypeSymbol type)
    {
        type = CoreLib.TryMap(type) ?? type; // e.g. KeyValuePair<K,V>, List<T>.Enumerator -> corelib structs
        return IsUserType(type) && type.TypeKind == TypeKind.Struct ? GetStructLayout(type).LlvmType : null;
    }

    public StructLayout GetStructLayout(INamedTypeSymbol type)
    {
        type = TypeSubstitution.EraseTupleNames(CoreLib.TryMap(type) ?? type, Compilation);
        // Generic structs are laid out per instantiation (the constructed type's fields are substituted).
        if (!TypeSubstitution.IsGenericContext(type)) type = type.OriginalDefinition;
        if (_structs.TryGetValue(type, out var existing)) return existing;
        if (TypeSubstitution.IsOpen(type)) throw CompileError.Unsupported($"open generic struct '{type}'", type);
        return BuildOrForget(_structs, type, BuildStructLayout);
    }

    /// <summary>
    /// Explicit layouts (overlapping [FieldOffset] fields, unions), Pack and Size change where fields
    /// live in memory, which unsafe code, sizeof and P/Invoke observe; csharpc lays fields out
    /// sequentially, so such a type would silently get a different layout. (Field-access aliasing
    /// facts for LLVM, MemoryFacts.cs, also assume fields never overlap.)
    /// </summary>
    internal static void RejectCustomLayout(INamedTypeSymbol type)
    {
        foreach (var a in type.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() != "System.Runtime.InteropServices.StructLayoutAttribute") continue;
            var kind = a.ConstructorArguments.FirstOrDefault().Value;
            if (kind is 2 /* LayoutKind.Explicit */ || a.NamedArguments.Any(n => n.Key is "Pack" or "Size"))
                throw CompileError.Unsupported($"[StructLayout] with explicit layout, Pack or Size on '{type.Name}'", type);
        }
        if (type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.FieldOffsetAttribute")) is { } offsetField)
            throw CompileError.Unsupported($"[FieldOffset] on '{type.Name}.{offsetField.Name}'", offsetField);
    }

    private StructLayout BuildStructLayout(INamedTypeSymbol type)
    {
        // A struct may declare an explicit layout, Pack or Size that places every field exactly where the
        // sequential layout does (Roslyn's ExtendedSpecialType: Explicit, Size = 1, one byte at offset 0):
        // checked against the built layout below. Anything else is still refused.
        var custom = CustomLayoutOf(type);

        // Both repeat their element in memory; laid out as a single field they would silently shrink.
        // [InlineArray(N)] structs from IL (the compiler's <>y__InlineArrayN<T> behind collection expressions
        // and params spans) are N consecutive elements: IL reaches them only through the compiler's
        // InlineArray* helpers (FunctionCodeGen.IlCalls.cs) and the field itself (element 0). C# source would
        // also index them directly, which csharpc doesn't lower yet.
        var inlineArray = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InlineArrayAttribute");
        if (inlineArray is not null)
        {
            var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst).ToList();
            if (Il.OwnerOf(type) is null || custom is not null || fields.Count != 1
                || inlineArray.ConstructorArguments is not [{ Value: int length }] || length <= 0)
                throw CompileError.Unsupported($"[InlineArray] struct '{type.Name}'", type);
            var arrayLayout = new StructLayout { Type = type, LlvmType = Llvm.NamedStruct("struct." + Mangler.MetadataName(type)) };
            _structs[type] = arrayLayout;
            arrayLayout.FieldIndex[fields[0]] = 0;
            arrayLayout.FieldIndex[fields[0].OriginalDefinition] = 0;
            LlvmContext.SetBody(arrayLayout.LlvmType, [LlvmContext.Array(FieldStorage(fields[0]), (uint)length)]);
            return arrayLayout;
        }
        if (type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.IsFixedSizeBuffer) is { } buffer)
            throw CompileError.Unsupported($"fixed-size buffer '{type.Name}.{buffer.Name}'", buffer);

        var layout = new StructLayout { Type = type, LlvmType = Llvm.NamedStruct("struct." + Mangler.MetadataName(type)) };
        _structs[type] = layout; // register first: field types may mention other structs

        List<LLVMTypeRef> elements = [];
        foreach (var f in type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst))
        {
            layout.FieldIndex[f] = (uint)elements.Count;
            layout.FieldIndex[f.OriginalDefinition] = (uint)elements.Count; // same index in every instantiation
            elements.Add(FieldStorage(f));
        }
        if (elements.Count == 0) elements.Add(Llvm.I8); // an empty struct still occupies a byte, as in the CLR
        LlvmContext.SetBody(layout.LlvmType, [.. elements]);
        if (custom is not null && !MatchesSequentialLayout(layout, custom)) throw custom.Rejection;
        return layout;
    }

    /// <summary>A struct's declared layout: [StructLayout]/[FieldOffset] in source, the ClassLayout and FieldLayout tables in IL.</summary>
    private sealed record CustomLayout(bool Explicit, int Pack, int Size, Dictionary<IFieldSymbol, int> Offsets, CompileError Rejection);

    private CustomLayout? CustomLayoutOf(INamedTypeSymbol type)
    {
        if (Il.OwnerOf(type) is { } owner)
        {
            var definition = owner.TypeDefinitionOf(type);
            var layout = definition.GetLayout();
            var isExplicit = (definition.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout;
            var hasFields = InstanceFields(type).Any();
            if (!isExplicit && layout.PackingSize == 0 && (layout.Size == 0 || (layout.Size == 1 && !hasFields))) return null;
            var offsets = new Dictionary<IFieldSymbol, int>(SymbolEqualityComparer.Default);
            if (isExplicit)
                foreach (var f in InstanceFields(type)) offsets[f] = owner.FieldDefinitionOf(f).GetOffset();
            return new CustomLayout(isExplicit, layout.PackingSize, layout.Size, offsets,
                CompileError.Unsupported($"explicit layout, .pack or .size on '{type}' ({owner.Name}.dll)", type));
        }
        var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        var sourceExplicit = attribute?.ConstructorArguments.FirstOrDefault().Value is 2 /* LayoutKind.Explicit */;
        int Named(string name) => attribute?.NamedArguments.FirstOrDefault(n => n.Key == name).Value.Value is int v ? v : 0;
        var fieldOffsets = new Dictionary<IFieldSymbol, int>(SymbolEqualityComparer.Default);
        foreach (var f in InstanceFields(type))
            if (f.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.FieldOffsetAttribute")
                    ?.ConstructorArguments.FirstOrDefault().Value is int offset)
                fieldOffsets[f] = offset;
        var (pack, size) = (Named("Pack"), Named("Size"));
        if (!sourceExplicit && pack == 0 && size == 0 && fieldOffsets.Count == 0) return null;
        var message = fieldOffsets.Count > 0 && !sourceExplicit
            ? $"[FieldOffset] on '{type.Name}.{fieldOffsets.Keys.First().Name}'"
            : $"[StructLayout] with explicit layout, Pack or Size on '{type.Name}'";
        return new CustomLayout(sourceExplicit, pack, size, fieldOffsets, CompileError.Unsupported(message, type));
    }

    private static IEnumerable<IFieldSymbol> InstanceFields(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst);

    /// <summary>
    /// True when the declared layout is the sequential one csharpc built: every explicit offset is the
    /// field's offset, Pack doesn't lower any field's alignment, and Size is absent or the struct's size.
    /// </summary>
    private bool MatchesSequentialLayout(StructLayout layout, CustomLayout custom)
    {
        var target = Types.Target;
        if (custom.Explicit && custom.Offsets.Count != layout.FieldIndex.Keys.Count(f => SymbolEqualityComparer.Default.Equals(f, f.OriginalDefinition)))
            return false;
        uint maxAlign = 1;
        for (uint i = 0; i < Native.LLVMCountStructElementTypes(layout.LlvmType); i++)
            maxAlign = Math.Max(maxAlign, target.AlignOf(Native.LLVMStructGetTypeAtIndex(layout.LlvmType, i)));
        if (custom.Pack != 0 && custom.Pack < maxAlign) return false;
        if (custom.Size != 0 && (ulong)custom.Size != target.SizeOf(layout.LlvmType)) return false;
        foreach (var (field, offset) in custom.Offsets)
            if (!layout.FieldIndex.TryGetValue(field.OriginalDefinition, out var index) || target.OffsetOf(layout.LlvmType, index) != (ulong)offset)
                return false;
        return true;
    }

    /// <summary>LLVM struct type + element index of an instance field of a struct or tuple.</summary>
    public (LLVMTypeRef Type, uint Index) ValueTypeField(IFieldSymbol field)
    {
        var owner = field.ContainingType;
        if (owner.IsTupleType)
        {
            // Named elements (`t.x`) alias the underlying ItemN field.
            var name = (field.CorrespondingTupleField ?? field).Name;
            if (!name.StartsWith("Item", StringComparison.Ordinal) || !uint.TryParse(name.AsSpan(4), out var n) || n is 0 or > 7)
                throw CompileError.Unsupported($"tuple field '{field.Name}'", field);
            return (Types.Value(owner), n - 1);
        }
        var layout = GetStructLayout(owner);
        return layout.FieldIndex.TryGetValue(field.OriginalDefinition, out var index)
            ? (layout.LlvmType, index)
            : throw CompileError.Unsupported($"field '{field.Name}' of struct '{owner.Name}' (its layout could not be built)", field);
    }

    /// <summary>
    /// Method table for boxed instances of a struct: { object MT, name, 8 + size, 0, [ToString, Equals, GetHashCode] }.
    /// Overrides take `this` as a pointer to the struct, so the vtable points at unboxing thunks.
    /// </summary>
    public LLVMValueRef StructMethodTable(INamedTypeSymbol type)
    {
        var layout = GetStructLayout(type);
        if (!layout.MethodTable.IsNull) return layout.MethodTable;

        var size = 8 + Types.Target.SizeOf(layout.LlvmType);
        // Same header as class method tables (TypeMapper.MethodTableHeader): base, name, size, flags,
        // interface map, interface count, reserved — then the vtable.
        var mtType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32,
            LlvmContext.Array(Llvm.Ptr, ClassLayout.ObjectSlotCount));
        var global = Module.AddGlobal(Mangler.TypeSymbol(layout.Type, "MT"), mtType, constant: true, linkage: LLVMLinkage.Internal);
        layout.MethodTable = global;

        // ToString, Equals, GetHashCode: the struct's overrides (through unboxing thunks), else the defaults.
        LLVMValueRef[] entries =
        [
            StructOverride(layout.Type, "ToString") is { } toString ? UnboxingThunk(toString) : Runtime("csrt_object_to_string_default", Llvm.Ptr, Llvm.Ptr).Fn,
            StructOverride(layout.Type, "Equals") is { } equals ? UnboxingThunk(equals) : BoxedDefaultEquals(layout),
            StructOverride(layout.Type, "GetHashCode") is { } hash ? UnboxingThunk(hash) : BoxedDefaultHash(layout),
        ];

        var (interfaceMap, interfaceCount) = EmitStructInterfaceMap(layout.Type);
        Native.LLVMSetInitializer(global, Llvm.ConstStruct(
            ExternalGlobal("csrt_Object_MT"),
            CString(Mangler.MetadataName(layout.Type)),
            Llvm.ConstInt(Llvm.I32, (long)size),
            Llvm.ConstInt(Llvm.I32, 64 /* CSRT_MT_VALUE_TYPE */),
            interfaceMap, Llvm.ConstInt(Llvm.I32, interfaceCount), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, entries)));
        return global;
    }

    /// <summary>Interface map for a boxed struct: slots are unboxing thunks into the struct's methods.</summary>
    private (LLVMValueRef Map, int Count) EmitStructInterfaceMap(INamedTypeSymbol type)
    {
        var interfaces = type.AllInterfaces;
        if (interfaces.IsEmpty) return (LlvmContext.Null(Llvm.Ptr), 0);
        var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
        List<LLVMValueRef> entries = [];
        foreach (var itf in interfaces)
        {
            var slots = InterfaceSlots(itf).Select(member =>
                type.FindImplementationForInterfaceMember(member) is IMethodSymbol impl && IsUserMethod(impl) && !impl.IsAbstract
                    ? UnboxingThunk(impl)
                    : LlvmContext.Null(Llvm.Ptr)).ToArray();
            var slotsInit = LlvmContext.ConstArray(Llvm.Ptr, slots);
            var slotTable = Module.AddGlobal(Mangler.TypeSymbol(type, "ITBL" + entries.Count), Native.LLVMTypeOf(slotsInit), slotsInit,
                constant: true, linkage: LLVMLinkage.Internal);
            entries.Add(LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(itf), slotTable));
        }
        var mapInit = LlvmContext.ConstArray(entryType, [.. entries]);
        var map = Module.AddGlobal(Mangler.TypeSymbol(type, "IMAP"), Native.LLVMTypeOf(mapInit), mapInit,
            constant: true, linkage: LLVMLinkage.Internal);
        return (map, entries.Count);
    }

    /// <summary>thunk(box, args...) => method(box + 8, args...)</summary>
    private LLVMValueRef UnboxingThunk(IMethodSymbol method)
    {
        var target = GetFunction(method);
        var fn = Module.AddFunction(Mangler.Method(method) + "$unbox", target.Type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(fn, "entry"));
        var args = new LLVMValueRef[method.Parameters.Length + 1];
        args[0] = b.InBoundsGep(Llvm.I8, fn.Param(0), [Llvm.ConstInt(Llvm.I64, 8)], "payload");
        for (var i = 1; i < args.Length; i++) args[i] = fn.Param((uint)i);
        var result = b.Call(target.Type, target.Fn, args);
        if (method.ReturnsVoid) b.RetVoid(); else b.Ret(result);
        return fn;
    }
}

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _tupleMethodTables = new(SymbolEqualityComparer.Default);

    /// <summary>Method table for boxed tuples: { header, [ToString, Equals, GetHashCode] } with element-wise thunks.</summary>
    public LLVMValueRef TupleMethodTable(INamedTypeSymbol tuple, FunctionCodeGen _)
    {
        if (_tupleMethodTables.TryGetValue(tuple, out var existing)) return existing;
        var index = _tupleMethodTables.Count;
        var mtType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr, Llvm.I32, Llvm.I32, Llvm.Ptr, Llvm.I32, Llvm.I32, LlvmContext.Array(Llvm.Ptr, ClassLayout.ObjectSlotCount));
        var global = Module.AddGlobal($"__tuple{index}_MT", mtType, constant: true, linkage: LLVMLinkage.Internal);
        _tupleMethodTables[tuple] = global;
        LoweredFunction Make(string name, LLVMTypeRef ret, params LLVMTypeRef[] ps)
        {
            var type = Llvm.FunctionType(ret, ps);
            var fn = Module.AddFunction($"__tuple{index}_{name}", type);
            Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
            return new LoweredFunction(fn, type);
        }
        var toString = Make("ToString", Llvm.Ptr, Llvm.Ptr);
        var equals = Make("Equals", Llvm.I1, Llvm.Ptr, Llvm.Ptr);
        var hash = Make("GetHashCode", Llvm.I32, Llvm.Ptr);
        var size = 8 + Types.Target.SizeOf(Types.Value(tuple));
        var name = "System.ValueTuple`" + tuple.TupleElements.Length + "[" + string.Join(",", tuple.TupleElements.Select(e => ClrName(e.Type))) + "]";
        Native.LLVMSetInitializer(global, Llvm.ConstStruct(ExternalGlobal("csrt_Object_MT"), CString(name),
            Llvm.ConstInt(Llvm.I32, (long)size), Llvm.ConstInt(Llvm.I32, 64 /* CSRT_MT_VALUE_TYPE */), LlvmContext.Null(Llvm.Ptr), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0),
            LlvmContext.ConstArray(Llvm.Ptr, toString.Fn, equals.Fn, hash.Fn)));
        new FunctionCodeGen(this, null, toString).EmitTupleBoxToString(tuple);
        new FunctionCodeGen(this, null, equals).EmitTupleBoxEquals(tuple, global);
        new FunctionCodeGen(this, null, hash).EmitTupleBoxHash(tuple);
        return global;
    }
}
