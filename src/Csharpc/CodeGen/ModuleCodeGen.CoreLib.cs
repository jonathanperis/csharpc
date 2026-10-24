using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Arrays and strings as BCL interfaces (int[] as IEnumerable&lt;int&gt;/IList&lt;int&gt;, string as
/// IEnumerable&lt;char&gt;). They share one runtime method table each, so instead of interface maps the
/// *interface's* identity table carries the implementation: its `interfaces` field points at a slot
/// table of CsharpcCoreLib.SZArrayHelper&lt;T&gt; static methods (used when the receiver is an array), its
/// `base` field at CsharpcCoreLib.StringInterfaces methods (receiver is a string); csrt falls back
/// to them (src/runtime/csrt.c builtin_interface_slots). A static (T[] self, args...) method has the
/// same ABI as the interface method (ptr this, args...).
///
/// Tables are only built for interfaces the program actually converts an array/string to.
///
/// Arrays allocated by generated code carry a per-element-type method table ("System.Int32[]",
/// ModuleCodeGen.Bcl.cs). Those get a real interface map: the requested interfaces their element
/// type implements (with array covariance for reference elements), so `object is IEnumerable&lt;X&gt;`
/// checks the element type, and the non-generic IEnumerable/IList, which have to box elements, get
/// per-element-type tables. The shared tables only serve untagged arrays made by the C runtime.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly HashSet<INamedTypeSymbol> _arrayInterfaces = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _stringInterfaces = new(SymbolEqualityComparer.Default);
    private readonly Queue<INamedTypeSymbol> _pendingBuiltinInterfaces = new();
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _arraySlotTables = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, LLVMValueRef> _stringSlotTables = new(SymbolEqualityComparer.Default);

    private sealed record TaggedArray(string Name, ITypeSymbol ElementType, LLVMValueRef Global)
    {
        public int InterfaceCount { get; set; }
    }

    private readonly List<TaggedArray> _taggedArrays = [];
    private readonly Dictionary<(INamedTypeSymbol, nint), LLVMValueRef> _elementSlotTables = [];

    /// <summary>Non-generic interfaces of T[] (System.Array) that csharpc implements.</summary>
    private static bool IsNonGenericArrayInterface(INamedTypeSymbol itf) =>
        itf.ToDisplayString() is "System.Collections.ICollection" or "System.Collections.IEnumerable" or "System.Collections.IList"
            or "System.Collections.IStructuralComparable" or "System.Collections.IStructuralEquatable";

    /// <summary>Non-generic interfaces whose members depend on the element type (boxing).</summary>
    private static bool NeedsElementTable(INamedTypeSymbol itf) =>
        !itf.IsGenericType && itf.ToDisplayString() is "System.Collections.IEnumerable" or "System.Collections.IList" or "System.Collections.ICollection"
            or "System.Collections.IStructuralComparable" or "System.Collections.IStructuralEquatable";

    public void RequestArrayInterface(INamedTypeSymbol itf)
    {
        foreach (var i in (IEnumerable<INamedTypeSymbol>)[itf, .. itf.AllInterfaces])
            if ((i.IsGenericType ? !TypeSubstitution.IsOpen(i) : IsNonGenericArrayInterface(i))
                && _arrayInterfaces.Add(i)) _pendingBuiltinInterfaces.Enqueue(i);
    }

    /// <summary>Does an array with this element type implement the (requested) interface?</summary>
    private bool ArrayImplements(ITypeSymbol element, INamedTypeSymbol itf)
    {
        if (!itf.IsGenericType) return true;
        if (itf.TypeArguments.Length != 1) return false;
        var name = itf.OriginalDefinition.ToDisplayString();
        if (name is not ("System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.ICollection<T>" or "System.Collections.Generic.IList<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.IReadOnlyList<T>")) return false;
        // Compare as codegen sees types: List<T> and its corelib shadow are the same element type.
        element = CoreLib.Map(element);
        var u = CoreLib.Map(itf.TypeArguments[0]);
        if (SymbolEqualityComparer.Default.Equals(element, u)) return true;
        // Array covariance: string[] is an IList<object>.
        if (element.IsReferenceType && u.IsReferenceType)
            return Compilation.ClassifyCommonConversion(element, u) is { IsImplicit: true, IsReference: true };
        // The CLR's array-element compatibility (ECMA-335 I.8.7.1): enums reduce to their underlying
        // type and signed/unsigned integers of one size are interchangeable, so a Day[] (enum : int)
        // is an IEnumerable<int> and an int[] an IList<uint>.
        return ReducedElementType(element) is { } re && re == ReducedElementType(u);
    }

    private static string? ReducedElementType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }) type = underlying;
        return type.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte => "int8",
            SpecialType.System_Int16 or SpecialType.System_UInt16 => "int16",
            SpecialType.System_Int32 or SpecialType.System_UInt32 => "int32",
            SpecialType.System_Int64 or SpecialType.System_UInt64 => "int64",
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr => "native int",
            _ => null,
        };
    }

    /// <summary>(Re)builds the interface maps of per-element-type array method tables; true if any changed.</summary>
    private bool RefreshArrayInterfaceMaps()
    {
        var changed = false;
        for (var a = 0; a < _taggedArrays.Count; a++)
        {
            var array = _taggedArrays[a];
            var interfaces = _arrayInterfaces.Where(i => ArrayImplements(array.ElementType, i))
                .OrderBy(i => i.ToDisplayString(), StringComparer.Ordinal).ToList();
            if (interfaces.Count == array.InterfaceCount) continue; // the set only grows
            changed = true;
            var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
            var entries = interfaces.Select(i => LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(i), ArraySlotTable(i, array))).ToArray();
            var mapInit = LlvmContext.ConstArray(entryType, entries);
            var map = Module.AddGlobal($"csrt.arraymap.{array.Name}.{interfaces.Count}", Native.LLVMTypeOf(mapInit), mapInit,
                constant: true, linkage: LLVMLinkage.Private);
            Native.LLVMSetInitializer(array.Global, ArrayMethodTableInit(array.Name, array.ElementType, map, interfaces.Count));
            array.InterfaceCount = interfaces.Count;
        }
        return changed;
    }

    private LLVMValueRef ArraySlotTable(INamedTypeSymbol itf, TaggedArray array)
    {
        if (!NeedsElementTable(itf))
        {
            if (!_arraySlotTables.TryGetValue(itf, out var shared))
                _arraySlotTables[itf] = shared = SlotTable(itf, "ATBL", m => ArrayInterfaceHelper(itf, m));
            return shared;
        }
        if (_elementSlotTables.TryGetValue((itf, array.Global.Handle), out var table)) return table;
        var element = array.ElementType;
        var slots = InterfaceSlots(itf).Select(m => BuiltinHelper("SZArrayHelper", itf, m, element) is { } h ? GetFunction(h).Fn : LlvmContext.Null(Llvm.Ptr)).ToArray();
        var init = LlvmContext.ConstArray(Llvm.Ptr, slots);
        table = Module.AddGlobal($"csrt.arraytbl.{array.Name}.{itf.Name}", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Private);
        return _elementSlotTables[(itf, array.Global.Handle)] = table;
    }

    public void RequestStringInterface(INamedTypeSymbol itf)
    {
        foreach (var i in (IEnumerable<INamedTypeSymbol>)[itf, .. itf.AllInterfaces])
            if (i.IsGenericType && _stringInterfaces.Add(i)) _pendingBuiltinInterfaces.Enqueue(i);
    }

    /// <summary>Corelib helper implementing <paramref name="member"/> of <paramref name="itf"/> for arrays, if any.</summary>
    public IMethodSymbol? ArrayInterfaceHelper(INamedTypeSymbol itf, IMethodSymbol member) =>
        BuiltinHelper("SZArrayHelper", itf, member, itf.TypeArguments.FirstOrDefault());

    /// <summary>The helper for a call on an array whose element type is known statically.</summary>
    public IMethodSymbol? ArrayInterfaceHelper(INamedTypeSymbol itf, IMethodSymbol member, ITypeSymbol element) =>
        NeedsElementTable(itf) ? BuiltinHelper("SZArrayHelper", itf, member, element) : ArrayInterfaceHelper(itf, member);

    public IMethodSymbol? StringInterfaceHelper(INamedTypeSymbol itf, IMethodSymbol member) =>
        BuiltinHelper("StringInterfaces", itf, member, null);

    private IMethodSymbol? BuiltinHelper(string helperName, INamedTypeSymbol itf, IMethodSymbol member, ITypeSymbol? elementType)
    {
        var helper = CoreLib.TypeNamed(helperName + (elementType is null ? "" : "`1"));
        if (helper is null) return null;
        if (elementType is not null) helper = helper.Construct(elementType);
        var name = itf.MetadataName.Replace("`", "") + "_" + member.Name;
        return helper.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault();
    }

    /// <summary>Builds slot tables for newly requested interfaces; true if anything was emitted.</summary>
    private bool EmitPendingBuiltinInterfaceTables()
    {
        var any = false;
        while (_pendingBuiltinInterfaces.TryDequeue(out var itf))
        {
            any = true;
            if (_arrayInterfaces.Contains(itf) && !_arraySlotTables.ContainsKey(itf))
                _arraySlotTables[itf] = SlotTable(itf, "ATBL", m => ArrayInterfaceHelper(itf, m));
            if (_stringInterfaces.Contains(itf) && !_stringSlotTables.ContainsKey(itf))
                _stringSlotTables[itf] = SlotTable(itf, "STBL", m => StringInterfaceHelper(itf, m));
            RefreshInterfaceTable(itf);
        }
        return RefreshArrayInterfaceMaps() || any;
    }

    private LLVMValueRef SlotTable(INamedTypeSymbol itf, string suffix, Func<IMethodSymbol, IMethodSymbol?> helper)
    {
        var slots = InterfaceSlots(itf).Select(m => helper(m) is { } h ? GetFunction(h).Fn : LlvmContext.Null(Llvm.Ptr)).ToArray();
        var init = LlvmContext.ConstArray(Llvm.Ptr, slots);
        return Module.AddGlobal(Mangler.TypeSymbol(itf, suffix), Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
    }

    private void RefreshInterfaceTable(INamedTypeSymbol itf)
    {
        var global = InterfaceMethodTable(itf);
        var init = Llvm.ConstStruct(
            _stringSlotTables.GetValueOrDefault(itf, LlvmContext.Null(Llvm.Ptr)), CString(Mangler.MetadataName(itf)),
            Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 2 /* CSRT_MT_INTERFACE */),
            _arraySlotTables.GetValueOrDefault(itf, LlvmContext.Null(Llvm.Ptr)), Llvm.ConstInt(Llvm.I32, 0), Llvm.ConstInt(Llvm.I32, 0));
        Native.LLVMSetInitializer(global, init);
    }
}
