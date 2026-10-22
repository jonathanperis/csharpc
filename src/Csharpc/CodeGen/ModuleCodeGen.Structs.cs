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
}

public sealed partial class ModuleCodeGen
{
    private readonly Dictionary<INamedTypeSymbol, StructLayout> _structs = new(SymbolEqualityComparer.Default);

    private LLVMTypeRef? ResolveStructType(INamedTypeSymbol type) =>
        IsUserType(type) && type.TypeKind == TypeKind.Struct ? GetStructLayout(type).LlvmType : null;

    public StructLayout GetStructLayout(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (_structs.TryGetValue(type, out var existing)) return existing;
        if (type.IsGenericType) throw CompileError.Unsupported($"generic struct '{type.Name}'", type);

        var layout = new StructLayout { Type = type, LlvmType = Llvm.NamedStruct("struct." + Mangler.MetadataName(type)) };
        _structs[type] = layout; // register first: field types may mention other structs

        List<LLVMTypeRef> elements = [];
        foreach (var f in type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst))
        {
            layout.FieldIndex[f] = (uint)elements.Count;
            elements.Add(Types.Storage(f.Type));
        }
        if (elements.Count == 0) elements.Add(Llvm.I8); // an empty struct still occupies a byte, as in the CLR
        LlvmContext.SetBody(layout.LlvmType, [.. elements]);
        return layout;
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
        return (layout.LlvmType, layout.FieldIndex[field.OriginalDefinition]);
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

        (string Name, string Default)[] slots =
            [("ToString", "csrt_object_to_string_default"), ("Equals", "csrt_valuetype_equals"), ("GetHashCode", "csrt_valuetype_get_hash_code")];
        var entries = slots.Select(slot =>
        {
            var overrider = layout.Type.GetMembers(slot.Name).OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.IsOverride && ClassLayout.RootOf(m).ContainingType.SpecialType == SpecialType.System_Object);
            return overrider is not null ? UnboxingThunk(overrider) : Runtime(slot.Default, Llvm.Ptr, Llvm.Ptr).Fn;
        }).ToArray();

        var (interfaceMap, interfaceCount) = EmitStructInterfaceMap(layout.Type);
        Native.LLVMSetInitializer(global, Llvm.ConstStruct(
            ExternalGlobal("csrt_Object_MT"),
            CString(Mangler.MetadataName(layout.Type)),
            Llvm.ConstInt(Llvm.I32, (long)size),
            Llvm.ConstInt(Llvm.I32, 0),
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
