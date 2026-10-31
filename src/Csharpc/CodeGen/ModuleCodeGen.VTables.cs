using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Vtable and interface-map pruning (rapid type analysis on dispatch slots).
///
/// A method table that referenced every virtual of its class would keep all of them alive: creating one
/// CultureInfo would drag in its calendars, CompareInfo, TextInfo, ... although a program formatting a
/// number only ever calls NumberFormat. So a slot is filled only when some emitted code dispatches
/// through it (VirtualCall / LoadVirtualFunction for class slots, InterfaceDispatch for interface
/// slots); the others point at csrt_pruned_virtual, which fails loudly rather than jumping to null.
/// Method-table initializers are therefore set at the very end, once the set of dispatched slots has
/// reached its fixed point (filling a slot can emit code that dispatches through further slots).
/// The three System.Object slots, and Exception.Message, are always kept: the runtime calls them.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private sealed class PendingMethodTable
    {
        public required ClassLayout Layout { get; init; }
        public required LLVMValueRef Global { get; init; }
        public List<(INamedTypeSymbol Interface, List<IMethodSymbol> Members, LLVMValueRef SlotTable)> Interfaces { get; } = [];
        public HashSet<int> RequestedSlots { get; } = [];
        public HashSet<(int Interface, int Slot)> RequestedInterfaceSlots { get; } = [];
        public LLVMValueRef InterfaceMap { get; set; }
    }

    private readonly List<PendingMethodTable> _pendingMethodTables = [];
    private readonly HashSet<IMethodSymbol> _dispatchedVirtuals = new(SymbolEqualityComparer.Default);
    // Per generic definition: the dispatched (slot, instantiation) pairs; a null instantiation is any (InterfaceDispatchKey).
    private readonly Dictionary<INamedTypeSymbol, HashSet<(int Slot, string? Instantiation)>> _dispatchedInterfaceSlots = new(SymbolEqualityComparer.Default);
    // Slots waiting for their root / interface member to be dispatched.
    private readonly Dictionary<IMethodSymbol, List<(PendingMethodTable Table, int Slot)>> _slotsByRoot = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<(PendingMethodTable Table, int Interface)>> _interfaceEntries = new(SymbolEqualityComparer.Default);
    // Slots to fill: (table, vtable slot, -1) or (table, interface member index, interface entry).
    private readonly Queue<(PendingMethodTable Table, int Slot, int Interface)> _slotRequests = new();
    private bool _methodTablesFinished;

    /// <summary>Emitted code dispatches through the vtable slot of <paramref name="root"/> (a SlotOf key).</summary>
    internal void NoteVirtualDispatch(IMethodSymbol root)
    {
        if (!_dispatchedVirtuals.Add(root)) return;
        CheckNotFinished();
        if (_slotsByRoot.Remove(root, out var slots))
            foreach (var (table, slot) in slots) _slotRequests.Enqueue((table, slot, -1));
    }

    /// <summary>
    /// Which instantiations of a generic interface a dispatch through <paramref name="itf"/> can reach: only
    /// itself when every type parameter is invariant (ICollection&lt;T&gt;, IList&lt;T&gt;, IDictionary&lt;K, V&gt;: a call
    /// through ICollection&lt;int&gt; never runs a Dictionary's ICollection&lt;KeyValuePair&lt;K, V&gt;&gt;.CopyTo), else
    /// null, any of them (variance lets an IEnumerable&lt;object&gt; call use an IEnumerable&lt;string&gt; table; an
    /// open instantiation is conservatively any). Compared by .NET metadata name, which corelib shadows share.
    /// </summary>
    private string? InterfaceDispatchKey(INamedTypeSymbol itf) =>
        itf.IsGenericType && !TypeSubstitution.IsOpen(itf) && itf.OriginalDefinition.TypeParameters.All(p => p.Variance == VarianceKind.None)
            ? Mangler.MetadataName(itf) : null;

    private static bool DispatchReaches((int Slot, string? Instantiation) dispatch, string? table) =>
        dispatch.Instantiation is null || table is null || dispatch.Instantiation == table;

    /// <summary>Emitted code dispatches through slot <paramref name="index"/> of <paramref name="itf"/>.</summary>
    internal void NoteInterfaceDispatch(INamedTypeSymbol itf, int index)
    {
        var definition = itf.OriginalDefinition;
        var dispatch = (index, InterfaceDispatchKey(itf));
        if (!_dispatchedInterfaceSlots.TryGetValue(definition, out var dispatched)) _dispatchedInterfaceSlots[definition] = dispatched = [];
        if (!dispatched.Add(dispatch)) return;
        CheckNotFinished();
        if (_interfaceEntries.TryGetValue(definition, out var entries))
            foreach (var (table, entry) in entries)
                if (DispatchReaches(dispatch, InterfaceDispatchKey(table.Interfaces[entry].Interface))) _slotRequests.Enqueue((table, index, entry));
        NoteBuiltinInterfaceDispatch(definition, dispatch);
    }

    private void CheckNotFinished()
    {
        if (_methodTablesFinished) throw new InvalidOperationException("virtual dispatch emitted after the method tables were finished");
    }

    // Rapid type analysis proper: only a class something allocates can be a receiver, so the slots of a
    // method table nothing allocates (abstract classes, bases, classes only named in casts, signatures or
    // typeof) stay pruned, and generic virtual dispatchers get no case for it. A visitor base class with
    // 30 subclasses, of which the program creates two, no longer keeps the other 28's overrides.
    // A table waits here (keyed on its global) until NoteConstructed sees an allocation with it. One that
    // never is gets instance size 0 too, which csrt's GetUninitializedObject reports as "no instances"
    // (a type known only at run time; `GetUninitializedObject(typeof(C))` constructs C).
    private readonly Dictionary<nint, PendingMethodTable> _unconstructedTables = [];
    private readonly HashSet<nint> _constructedMethodTables = [];

    /// <summary>
    /// Generated code allocates an object with this method table (csrt_alloc). Other method tables
    /// (csrt's, arrays', boxes') are ignored.
    /// </summary>
    internal void NoteConstructed(LLVMValueRef methodTable)
    {
        if (!_constructedMethodTables.Add(methodTable.Handle)) return;
        if (_unconstructedTables.Remove(methodTable.Handle, out var table))
        {
            if (s_whyLowered is not null) Console.Error.WriteLine($"constructed {table.Layout.Type.ToDisplayString()} by {_currentCaller?.ToDisplayString()}");
            RegisterMethodTableSlots(table);
        }
    }

    /// <summary>Can an object with this class layout's method table exist at run time (as far as lowering has seen)?</summary>
    internal bool IsConstructed(ClassLayout layout) =>
        !layout.MethodTable.IsNull && _constructedMethodTables.Contains(layout.MethodTable.Handle);

    /// <summary>Registers a class method table: its slots are filled as dispatches through them appear, once it is constructed.</summary>
    private void AddPendingMethodTable(PendingMethodTable table)
    {
        _pendingMethodTables.Add(table);
        if (_constructedMethodTables.Contains(table.Global.Handle)) RegisterMethodTableSlots(table);
        else _unconstructedTables[table.Global.Handle] = table;
    }

    private void RegisterMethodTableSlots(PendingMethodTable table)
    {
        CheckNotFinished();
        var layout = table.Layout;
        var runtimeBase = LibraryBaseOf(layout);
        var keepMessage = runtimeBase is not null && IsLibraryException(runtimeBase);
        // Classes deriving from CancellationTokenSource: csrt calls Dispose(bool) (CancellationTokenSource.Dispose()).
        var keepDispose = runtimeBase is not null && IsRuntimeCancellationSource(runtimeBase);
        var rootsBySlot = layout.SlotOf.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
        for (var i = 0; i < layout.VTable.Count; i++)
        {
            if (layout.VTable[i] is null) continue;
            // The runtime calls the System.Object slots (and Exception.Message) itself.
            if (i < ClassLayout.ObjectSlotCount || (i == ExceptionMessageSlot && keepMessage) || (i == CancellationDisposeBoolSlot && keepDispose))
            { _slotRequests.Enqueue((table, i, -1)); continue; }
            if (!rootsBySlot.TryGetValue(i, out var roots)) continue;
            foreach (var root in roots)
            {
                if (_dispatchedVirtuals.Contains(root)) { _slotRequests.Enqueue((table, i, -1)); break; }
                if (!_slotsByRoot.TryGetValue(root, out var waiting)) _slotsByRoot[root] = waiting = [];
                waiting.Add((table, i));
            }
        }
        for (var j = 0; j < table.Interfaces.Count; j++)
        {
            var definition = table.Interfaces[j].Interface.OriginalDefinition;
            if (!_interfaceEntries.TryGetValue(definition, out var entries)) _interfaceEntries[definition] = entries = [];
            entries.Add((table, j));
            var key = InterfaceDispatchKey(table.Interfaces[j].Interface);
            if (_dispatchedInterfaceSlots.TryGetValue(definition, out var dispatched))
                foreach (var dispatch in dispatched)
                    if (DispatchReaches(dispatch, key)) _slotRequests.Enqueue((table, dispatch.Slot, j));
        }
    }

    /// <summary>The csrt-implemented class a class derives from (a library exception, CancellationTokenSource), or null.</summary>
    private static INamedTypeSymbol? LibraryBaseOf(ClassLayout layout)
    {
        for (var l = layout; l is not null; l = l.Base)
            if (l.IsLibrary) return l.Type;
        return null;
    }

    /// <summary>
    /// Declares the implementations of newly dispatched slots (queueing their bodies); true when anything
    /// was requested, so the worklists must be drained again.
    /// </summary>
    private bool RequestDispatchedSlots()
    {
        var requested = RequestDispatchedBuiltinSlots();
        while (_slotRequests.TryDequeue(out var request))
        {
            var (table, slot, entry) = request;
            if (s_whyLowered is not null)
                _whyContext = $"slot of {table.Layout.Type.ToDisplayString()}" + (entry < 0 ? "" : $" for {table.Interfaces[entry].Interface.ToDisplayString()}");
            // One slot that can't be declared (a library method whose signature mentions an unsupported
            // type) is that slot's error, not the end of code generation for every other method.
            try
            {
                if (entry < 0)
                {
                    if (table.RequestedSlots.Add(slot) && table.Layout.VTable[slot] is { IsAbstract: false } implementation)
                    { SlotFunction(implementation); requested = true; }
                }
                else if (slot < table.Interfaces[entry].Members.Count && table.RequestedInterfaceSlots.Add((entry, slot)))
                {
                    ImplementationFor(table.Layout, table.Interfaces[entry].Members[slot]);
                    requested = true;
                }
            }
            catch (CompileError error) { Errors.Add(error); }
            _whyContext = null;
        }
        return requested;
    }

    /// <summary>Sets every class method table's initializer, with the slots nothing dispatches through pruned.</summary>
    private void FinishMethodTables()
    {
        if (RequestDispatchedSlots()) throw new InvalidOperationException("method tables finished before the dispatch fixed point");
        _methodTablesFinished = true;
        FinishBuiltinSlotTables();
        var pruned = Runtime("csrt_pruned_virtual", Llvm.Void).Fn;
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        foreach (var table in _pendingMethodTables)
        {
            var layout = table.Layout;
            // An abstract override (`public abstract override string ToString()`) leaves its slot without a body.
            var slots = layout.VTable.Select((m, i) => m is not null
                ? table.RequestedSlots.Contains(i) && !m.IsAbstract ? SlotFunction(m) : pruned
                : layout.RuntimeSlots.TryGetValue(i, out var runtimeSlot)
                    ? RuntimeSlotFunction(runtimeSlot)
                : i < ClassLayout.ObjectSlotCount
                    ? Runtime(defaults[i], Llvm.Ptr, Llvm.Ptr).Fn
                    : LlvmContext.Null(Llvm.Ptr)).ToArray();

            for (var j = 0; j < table.Interfaces.Count; j++)
            {
                var (_, members, slotTable) = table.Interfaces[j];
                var implementations = members.Select((member, index) => table.RequestedInterfaceSlots.Contains((j, index))
                    ? ImplementationFor(layout, member) : pruned).ToArray();
                Native.LLVMSetInitializer(slotTable, LlvmContext.ConstArray(Llvm.Ptr, implementations));
            }

            var baseMt = layout.Base?.MethodTable ?? ExternalGlobal("csrt_Object_MT");
            var map = table.Interfaces.Count == 0 ? LlvmContext.Null(Llvm.Ptr) : table.InterfaceMap;
            Native.LLVMSetInitializer(table.Global, Llvm.ConstStruct(
                baseMt,
                CString(Mangler.MetadataName(layout.Type)),
                Llvm.ConstInt(Llvm.I32, IsConstructed(layout) ? (long)layout.InstanceSize : 0),
                Llvm.ConstInt(Llvm.I32, layout.Type.IsAbstract ? 1 : 0),
                map,
                Llvm.ConstInt(Llvm.I32, table.Interfaces.Count),
                Llvm.ConstInt(Llvm.I32, 0),
                LlvmContext.ConstArray(Llvm.Ptr, slots)));
        }
    }
}
