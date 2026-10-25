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
    private readonly Dictionary<INamedTypeSymbol, HashSet<int>> _dispatchedInterfaceSlots = new(SymbolEqualityComparer.Default);
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

    /// <summary>Emitted code dispatches through slot <paramref name="index"/> of <paramref name="itf"/>.</summary>
    internal void NoteInterfaceDispatch(INamedTypeSymbol itf, int index)
    {
        // Keyed on the generic definition: variance lets an IEnumerable<object> call use an IEnumerable<string> table.
        var definition = itf.OriginalDefinition;
        if (!_dispatchedInterfaceSlots.TryGetValue(definition, out var dispatched)) _dispatchedInterfaceSlots[definition] = dispatched = [];
        if (!dispatched.Add(index)) return;
        CheckNotFinished();
        if (_interfaceEntries.TryGetValue(definition, out var entries))
            foreach (var (table, entry) in entries) _slotRequests.Enqueue((table, index, entry));
    }

    private void CheckNotFinished()
    {
        if (_methodTablesFinished) throw new InvalidOperationException("virtual dispatch emitted after the method tables were finished");
    }

    /// <summary>Registers a class method table: its slots are filled as dispatches through them appear.</summary>
    private void AddPendingMethodTable(PendingMethodTable table)
    {
        _pendingMethodTables.Add(table);
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
            if (_dispatchedInterfaceSlots.TryGetValue(definition, out var dispatched))
                foreach (var index in dispatched) _slotRequests.Enqueue((table, index, j));
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
        var requested = false;
        while (_slotRequests.TryDequeue(out var request))
        {
            var (table, slot, entry) = request;
            // One slot that can't be declared (a library method whose signature mentions an unsupported
            // type) is that slot's error, not the end of code generation for every other method.
            try
            {
                if (entry < 0)
                {
                    if (table.RequestedSlots.Add(slot)) { SlotFunction(table.Layout.VTable[slot]!); requested = true; }
                }
                else if (slot < table.Interfaces[entry].Members.Count && table.RequestedInterfaceSlots.Add((entry, slot)))
                {
                    ImplementationFor(table.Layout, table.Interfaces[entry].Members[slot]);
                    requested = true;
                }
            }
            catch (CompileError error) { Errors.Add(error); }
        }
        return requested;
    }

    /// <summary>Sets every class method table's initializer, with the slots nothing dispatches through pruned.</summary>
    private void FinishMethodTables()
    {
        if (RequestDispatchedSlots()) throw new InvalidOperationException("method tables finished before the dispatch fixed point");
        _methodTablesFinished = true;
        var pruned = Runtime("csrt_pruned_virtual", Llvm.Void).Fn;
        string[] defaults = ["csrt_object_to_string_default", "csrt_object_equals_default", "csrt_object_get_hash_code_default"];
        foreach (var table in _pendingMethodTables)
        {
            var layout = table.Layout;
            var slots = layout.VTable.Select((m, i) => m is not null
                ? table.RequestedSlots.Contains(i) ? SlotFunction(m) : pruned
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
                Llvm.ConstInt(Llvm.I32, (long)layout.InstanceSize),
                Llvm.ConstInt(Llvm.I32, layout.Type.IsAbstract ? 1 : 0),
                map,
                Llvm.ConstInt(Llvm.I32, table.Interfaces.Count),
                Llvm.ConstInt(Llvm.I32, 0),
                LlvmContext.ConstArray(Llvm.Ptr, slots)));
        }
    }
}
