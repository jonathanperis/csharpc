using Csharpc.Llvm;
using Microsoft.CodeAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Guarded devirtualization of interface calls (closed world). An interface call goes through a stub per
/// interface slot, `ret stub(args..., site cache)`, inlined at every call site, whose body is written once
/// the program's classes are all known (FinishMethodTables): for each constructed class whose interface
/// map holds the interface (at most <see cref="MaxGuardedClasses"/> of them, sharing at most
/// <see cref="MaxGuardedCallees"/> implementations) it compares the receiver's method table and calls
/// that class's implementation directly — the same function its slot table holds,
/// so the result is the same whichever path runs — and otherwise dispatches through the call site's cache
/// as before. Most of Kestrel's request path calls interfaces with a single implementation in the program
/// (IHttpApplication, ITimeoutControl, the feature interfaces HttpProtocol implements): a direct call is
/// cheaper than the cached lookup and indirect call, and LLVM can inline it. A receiver of any other type
/// (another class, a boxed struct, an array, a string, a variant instantiation) takes the cached path,
/// so the guards never need the full set of implementing types.
/// </summary>
public sealed partial class ModuleCodeGen
{
    /// <summary>
    /// Compares per call before the cached lookup: an interface with more implementing classes than this,
    /// or more distinct implementations than <see cref="MaxGuardedCallees"/>, dispatches through the cache.
    /// A compare is a cmp/jne on the method table already loaded; each implementation is a direct call LLVM
    /// may inline into the caller, which is what bounds the second number (the callers' size, and so
    /// whether they are inlined in turn).
    /// </summary>
    private const int MaxGuardedClasses = 8, MaxGuardedCallees = 4;

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
                // One direct call per implementation, reached from the compare of every class sharing it
                // (Http1Connection, Http2Stream and Http3Stream all inherit HttpProtocol's IFeatureCollection).
                var mt = b.Load(Llvm.Ptr, self, "mt");
                var calls = new Dictionary<nint, LLVMBasicBlockRef>();
                foreach (var (methodTable, callee) in guards)
                {
                    if (!calls.TryGetValue(callee.Handle, out var hit))
                    {
                        calls[callee.Handle] = hit = b.AppendBlock(stub.Fn, "direct");
                        using var hb = new IrBuilder(Llvm);
                        hb.PositionAtEnd(hit);
                        var direct = hb.Call(stub.CalleeType, callee, args);
                        if (returnsVoid) hb.RetVoid(); else hb.Ret(direct);
                    }
                    var next = b.AppendBlock(stub.Fn, "next");
                    b.CondBr(b.ICmp(LLVMIntPredicate.EQ, mt, methodTable), hit, next);
                    b.PositionAtEnd(next);
                }
            }
            if (guards.Count == 0)
            {
                var result = CachedDispatch(b, stub, lookup, args, site);
                if (returnsVoid) b.RetVoid(); else b.Ret(result);
                continue;
            }
            // With guards, the cached dispatch is out of line (and cold): inlined at every call site it made
            // the callers too large for LLVM to inline them in turn (Dictionary<string, int>'s Hash and
            // KeysEqual stopped being inlined into wordfreq's loop: +24 % executed instructions).
            var slow = Module.AddFunction($"csrt.istub.slow.{_interfaceSlowPaths++}", stub.Type);
            Native.LLVMSetLinkage(slow, LLVMLinkage.Internal);
            Llvm.AddFunctionAttribute(slow, "noinline");
            Llvm.AddFunctionAttribute(slow, "cold");
            Llvm.AddFunctionAttribute(slow, "uwtable", 2);
            using (var sb = new IrBuilder(Llvm))
            {
                sb.PositionAtEnd(sb.AppendBlock(slow, "entry"));
                var slowArgs = Enumerable.Range(0, count - 1).Select(i => slow.Param((uint)i)).ToArray();
                var result = CachedDispatch(sb, stub, lookup, slowArgs, slow.Param((uint)(count - 1)));
                if (returnsVoid) sb.RetVoid(); else sb.Ret(result);
            }
            var viaSlow = b.Call(stub.Type, slow, [.. args, site]);
            if (returnsVoid) b.RetVoid(); else b.Ret(viaSlow);
        }
    }

    private int _interfaceSlowPaths;

    /// <summary>A runtime built with -DCSRT_ALLOC_PROFILE (Toolchain's $CSHARPC_RUNTIME_CFLAGS) counts dispatches.</summary>
    private static readonly bool s_profilingRuntime =
        Environment.GetEnvironmentVariable("CSHARPC_RUNTIME_CFLAGS")?.Contains("CSRT_ALLOC_PROFILE", StringComparison.Ordinal) == true;

    /// <summary>
    /// The call through the receiver's interface map, found with the call site's cache. The cache's hit
    /// (csrt_interface_slots_cached's first test: word = method table | map index &lt;&lt; 48) is inline, so a
    /// monomorphic site makes no runtime call; a miss, or an empty word (0 matches no method table), calls
    /// the runtime, which scans the map and refills the word. Profiling runtimes count every dispatch
    /// there, so their builds always call it.
    /// </summary>
    private LLVMValueRef CachedDispatch(IrBuilder b, InterfaceCallStub stub, LoweredFunction lookup, LLVMValueRef[] args, LLVMValueRef site)
    {
        LLVMValueRef slots;
        if (s_profilingRuntime) slots = b.Call(lookup.Type, lookup.Fn, [args[0], stub.Interface, site]);
        else
        {
            var function = Native.LLVMGetBasicBlockParent(b.InsertBlock);
            var word = b.Load(Llvm.I64, site, "icache");
            Native.LLVMSetOrdering(word, LLVMAtomicOrdering.Monotonic);
            Native.LLVMSetAlignment(word, 8);
            var mt = b.Load(Llvm.Ptr, args[0], "mt");
            var mtBits = b.Cast(LLVMOpcode.PtrToInt, mt, Llvm.I64);
            var cached = b.BinOp(LLVMOpcode.And, word, Llvm.ConstInt(Llvm.I64, (1L << 48) - 1));
            var hit = b.AppendBlock(function, "icache.hit");
            var miss = b.AppendBlock(function, "icache.miss");
            var done = b.AppendBlock(function, "icache.done");
            b.CondBr(b.ICmp(LLVMIntPredicate.EQ, cached, mtBits), hit, miss);
            b.PositionAtEnd(hit);
            var map = b.Load(Llvm.Ptr, b.StructGep(Types.MethodTableHeader, mt, 4), "imap");
            var index = b.BinOp(LLVMOpcode.LShr, word, Llvm.ConstInt(Llvm.I64, 48));
            var entry = b.InBoundsGep(Llvm.Struct(Llvm.Ptr, Llvm.Ptr), map, [index], "ientry");
            var fast = b.Load(Llvm.Ptr, b.StructGep(Llvm.Struct(Llvm.Ptr, Llvm.Ptr), entry, 1), "islots");
            b.Br(done);
            b.PositionAtEnd(miss);
            var slow = b.Call(lookup.Type, lookup.Fn, [args[0], stub.Interface, site]);
            b.Br(done);
            b.PositionAtEnd(done);
            slots = b.Phi(Llvm.Ptr, "slots");
            IrBuilder.AddIncoming(slots, fast, hit);
            IrBuilder.AddIncoming(slots, slow, miss);
        }
        var slot = b.InBoundsGep(Llvm.I8, slots, [Llvm.ConstInt(Llvm.I64, (long)((ulong)stub.Index * Types.PointerSize))], "islot");
        var fn = b.Load(Llvm.Ptr, slot, "ifn");
        return b.Call(stub.CalleeType, fn, args);
    }

    /// <summary>
    /// (method table, implementation) for each constructed class implementing the stub's interface, when
    /// there are at most MaxGuardedClasses (with at most MaxGuardedCallees distinct implementations) and
    /// each slot holds a function of the call's type; else none.
    /// </summary>
    private List<(LLVMValueRef MethodTable, LLVMValueRef Callee)> GuardedImplementations(
        InterfaceCallStub stub, Dictionary<nint, List<(PendingMethodTable Table, int Entry)>> implementers)
    {
        if (!implementers.TryGetValue(stub.Interface.Handle, out var tables) || tables.Count > MaxGuardedClasses) return [];
        List<(LLVMValueRef, LLVMValueRef)> guards = [];
        var callees = new HashSet<nint>();
        foreach (var (table, entry) in tables)
        {
            var members = table.Interfaces[entry].Members;
            // A slot nothing requested holds csrt_pruned_virtual (FinishMethodTables): no call reaches it.
            if (stub.Index >= members.Count || !table.RequestedInterfaceSlots.Contains((entry, stub.Index))) return [];
            var callee = ImplementationFor(table.Layout, members[stub.Index]);
            if (callee.IsNull || Native.LLVMIsAFunction(callee).IsNull || Native.LLVMGlobalGetValueType(callee).Handle != stub.CalleeType.Handle) return [];
            guards.Add((table.Global, callee));
            if (callees.Add(callee.Handle) && callees.Count > MaxGuardedCallees) return [];
        }
        return guards;
    }
}
