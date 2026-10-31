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
        public LLVMValueRef Element { get; set; } // csrt_ArrayMethodTable.element
    }

    private readonly List<TaggedArray> _taggedArrays = [];

    /// <summary>A rectangular array's method table: its own interface map only has the element-typed IEnumerable.</summary>
    private sealed record MdArrayTable(string Name, IArrayTypeSymbol Type, Action<LLVMValueRef, int> SetInterfaceMap)
    {
        public bool HasMap { get; set; }
    }

    private readonly List<MdArrayTable> _mdArrays = [];
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
                && _arrayInterfaces.Add(i))
            {
                _pendingBuiltinInterfaces.Enqueue(i);
                if (s_whyLowered is not null) Console.Error.WriteLine($"array interface {i.ToDisplayString()} (as {itf.ToDisplayString()}) by {_currentCaller?.ToDisplayString() ?? _whyContext}");
            }
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
            Native.LLVMSetInitializer(array.Global, ArrayMethodTableInit(array, map, interfaces.Count));
            array.InterfaceCount = interfaces.Count;
        }
        // Rectangular arrays used as IEnumerable box their elements (MdArrayHelper<T>); their other
        // non-generic interfaces only need what the untyped array tables do (csrt.c falls back to them).
        if (_arrayInterfaces.FirstOrDefault(i => !i.IsGenericType && i.ToDisplayString() == "System.Collections.IEnumerable") is { } enumerable)
            foreach (var md in _mdArrays)
            {
                if (md.HasMap) continue;
                var element = md.Type.ElementType;
                var shared = element.IsReferenceType ? Compilation.GetSpecialType(SpecialType.System_Object) : element;
                if (BuiltinHelper("MdArrayHelper", enumerable, InterfaceSlots(enumerable)[0], shared) is not { } helper) continue;
                changed = true;
                md.HasMap = true;
                var slotsInit = LlvmContext.ConstArray(Llvm.Ptr, [GetFunction(helper).Fn]);
                var slots = Module.AddGlobal($"csrt.mdarraytbl.{md.Name}", Native.LLVMTypeOf(slotsInit), slotsInit, constant: true, linkage: LLVMLinkage.Private);
                var entryType = Llvm.Struct(Llvm.Ptr, Llvm.Ptr);
                var mapInit = LlvmContext.ConstArray(entryType, [LlvmContext.ConstNamedStruct(entryType, InterfaceMethodTable(enumerable), slots)]);
                md.SetInterfaceMap(Module.AddGlobal($"csrt.mdarraymap.{md.Name}", Native.LLVMTypeOf(mapInit), mapInit, constant: true, linkage: LLVMLinkage.Private), 1);
            }
        return changed;
    }

    private LLVMValueRef ArraySlotTable(INamedTypeSymbol itf, TaggedArray array)
    {
        if (!NeedsElementTable(itf))
        {
            if (!_arraySlotTables.TryGetValue(itf, out var shared))
                _arraySlotTables[itf] = shared = SlotTable(itf, "ATBL", m => ArraySlotHelper(itf, m));
            return shared;
        }
        if (_elementSlotTables.TryGetValue((itf, array.Global.Handle), out var table)) return table;
        var element = array.ElementType;
        table = BuiltinSlotTable(itf, $"csrt.arraytbl.{array.Name}.{itf.Name}", LLVMLinkage.Private, m => ElementHelper(itf, m, element));
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

    /// <summary>
    /// The helper in a generic array interface's slot table (IList&lt;U&gt;, ICollection&lt;U&gt;, IReadOnlyList&lt;U&gt;,
    /// ...). For a reference type U the elements are pointers, so SZArrayHelper&lt;object&gt;'s member does the
    /// same thing with the same ABI and one copy serves every U — Contains/IndexOf included, since .NET
    /// compares reference-type elements there with Equals(object) (CoreCLR's shared __Canon code), not
    /// IEquatable&lt;U&gt;. GetEnumerator stays per U: its result must be an IEnumerator&lt;U&gt;. Only slot
    /// tables share: a direct call is typed by the helper's U-typed signature.
    /// </summary>
    private IMethodSymbol? ArraySlotHelper(INamedTypeSymbol itf, IMethodSymbol member)
    {
        if (itf.TypeArguments.FirstOrDefault() is { IsReferenceType: true } element && !TypeSubstitution.IsOpen(element)
            && member.Name != "GetEnumerator")
            return BuiltinHelper("SZArrayHelper", itf, member, Compilation.GetSpecialType(SpecialType.System_Object));
        return ArrayInterfaceHelper(itf, member);
    }

    /// <summary>The helper for a call on an array whose element type is known statically.</summary>
    public IMethodSymbol? ArrayInterfaceHelper(INamedTypeSymbol itf, IMethodSymbol member, ITypeSymbol element) =>
        NeedsElementTable(itf) ? ElementHelper(itf, member, element) : ArrayInterfaceHelper(itf, member);

    /// <summary>
    /// The SZArrayHelper&lt;T&gt; member behind a non-generic interface slot of T[]. A reference-type
    /// element is a pointer that boxes to itself, so arrays of every reference type share
    /// SZArrayHelper&lt;object&gt;'s members (one copy instead of one per element type — 561 array types
    /// in the TechEmpower app). Not the two that must check the destination's element type:
    /// IList's indexer setter (string[] as IList must reject an int) and ICollection.CopyTo (its
    /// typed fast path would accept any reference array as an object[]).
    /// </summary>
    private IMethodSymbol? ElementHelper(INamedTypeSymbol itf, IMethodSymbol member, ITypeSymbol element)
    {
        var typed = (itf.Name, member.Name) is ("IList", "set_Item") or ("ICollection", "CopyTo");
        var shared = element.IsReferenceType && !typed ? Compilation.GetSpecialType(SpecialType.System_Object) : element;
        return BuiltinHelper("SZArrayHelper", itf, member, shared);
    }

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
                _arraySlotTables[itf] = SlotTable(itf, "ATBL", m => ArraySlotHelper(itf, m));
            if (_stringInterfaces.Contains(itf) && !_stringSlotTables.ContainsKey(itf))
                _stringSlotTables[itf] = SlotTable(itf, "STBL", m => StringInterfaceHelper(itf, m));
            RefreshInterfaceTable(itf);
        }
        // CreateInstance may need T[] for newly requested IEnumerable<T> / IList<T> element types.
        EnsureCreateInstanceArrays();
        return RefreshArrayInterfaceMaps() || any;
    }

    private LLVMValueRef SlotTable(INamedTypeSymbol itf, string suffix, Func<IMethodSymbol, IMethodSymbol?> helper) =>
        BuiltinSlotTable(itf, Mangler.TypeSymbol(itf, suffix), LLVMLinkage.Internal, helper);

    /// <summary>
    /// An array or string slot table: like a class's interface map entry (ModuleCodeGen.VTables.cs), a slot
    /// gets its helper only once emitted code dispatches through that interface member; the others point
    /// at csrt_pruned_virtual. Without this every array type the program allocates (a Dictionary's Entry[],
    /// an interpreter's Instruction[]) carried all ~30 IList/ICollection/IEnumerable helpers.
    /// </summary>
    private sealed class PendingBuiltinSlotTable
    {
        public required LLVMValueRef Global { get; init; }
        public required IMethodSymbol?[] Helpers { get; init; }
        public required string Name { get; init; }
        public required string? Instantiation { get; init; }
        public HashSet<int> Requested { get; } = [];
    }

    private readonly List<PendingBuiltinSlotTable> _builtinSlotTables = [];
    private readonly Dictionary<INamedTypeSymbol, List<PendingBuiltinSlotTable>> _builtinSlotTablesByInterface = new(SymbolEqualityComparer.Default);
    private readonly Queue<(PendingBuiltinSlotTable Table, int Slot)> _builtinSlotRequests = new();

    private LLVMValueRef BuiltinSlotTable(INamedTypeSymbol itf, string name, LLVMLinkage linkage, Func<IMethodSymbol, IMethodSymbol?> helper)
    {
        var helpers = InterfaceSlots(itf).Select(helper).ToArray();
        var type = LlvmContext.Array(Llvm.Ptr, (ulong)helpers.Length);
        var table = new PendingBuiltinSlotTable { Global = Module.AddGlobal(name, type, constant: true, linkage: linkage), Helpers = helpers, Name = name,
            Instantiation = InterfaceDispatchKey(itf) };
        _builtinSlotTables.Add(table);
        var definition = itf.OriginalDefinition;
        if (!_builtinSlotTablesByInterface.TryGetValue(definition, out var tables)) _builtinSlotTablesByInterface[definition] = tables = [];
        tables.Add(table);
        if (_dispatchedInterfaceSlots.TryGetValue(definition, out var dispatched))
            foreach (var dispatch in dispatched)
                if (DispatchReaches(dispatch, table.Instantiation)) _builtinSlotRequests.Enqueue((table, dispatch.Slot));
        SetBuiltinSlotTableInitializer(table);
        return table.Global;
    }

    /// <summary>Queues the helpers of builtin slot tables for a newly dispatched interface member.</summary>
    private void NoteBuiltinInterfaceDispatch(INamedTypeSymbol definition, (int Slot, string? Instantiation) dispatch)
    {
        if (_builtinSlotTablesByInterface.TryGetValue(definition, out var tables))
            foreach (var table in tables)
                if (DispatchReaches(dispatch, table.Instantiation)) _builtinSlotRequests.Enqueue((table, dispatch.Slot));
    }

    /// <summary>Declares (queues the lowering of) the helpers of dispatched builtin slots; true when anything was requested.</summary>
    private bool RequestDispatchedBuiltinSlots()
    {
        var requested = false;
        while (_builtinSlotRequests.TryDequeue(out var request))
        {
            var (table, slot) = request;
            if (slot >= table.Helpers.Length || table.Helpers[slot] is not { } helper || !table.Requested.Add(slot)) continue;
            if (s_whyLowered is not null) _whyContext = $"slot of {table.Name}";
            try { GetFunction(helper); }
            catch (CompileError error) { Errors.Add(error); }
            _whyContext = null;
            requested = true;
        }
        return requested;
    }

    private void SetBuiltinSlotTableInitializer(PendingBuiltinSlotTable table)
    {
        var pruned = Runtime("csrt_pruned_virtual", Llvm.Void).Fn;
        var slots = table.Helpers.Select((h, i) => h is null ? LlvmContext.Null(Llvm.Ptr)
            : table.Requested.Contains(i) ? GetFunction(h).Fn : pruned).ToArray();
        Native.LLVMSetInitializer(table.Global, LlvmContext.ConstArray(Llvm.Ptr, slots));
    }

    /// <summary>Sets the builtin slot tables' final initializers (at the dispatch fixed point, with the method tables).</summary>
    private void FinishBuiltinSlotTables()
    {
        foreach (var table in _builtinSlotTables) SetBuiltinSlotTableInitializer(table);
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
