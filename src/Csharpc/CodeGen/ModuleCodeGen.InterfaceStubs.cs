using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Guarded devirtualization of interface calls (closed world). An interface call goes through a stub per
/// interface slot, `ret stub(args..., site cache)`, inlined at every call site, whose body is written once
/// the program's classes are all known (FinishMethodTables): for each constructed class whose interface
/// map holds the interface (at most <see cref="MaxGuardedClasses"/> of them) it compares the receiver's
/// method table and calls that class's implementation directly — the same function its slot table holds,
/// so the result is the same whichever path runs — and otherwise dispatches through the call site's cache
/// as before. Most of Kestrel's request path calls interfaces with a single implementation in the program
/// (IHttpApplication, ITimeoutControl, the feature interfaces HttpProtocol implements): a direct call is
/// cheaper than the cached lookup and indirect call, and LLVM can inline it. A receiver of any other type
/// (another class, a boxed struct, an array, a string, a variant instantiation) takes the cached path,
/// so the guards never need the full set of implementing types.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>Compares per call before the cached lookup: more classes than this dispatch through the cache.</summary>
    private const int MaxGuardedClasses = 3;

    private sealed class InterfaceCallStub
    {
        public required LLVMValueRef Interface { get; init; }  // the interface's method table
        public required int Index { get; init; }
        public required IMethodSymbol Member { get; init; }
        public required LLVMTypeRef CalleeType { get; init; }  // the interface method's function type
        public required LLVMTypeRef Type { get; init; }        // CalleeType's parameters + the site cache
        public required LLVMValueRef Fn { get; init; }
    }

    private readonly Dictionary<(nint Interface, int Index, nint Type), InterfaceCallStub> _interfaceStubs = [];

    /// <summary>
    /// The stub for calls to slot <paramref name="index"/> of <paramref name="itf"/> whose function type is
    /// <paramref name="calleeType"/>: call it with the call's arguments and the site's cache word appended.
    /// </summary>
    internal LoweredFunction InterfaceCallStubFor(INamedTypeSymbol itf, int index, IMethodSymbol member, LLVMTypeRef calleeType)
    {
        var mt = InterfaceMethodTable(itf);
        var key = (mt.Handle, index, calleeType.Handle);
        if (_interfaceStubs.TryGetValue(key, out var existing)) return new LoweredFunction(existing.Fn, existing.Type);
        var parameters = new LLVMTypeRef[Native.LLVMCountParamTypes(calleeType)];
        if (parameters.Length > 0) Native.LLVMGetParamTypes(calleeType, parameters);
        var type = Llvm.FunctionType(Native.LLVMGetReturnType(calleeType), [.. parameters, Llvm.Ptr]);
        var fn = Module.AddFunction($"csrt.istub.{_interfaceStubs.Count}", type);
        Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
        Llvm.AddFunctionAttribute(fn, "alwaysinline");
        Llvm.AddFunctionAttribute(fn, "uwtable", 2); // the implementations may throw (ADR 0005)
        _interfaceStubs[key] = new InterfaceCallStub { Interface = mt, Index = index, Member = member, CalleeType = calleeType, Type = type, Fn = fn };
        return new LoweredFunction(fn, type);
    }

    /// <summary>
    /// Writes every stub's body. Runs when the method tables are finished: the constructed classes and the
    /// implementations of every dispatched interface slot are final (and already lowered).
    /// </summary>
    private void EmitInterfaceCallStubs()
    {
        if (_interfaceStubs.Count == 0) return;
        // Per interface method table: the constructed classes listing it, with their first entry for it
        // (the one csrt_interface_slots finds).
        var implementers = new Dictionary<nint, List<(PendingMethodTable Table, int Entry)>>();
        foreach (var table in _pendingMethodTables)
        {
            if (!IsConstructed(table.Layout)) continue;
            var seen = new HashSet<nint>();
            for (var j = 0; j < table.Interfaces.Count; j++)
            {
                var mt = InterfaceMethodTable(table.Interfaces[j].Interface).Handle;
                if (!seen.Add(mt)) continue;
                if (!implementers.TryGetValue(mt, out var list)) implementers[mt] = list = [];
                list.Add((table, j));
            }
        }
        var lookup = Runtime("csrt_interface_slots_cached", Llvm.Ptr, Llvm.Ptr, Llvm.Ptr, Llvm.Ptr);
        using var b = new IrBuilder(Llvm);
        foreach (var stub in _interfaceStubs.Values)
        {
            var count = (int)Native.LLVMCountParams(stub.Fn);
            var args = Enumerable.Range(0, count - 1).Select(i => stub.Fn.Param((uint)i)).ToArray();
            var self = args[0];
            var site = stub.Fn.Param((uint)(count - 1));
            var returnsVoid = Native.LLVMGetTypeKind(Native.LLVMGetReturnType(stub.CalleeType)) == LLVMTypeKind.Void;
            b.PositionAtEnd(b.AppendBlock(stub.Fn, "entry"));

            var guards = GuardedImplementations(stub, implementers);
            if (guards.Count > 0)
            {
                var mt = b.Load(Llvm.Ptr, self, "mt");
                foreach (var (methodTable, callee) in guards)
                {
                    var hit = b.AppendBlock(stub.Fn, "direct");
                    var next = b.AppendBlock(stub.Fn, "next");
                    b.CondBr(b.ICmp(LLVMIntPredicate.EQ, mt, methodTable), hit, next);
                    b.PositionAtEnd(hit);
                    var direct = b.Call(stub.CalleeType, callee, args);
                    if (returnsVoid) b.RetVoid(); else b.Ret(direct);
                    b.PositionAtEnd(next);
                }
            }
            var slots = b.Call(lookup.Type, lookup.Fn, [self, stub.Interface, site]);
            var slot = b.InBoundsGep(Llvm.I8, slots, [Llvm.ConstInt(Llvm.I64, (long)((ulong)stub.Index * Types.PointerSize))], "islot");
            var fn = b.Load(Llvm.Ptr, slot, "ifn");
            var result = b.Call(stub.CalleeType, fn, args);
            if (returnsVoid) b.RetVoid(); else b.Ret(result);
        }
    }

    /// <summary>
    /// (method table, implementation) for each constructed class implementing the stub's interface, when
    /// there are at most MaxGuardedClasses and each slot holds a function of the call's type; else none.
    /// </summary>
    private List<(LLVMValueRef MethodTable, LLVMValueRef Callee)> GuardedImplementations(
        InterfaceCallStub stub, Dictionary<nint, List<(PendingMethodTable Table, int Entry)>> implementers)
    {
        if (!implementers.TryGetValue(stub.Interface.Handle, out var tables) || tables.Count > MaxGuardedClasses) return [];
        List<(LLVMValueRef, LLVMValueRef)> guards = [];
        foreach (var (table, entry) in tables)
        {
            var members = table.Interfaces[entry].Members;
            // A slot nothing requested holds csrt_pruned_virtual (FinishMethodTables): no call reaches it.
            if (stub.Index >= members.Count || !table.RequestedInterfaceSlots.Contains((entry, stub.Index))) return [];
            var callee = ImplementationFor(table.Layout, members[stub.Index]);
            if (callee.IsNull || Native.LLVMIsAFunction(callee).IsNull || Native.LLVMGlobalGetValueType(callee).Handle != stub.CalleeType.Handle) return [];
            guards.Add((table.Global, callee));
        }
        return guards;
    }
}
