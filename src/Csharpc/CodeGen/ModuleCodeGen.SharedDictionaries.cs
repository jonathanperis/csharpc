using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>What a shared generic body reads from its class's generic dictionary (ADR 0026, step 3b-1).</summary>
internal enum SharedHandle
{
    /// <summary>The method table a cast or type test compares against (csrt_isinst_class / csrt_castclass).</summary>
    Cast,
    /// <summary>The method table that identifies the type to reflection (typeof).</summary>
    TypeOf,
    /// <summary>The method table of the array type whose element is the slot's type (new T[], ldelema's exact-type check).</summary>
    Array,
    /// <summary>The method table a `new` of the class allocates with.</summary>
    Alloc,
}

/// <summary>
/// Generic dictionaries of shared generic code (ADR 0026, step 3b-1). A canonical body (List&lt;__Canon0&gt;.Grow) cannot name the method table
/// of T[], so it reads it from the dictionary of the object it runs on: this -> method table -> the dictionary slot of the generic class ->
/// entry. Slots are assigned per canonical class as its bodies are lowered, in lowering order (which is single-threaded and deterministic),
/// and every exact instantiation of the class gets every slot filled: the queue below is drained with the function worklists, because
/// filling an entry can need a new method table and what it references. The constants are emitted once nothing is left to lower.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private sealed class SharedDictionary
    {
        public readonly List<(SharedHandle Kind, ITypeSymbol Type)> Slots = [];
        public readonly Dictionary<ITypeSymbol, int>[] Index = [.. Enumerable.Range(0, 4).Select(_ => new Dictionary<ITypeSymbol, int>(SymbolEqualityComparer.Default))];
        public readonly List<SharedInstance> Instances = [];
    }

    private sealed class SharedInstance(ClassLayout layout, SharedDictionary dictionary)
    {
        public readonly ClassLayout Layout = layout;
        public readonly SharedDictionary Dictionary = dictionary;
        public readonly List<ITypeSymbol> Arguments = CodeGen.SharedGenerics.FlatArguments(layout.Type);
        public readonly List<LLVMValueRef> Entries = [];
    }

    private readonly Dictionary<INamedTypeSymbol, SharedDictionary> _sharedDictionaries = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, SharedInstance> _sharedInstances = new(SymbolEqualityComparer.Default);
    private readonly Queue<(SharedInstance Instance, int Slot)> _pendingSharedFills = new();
    private bool _sharedDictionariesEmitted;
    private LLVMValueRef _canonMethodTable;

    /// <summary>
    /// The function a delegate to <paramref name="method"/> calls. A method compiled as its canonical form shares that function with every
    /// instantiation it stands for, and Delegate.Method answers from the function a delegate holds (the delegate-method table is keyed by
    /// it), so a delegate gets a forwarder of its own per exact instantiation: `Repo&lt;Customer&gt;.Name` and `Repo&lt;string&gt;.Name` stay
    /// two methods to reflection, and two delegates to the same instantiation still hold the same function (delegate equality).
    /// </summary>
    internal LLVMValueRef DelegateEntry(IMethodSymbol method, LoweredFunction lowered)
    {
        if (!SharedGenerics || !SharedGenericsInfo.Active || SharedGenericsInfo.ShareAs(method) is not { } canonical
            || SymbolEqualityComparer.Default.Equals(canonical, method)) return lowered.Fn;
        var key = "exact:" + Mangler.Method(method);
        if (_globals.TryGetValue(key, out var existing)) return existing;
        var entry = Module.AddFunction(Mangler.Method(method) + "$exact", lowered.Type);
        Native.LLVMSetLinkage(entry, LLVMLinkage.Internal);
        using var b = new IrBuilder(Llvm);
        b.PositionAtEnd(b.AppendBlock(entry, "entry"));
        var args = new LLVMValueRef[Native.LLVMCountParams(entry)];
        for (var i = 0; i < args.Length; i++) args[i] = entry.Param((uint)i);
        var result = b.Call(lowered.Type, lowered.Fn, args);
        if (Native.LLVMGetReturnType(lowered.Type).IsVoid) b.RetVoid(); else b.Ret(result);
        return _globals[key] = entry;
    }

    /// <summary>Does the type name a canonical marker? Only shared generic code (--shared-generics) has such types.</summary>
    internal bool IsCanonDependent(ITypeSymbol? type) => SharedGenerics && type is not null && SharedGenericsInfo.Active && SharedGenericsInfo.MentionsMarker(type);

    /// <summary>
    /// Reflection describes exact types only: a canonical type reaching it is a lowering path that should have kept its method exact (or
    /// taken the type from the dictionary). An internal error that names the path, rather than metadata about "__Canon0".
    /// </summary>
    internal void CheckNotCanonical(ITypeSymbol type, string what)
    {
        if (IsCanonDependent(type))
            throw new CompileError("CSLLVM0004", $"internal error: {what} for the canonical type '{type}' (lowering {_currentCaller?.ToDisplayString() ?? "?"})", null);
    }

    /// <summary>
    /// What stands for the method table of a type that names a marker. No such table exists: shared code takes the exact one from its
    /// dictionary. Any use of this symbol left at the end is a lowering path that was not taught to, and fails the compile
    /// (<see cref="CheckSharedGenericLeaks"/>) rather than letting a marker reach the program.
    /// </summary>
    internal LLVMValueRef CanonMethodTable =>
        _canonMethodTable.IsNull ? _canonMethodTable = Module.AddGlobal("csharpc.shared.marker", Llvm.I8, constant: true) : _canonMethodTable;

    private SharedDictionary SharedDictionaryOf(INamedTypeSymbol canonical) =>
        _sharedDictionaries.TryGetValue(canonical, out var dictionary) ? dictionary : _sharedDictionaries[canonical] = new SharedDictionary();

    /// <summary>An exact instantiation of a shared generic class: its dictionary gets every slot the canonical bodies ask for.</summary>
    private void RegisterSharedInstance(ClassLayout layout)
    {
        if (SharedGenericsInfo.CanonicalizeArguments(layout.Type) is not { } canonical) return;
        var dictionary = SharedDictionaryOf(canonical);
        var instance = new SharedInstance(layout, dictionary);
        _sharedInstances[layout.Type] = instance;
        dictionary.Instances.Add(instance);
        for (var slot = 0; slot < dictionary.Slots.Count; slot++) _pendingSharedFills.Enqueue((instance, slot));
    }

    /// <summary>
    /// The index, in the dictionaries of <paramref name="canonicalOwner"/>'s instantiations, of the handle a canonical body asks for
    /// (entry 0 is the instantiation's own method table). The body's method must have named the type: the eligibility analysis
    /// (SharedGenerics.ExactReason) decided from the same body that a dictionary can serve it.
    /// </summary>
    internal int SharedSlot(INamedTypeSymbol canonicalOwner, IMethodSymbol method, SharedHandle kind, ITypeSymbol type)
    {
        var dictionary = SharedDictionaryOf(canonicalOwner);
        if (dictionary.Index[(int)kind].TryGetValue(type, out var known)) return known + 1;
        if (!BodyNames(method, type))
            throw new CompileError("CSLLVM0004", $"internal error: shared generic code '{method.OriginalDefinition}' asks its dictionary for the {kind} handle of '{type}', which its body does not name", null);
        if (_sharedDictionariesEmitted)
            throw new CompileError("CSLLVM0004", $"internal error: shared generic code '{method.OriginalDefinition}' lowered after the generic dictionaries were emitted", null);
        var slot = dictionary.Slots.Count;
        dictionary.Slots.Add((kind, type));
        dictionary.Index[(int)kind][type] = slot;
        foreach (var instance in dictionary.Instances) _pendingSharedFills.Enqueue((instance, slot));
        return slot + 1;
    }

    private readonly Dictionary<IMethodSymbol, HashSet<ITypeSymbol>> _sharedNamedTypes = new(SymbolEqualityComparer.Default);

    /// <summary>Is the type one the canonical method's body names (SharedGenerics.NamedTypes), as lowering sees types: corelib-redirected?</summary>
    private bool BodyNames(IMethodSymbol method, ITypeSymbol type)
    {
        ITypeSymbol Lowered(ITypeSymbol t) => CoreLib.Map(TypeSubstitution.EraseTupleNames(t, Compilation));
        if (!_sharedNamedTypes.TryGetValue(method, out var named))
        {
            named = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var t in SharedGenericsInfo.NamedTypes(method)) named.Add(Lowered(t));
            _sharedNamedTypes[method] = named;
        }
        return named.Contains(Lowered(type));
    }

    /// <summary>Fills the dictionary entries queued since the last round; true when there were any (they may have queued functions).</summary>
    private bool FillSharedDictionaries()
    {
        if (_pendingSharedFills.Count == 0) return false;
        while (_pendingSharedFills.TryDequeue(out var fill))
        {
            var (instance, slot) = fill;
            var (kind, canonical) = instance.Dictionary.Slots[slot];
            var exact = SharedGenericsInfo.Instantiate(canonical, instance.Arguments);
            while (instance.Entries.Count <= slot) instance.Entries.Add(default);
            instance.Entries[slot] = SharedEntry(kind, exact);
        }
        return true;
    }

    /// <summary>The handle exact code would have used for <paramref name="exact"/>: the same calls FunctionCodeGen makes for a known type.</summary>
    private LLVMValueRef SharedEntry(SharedHandle kind, ITypeSymbol exact)
    {
        switch (kind)
        {
            case SharedHandle.Cast:
                // (a T[] can be cast to the interfaces arrays implement: IlCastClass / IsInstance request them for a known target)
                if (exact is INamedTypeSymbol { TypeKind: TypeKind.Interface } itf) RequestArrayInterface(itf);
                return FunctionCodeGen.MethodTableFor(this, exact, null!);
            case SharedHandle.TypeOf:
                if (exact is INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType) NoteDelegateType(delegateType);
                return TypeMethodTable(exact, null!);
            case SharedHandle.Array:
                return ArrayMethodTable(exact);
            default:
                var layout = GetLayout((INamedTypeSymbol)exact);
                NoteConstructed(layout.MethodTable); // objects of it exist: its vtable slots can be dispatched
                return layout.MethodTable;
        }
    }

    /// <summary>Did the canonical bodies of this exact class's canonical form ask for any handle?</summary>
    private bool HasDictionarySlots(ClassLayout exact) =>
        _sharedInstances.TryGetValue(exact.Type, out var instance) && instance.Dictionary.Slots.Count > 0;

    /// <summary>
    /// One constant per exact instantiation of a shared generic class: [its own method table, then the handle of each slot of its canonical
    /// class]. Emitted with the method tables, once nothing is left to lower.
    /// </summary>
    private LLVMValueRef GenericDictionary(ClassLayout exact)
    {
        if (_genericDictionaries.TryGetValue(exact.Type, out var existing)) return existing;
        _sharedDictionariesEmitted = true;
        var entries = new List<LLVMValueRef> { exact.MethodTable };
        if (_sharedInstances.TryGetValue(exact.Type, out var instance))
        {
            if (_pendingSharedFills.Count != 0 || instance.Entries.Count != instance.Dictionary.Slots.Count)
                throw new CompileError("CSLLVM0004", $"internal error: the generic dictionary of '{exact.Type}' is emitted before it is filled", null);
            entries.AddRange(instance.Entries);
        }
        var init = LlvmContext.ConstArray(Llvm.Ptr, [.. entries]);
        var global = Module.AddGlobal(Mangler.TypeSymbol(exact.Type, "GDICT"), Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        return _genericDictionaries[exact.Type] = global;
    }

    /// <summary>
    /// The end-of-module guard (ADR 0026: markers are never observable): no instruction or constant refers to the method table of a type
    /// that names a marker, and no global variable or type-name string does. A hit is an internal error, never a program that could show
    /// "__Canon0" to its user.
    /// </summary>
    private void CheckSharedGenericLeaks()
    {
        if (!SharedGenerics) return;
        if (!_canonMethodTable.IsNull)
        {
            var users = new SortedSet<string>(StringComparer.Ordinal);
            // (through constant expressions and aggregates, up to the function or global that holds the use)
            void Collect(LLVMValueRef value, int depth)
            {
                for (var use = Native.LLVMGetFirstUse(value); use != 0; use = Native.LLVMGetNextUse(use))
                {
                    var user = Native.LLVMGetUser(use);
                    if (!Native.LLVMIsAInstruction(user).IsNull)
                        users.Add(Native.LLVMGetValueName(Native.LLVMGetBasicBlockParent(Native.LLVMGetInstructionParent(user))));
                    else if (!Native.LLVMIsAGlobalVariable(user).IsNull) users.Add("global " + Native.LLVMGetValueName(user));
                    else if (depth < 8) Collect(user, depth + 1);
                    else users.Add("(a constant)");
                }
            }
            Collect(_canonMethodTable, 0);
            if (users.Count > 0)
                throw new CompileError("CSLLVM0004", "internal error: shared generic code uses the method table of a canonical marker in "
                    + string.Join(", ", users.Take(8)) + (users.Count > 8 ? $" and {users.Count - 8} more" : ""), null);
            Native.LLVMDeleteGlobal(_canonMethodTable);
        }
        for (var global = Native.LLVMGetFirstGlobal(Module.Ref); !global.IsNull; global = Native.LLVMGetNextGlobal(global))
            if (IsMarkerName(Native.LLVMGetValueName(global)))
                throw new CompileError("CSLLVM0004", $"internal error: the global '{Native.LLVMGetValueName(global)}' names a canonical marker", null);
        foreach (var key in _globals.Keys)
            if (key.StartsWith("c:", StringComparison.Ordinal) && IsMarkerName(key))
                throw new CompileError("CSLLVM0004", $"internal error: the type-name string '{key[2..]}' names a canonical marker", null);
    }

    private static bool IsMarkerName(string name)
    {
        for (var at = name.IndexOf("__Canon", StringComparison.Ordinal); at >= 0; at = name.IndexOf("__Canon", at + 1, StringComparison.Ordinal))
            if (at + 7 < name.Length && char.IsAsciiDigit(name[at + 7])) return true;
        return false;
    }
}
