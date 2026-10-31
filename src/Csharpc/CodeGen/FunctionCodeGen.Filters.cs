using Csharpc.Frontend.Il;
using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Exception filters run in the first pass (src/runtime/eh.c, "Exception filters"). .NET dispatches
/// in two passes: `when` filters all the way up to the handler run before any finally block between
/// the throw and that handler. So a filter can't be ordinary code in its try's landing pad, which
/// only runs once the frames below have been unwound:
///
///  * each filter is lowered into a function of its own, `i32 filter(csrt_EhFrame* frame, exception)`
///    (1: accept), that reaches the method's locals, parameters, `this` and closure environments
///    through the addresses its try's frame record carries;
///  * the record { prev, try descriptor, values* } is filled and linked in (csrt_eh_push) on every
///    edge into the try and unlinked (csrt_eh_pop) on every edge out of it, by branch, return, or
///    exception (the dispatch), and around each suspension of a coroutine;
///  * a `when` clause's type-info in the landing pads is its csrt_EhClause, which the personality
///    recognizes, matches to its record, and runs; the dispatch then enters the handler the first
///    pass chose (csrt_eh_selected) instead of evaluating the filter.
///
/// wasm32 has no unwinder: the runtime walks the records when the exception is raised, so every try
/// with catch clauses gets a record there (ExceptionLowering drops them all again in programs
/// without filters), and so do async bodies (barriers: they catch everything into the task).
/// Code without filters is unchanged on native targets.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>csrt_EhClause.magic (src/runtime/eh.c): tells a `when` clause from a method table.</summary>
    private const ulong EhClauseMagic = 0xC5A7F117E2C1A05EUL;

    /// <summary>A try statement whose try region has a frame record.</summary>
    private sealed class EhTry
    {
        public required object Key { get; init; }
        public required ControlFlowGraph? Graph { get; init; }
        /// <summary>csrt_EhTry: { count, clauses[] }.</summary>
        public required LLVMValueRef Descriptor { get; init; }
        /// <summary>Per handler (CFG region or IL clause), in order: its csrt_EhClause and filter function (null: plain catch).</summary>
        public required List<(object Handler, LLVMValueRef Clause, LoweredFunction? Filter)> Handlers { get; init; }
        /// <summary>The record: { prev, owner, values* }.</summary>
        public required LLVMValueRef Record { get; init; }
        /// <summary>What the filters read, by index into the values array: addresses (or this / env) in this function.</summary>
        public List<LLVMValueRef> Values { get; } = [];
        public SortedSet<int> UsedValues { get; } = [];
        /// <summary>Edges into the try: filled in once the filters are lowered (and the values known).</summary>
        public List<(LLVMBasicBlockRef Block, LLVMBasicBlockRef Dest)> Pushes { get; } = [];
        public bool Finished { get; set; }

        public LLVMValueRef ClauseOf(object handler) => Handlers.First(h => ReferenceEquals(h.Handler, handler)).Clause;
    }

    private readonly Dictionary<object, EhTry> _ehTrys = new(ReferenceEqualityComparer.Instance);
    private LLVMTypeRef EhFrameType => L.Struct(L.Ptr, L.Ptr, L.Ptr);
    private LLVMTypeRef EhClauseType => L.Struct(L.I64, L.Ptr, L.Ptr, L.Ptr);
    private LLVMTypeRef FilterFunctionType => L.FunctionType(L.I32, L.Ptr, L.Ptr);

    // ======================================================================================
    // Which trys have records (Roslyn CFG)
    // ======================================================================================

    private static bool HasFilter(ControlFlowRegion tryAndCatch) =>
        tryAndCatch.NestedRegions.Any(r => r.Kind == ControlFlowRegionKind.FilterAndHandler);

    /// <summary>
    /// A try region with a frame record: its statement has a `when` clause, or (wasm32, where the
    /// runtime's first pass walks the records) any catch clause.
    /// </summary>
    private bool NeedsEhFrame(ControlFlowRegion region) =>
        region is { Kind: ControlFlowRegionKind.Try, EnclosingRegion: { Kind: ControlFlowRegionKind.TryAndCatch } statement }
        && !ReferenceEquals(region, _cfg?.Graph.Root) && (T.Target.IsWasm || HasFilter(statement));

    private EhTry EhTryOf(ControlFlowRegion tryRegion)
    {
        if (_ehTrys.TryGetValue(tryRegion, out var known)) return known;
        var handlers = Handlers(tryRegion.EnclosingRegion!)
            .Select(h => ((object)h, h.Kind == ControlFlowRegionKind.FilterAndHandler, IsCatchAll(h.ExceptionType) ? LlvmContext.Null(L.Ptr) : CatchTypeInfo(h.ExceptionType!)))
            .ToList();
        return _ehTrys[tryRegion] = NewEhTry(tryRegion, _cfg!.Graph, handlers);
    }

    /// <summary>The descriptor, clause constants, filter function declarations and record of a try statement.</summary>
    private EhTry NewEhTry(object key, ControlFlowGraph? graph, List<(object Handler, bool HasFilter, LLVMValueRef Type)> handlers)
    {
        var id = _m.NextEhTryId();
        var owner = Native.TakeName(_fn.Fn);
        List<(object, LLVMValueRef, LoweredFunction?)> entries = [];
        var index = 0;
        foreach (var (handler, hasFilter, _) in handlers)
        {
            LoweredFunction? filter = null;
            if (hasFilter)
            {
                var fn = _m.Module.AddFunction($"{owner}$filter{id}.{index}", FilterFunctionType);
                Native.LLVMSetLinkage(fn, LLVMLinkage.Internal);
                L.AddFunctionAttribute(fn, "uwtable", 2);
                L.AddFunctionAttribute(fn, "cold"); // only the throw path calls it
                filter = new LoweredFunction(fn, FilterFunctionType);
            }
            var clause = _m.Module.AddGlobal($"csharpc.eh{(hasFilter ? "filter" : "clause")}.{id}.{index}", EhClauseType, constant: true, linkage: LLVMLinkage.Internal);
            Native.LLVMSetAlignment(clause, 8);
            entries.Add((handler, clause, filter));
            index++;
        }
        var clauses = entries.Select(e => e.Item2).ToArray();
        var init = L.ConstStruct(L.ConstInt(L.I64, clauses.Length), LlvmContext.ConstArray(L.Ptr, clauses));
        var descriptor = _m.Module.AddGlobal($"csharpc.ehtry.{id}", Native.LLVMTypeOf(init), init, constant: true, linkage: LLVMLinkage.Internal);
        Native.LLVMSetAlignment(descriptor, 8);
        for (var i = 0; i < entries.Count; i++)
            Native.LLVMSetInitializer(entries[i].Item2, L.ConstStruct(
                L.ConstUInt(L.I64, EhClauseMagic), handlers[i].Type,
                entries[i].Item3 is { } f ? f.Fn : LlvmContext.Null(L.Ptr), descriptor));
        var record = _allocas.Alloca(EhFrameType, "eh.frame");
        return new EhTry { Key = key, Graph = graph, Descriptor = descriptor, Handlers = entries, Record = record };
    }

    // ======================================================================================
    // Linking records in and out
    // ======================================================================================

    /// <summary>An edge into the try: a block that links its record in, filled by FinishEhTry.</summary>
    private LLVMBasicBlockRef EhPushTo(EhTry eh, LLVMBasicBlockRef dest)
    {
        var block = NewBlock("eh.enter");
        eh.Pushes.Add((block, dest));
        return block;
    }

    private void EhPop(EhTry eh) => CallRuntime("csrt_eh_pop", L.Void, [L.Ptr], eh.Record);

    /// <summary>Pushes on a branch that enters trys with records (outermost first).</summary>
    private LLVMBasicBlockRef EnterEhFrames(ControlFlowBranch branch, LLVMBasicBlockRef dest)
    {
        if (branch.EnteringRegions.IsEmpty) return dest;
        var target = dest;
        for (var i = branch.EnteringRegions.Length - 1; i >= 0; i--)
            if (NeedsEhFrame(branch.EnteringRegions[i])) target = EhPushTo(EhTryOf(branch.EnteringRegions[i]), target);
        return target;
    }

    /// <summary>Does the branch leave a try with a record?</summary>
    private bool LeavesEhFrames(ControlFlowBranch branch) => branch.LeavingRegions.Any(NeedsEhFrame);

    /// <summary>
    /// What a branch out of try regions runs on the way, innermost first: finally blocks, and the
    /// unlinking of records (in the same order, so an inner finally still runs inside an outer try).
    /// </summary>
    private List<object> ExitSteps(ControlFlowBranch branch)
    {
        List<object> steps = [];
        var finallys = branch.FinallyRegions;
        var next = 0;
        foreach (var region in branch.LeavingRegions)
        {
            if (region is { Kind: ControlFlowRegionKind.Try, EnclosingRegion: { Kind: ControlFlowRegionKind.TryAndFinally } statement }
                && next < finallys.Length && ReferenceEquals(finallys[next], statement.NestedRegions[^1]))
                steps.Add(finallys[next++]);
            else if (NeedsEhFrame(region)) steps.Add(EhTryOf(region));
        }
        for (; next < finallys.Length; next++) steps.Add(finallys[next]);
        return steps;
    }

    private LLVMBasicBlockRef EhPopTo(EhTry eh, LLVMBasicBlockRef dest)
    {
        var current = _b.InsertBlock;
        var block = NewBlock("eh.leave");
        _b.PositionAtEnd(block);
        EhPop(eh);
        _b.Br(dest);
        _b.PositionAtEnd(current);
        return block;
    }

    /// <summary>Fills the edges into the try: { prev, owner, values } and csrt_eh_push.</summary>
    private void FinishEhTry(EhTry eh)
    {
        if (eh.Finished) return;
        eh.Finished = true;
        var saved = _b.InsertBlock;
        var (savedUnwind, savedHook) = (_b.UnwindDest, _b.UnwindHook);
        (_b.UnwindDest, _b.UnwindHook) = (default, null);
        var values = eh.UsedValues.Count == 0 ? default : _allocas.Alloca(LlvmContext.Array(L.Ptr, (ulong)(eh.UsedValues.Max + 1)), "eh.values");
        void Fill(IrBuilder b)
        {
            b.Store(eh.Descriptor, b.StructGep(EhFrameType, eh.Record, 1));
            if (values.IsNull) return;
            b.Store(values, b.StructGep(EhFrameType, eh.Record, 2));
            foreach (var i in eh.UsedValues)
                b.Store(eh.Values[i], b.InBoundsGep(L.Ptr, values, [L.ConstInt(L.I32, i)]));
        }
        // The record's contents never change: fill it once on entry, so a try in a loop only links it
        // in and out. A coroutine's frame doesn't exist yet there (its allocas move into the frame at
        // llvm.coro.begin), so it fills the record on each entry instead.
        var once = _coro is null && eh.Pushes.Count > 0;
        if (once) Fill(_allocas);
        foreach (var (block, dest) in eh.Pushes)
        {
            _b.PositionAtEnd(block);
            if (!once) Fill(_b);
            // IL may branch to a try's first instruction from inside the try: csrt_eh_enter links only once.
            CallRuntime(eh.Key is IlTry ? "csrt_eh_enter" : "csrt_eh_push", L.Void, [L.Ptr], eh.Record);
            _b.Br(dest);
        }
        (_b.UnwindDest, _b.UnwindHook) = (savedUnwind, savedHook);
        if (!saved.IsNull) _b.PositionAtEnd(saved);
    }

    /// <summary>After a graph is lowered: its filters, then the records' edges.</summary>
    private void FinishEhTrys(ControlFlowGraph graph, CfgState state)
    {
        foreach (var eh in _ehTrys.Values.Where(e => ReferenceEquals(e.Graph, graph) && !e.Finished).ToList())
        {
            foreach (var (handler, _, filter) in eh.Handlers)
                if (filter is { } f)
                    new FunctionCodeGen(_m, _method, f, _nested, _subst, _initializingType, filterParent: this, filterTry: eh)
                        .EmitFilter(graph, (ControlFlowRegion)handler, state);
            FinishEhTry(eh);
        }
    }

    // ======================================================================================
    // Suspension (await): the records of the trys around it leave the stack with the frame
    // ======================================================================================

    /// <summary>The records linked in at the current point of this function, outermost first.</summary>
    private List<LLVMValueRef> ActiveEhRecords()
    {
        List<LLVMValueRef> records = [];
        if (_il is { CurrentBlock: { } ilBlock })
        {
            foreach (var t in _il.Trys.Where(t => t.Contains(ilBlock.Start) && IlNeedsEhFrame(t)).OrderByDescending(t => t.End - t.Start))
                records.Add(IlEhTryOf(t).Record);
        }
        else if (_currentBlock is not null && _cfg is not null)
        {
            for (var r = _currentBlock.EnclosingRegion; r is not null && !ReferenceEquals(r, _cfg.Graph.Root); r = r.EnclosingRegion)
                if (NeedsEhFrame(r)) records.Add(EhTryOf(r).Record);
            records.Reverse();
        }
        if (_coroBarrierActive) records.Insert(0, _coroBarrier);
        return records;
    }

    private List<LLVMValueRef>? _suspendedEhRecords;

    /// <summary>Before a suspension point hands the frame to whoever resumes it: unlink this frame's records.</summary>
    private void UnlinkEhRecordsForSuspend()
    {
        if (_suspendedEhRecords is not null) return;
        _suspendedEhRecords = ActiveEhRecords();
        if (_suspendedEhRecords.Count > 0) CallRuntime("csrt_eh_pop", L.Void, [L.Ptr], _suspendedEhRecords[0]);
    }

    /// <summary>Resumed (maybe on another thread): link them in again, outermost first.</summary>
    private void RelinkEhRecordsAfterResume()
    {
        foreach (var record in _suspendedEhRecords ?? []) CallRuntime("csrt_eh_push", L.Void, [L.Ptr], record);
        _suspendedEhRecords = null;
    }

    /// <summary>wasm32: an async body's barrier record (it catches everything into its task).</summary>
    private LLVMValueRef _coroBarrier;
    private bool _coroBarrierActive;

    private void PushCoroutineBarrier()
    {
        if (!T.Target.IsWasm) return;
        _coroBarrier = _allocas.Alloca(EhFrameType, "eh.barrier");
        _b.Store(_m.ExternalGlobal("csrt_eh_barrier"), _b.StructGep(EhFrameType, _coroBarrier, 1));
        CallRuntime("csrt_eh_push", L.Void, [L.Ptr], _coroBarrier);
        _coroBarrierActive = true;
    }

    /// <summary>At the start of the completion / fault blocks: the body is done.</summary>
    private void PopCoroutineBarrier(LLVMBasicBlockRef block)
    {
        if (_coroBarrier.IsNull) return;
        var saved = _b.InsertBlock;
        _b.PositionAtStart(block);
        CallRuntime("csrt_eh_pop", L.Void, [L.Ptr], _coroBarrier);
        if (!saved.IsNull) _b.PositionAtEnd(saved);
    }

    // ======================================================================================
    // The filter function (child FunctionCodeGen)
    // ======================================================================================

    private readonly FunctionCodeGen? _filterParent;
    private readonly EhTry? _filterTry;
    private ControlFlowRegion? _filterRegion;
    private LLVMValueRef _filterValues;
    private readonly Dictionary<nint, (LLVMValueRef Load, int Index)> _parentValues = [];
    private LLVMBasicBlockRef _filterPad, _filterAccept, _filterReject;

    /// <summary>A value of the parent function (an address there): loaded from the record's values.</summary>
    private LLVMValueRef ParentValue(LLVMValueRef value)
    {
        if (_parentValues.TryGetValue(value.Handle, out var known)) return known.Load;
        var eh = _filterTry!;
        var index = eh.Values.FindIndex(v => v.Handle == value.Handle);
        if (index < 0) { index = eh.Values.Count; eh.Values.Add(value); }
        if (_filterValues.IsNull)
            _filterValues = _allocas.Load(L.Ptr, _allocas.StructGep(EhFrameType, _fn.Fn.Param(0), 2), "eh.values");
        var load = _allocas.Load(L.Ptr, _allocas.InBoundsGep(L.Ptr, _filterValues, [L.ConstInt(L.I32, index)]), "parent");
        _parentValues[value.Handle] = (load, index);
        return load;
    }

    /// <summary>The parent's storage of a local the filter reads or writes (never a captured one: those live in environments).</summary>
    private LLVMValueRef? FilterLocal(ILocalSymbol local) =>
        _filterParent is { } parent ? (LLVMValueRef?)(_slots[local] = ParentValue(parent.LocalStorage(local))) : null;

    private LLVMValueRef? FilterParameter(IParameterSymbol parameter) =>
        _filterParent is { } parent && parent._slots.TryGetValue(parameter, out var slot) ? (LLVMValueRef?)(_slots[parameter] = ParentValue(slot)) : null;

    /// <summary>Lowers the filter of <paramref name="handler"/> (a FilterAndHandler region) as this function's body.</summary>
    private void EmitFilter(ControlFlowGraph graph, ControlFlowRegion handler, CfgState parentState)
    {
        var parent = _filterParent!;
        var filterRegion = _filterRegion = handler.NestedRegions.First(r => r.Kind == ControlFlowRegionKind.Filter);
        var state = _cfg = new CfgState(graph, continuation: null) { Closure = parentState.Closure };
        state.LvalueCaptures.UnionWith(parentState.LvalueCaptures);
        foreach (var (id, type) in parentState.ArrayEnumerators) state.ArrayEnumerators[id] = type;
        if (parent._this.Handle != 0) _this = ParentValue(parent._this);
        if (parent._incomingEnv.Handle != 0) _incomingEnv = ParentValue(parent._incomingEnv);
        EnsurePersonality();

        foreach (var block in graph.Blocks)
            if (block is { Kind: BasicBlockKind.Block, IsReachable: true }
                && block.Ordinal >= filterRegion.FirstBlockOrdinal && block.Ordinal <= filterRegion.LastBlockOrdinal)
                state.Blocks[block] = _b.AppendBlock(_fn.Fn, $"bb{block.Ordinal}");

        // The exception (`catch (E e)` assigns it from here), and the environments of the catch's
        // and the filter's captured variables, which the handler then uses.
        _b.Store(_fn.Fn.Param(1), ExceptionSlot(handler));
        foreach (var region in new[] { handler, filterRegion })
            if (CurrentClosure.Analysis.ScopeOf(CurrentClosure, region) is { } scope) AllocateEnv(scope);
        var first = graph.Blocks[filterRegion.FirstBlockOrdinal];
        if (!state.Blocks.TryGetValue(first, out var firstBlock)) { _b.Ret(L.ConstInt(L.I32, 0)); FinishFilter(); return; }
        _b.Br(firstBlock);

        foreach (var (block, llvmBlock) in state.Blocks)
        {
            BeginBlock(block);
            _b.PositionAtEnd(llvmBlock);
            _currentBlock = block;
            foreach (var operation in block.Operations) WithLocation(operation, () => EmitStatement(operation));
            WithLocation(block.BranchValue ?? block.Operations.LastOrDefault(), () => EmitTerminator(block));
        }
        EndCfg();
        FinishFilter();
    }

    private void FinishFilter()
    {
        // Values the lowering asked for but no code reads (e.g. `this`) are not stored by the parent.
        foreach (var (load, index) in _parentValues.Values)
            if (Native.LLVMGetFirstUse(load) == 0) Native.LLVMInstructionEraseFromParent(load);
            else _filterTry!.UsedValues.Add(index);
        Finish();
    }

    /// <summary>In a filter function: the branches that leave the filter return its verdict.</summary>
    private LLVMBasicBlockRef? FilterExit(ControlFlowBranch branch)
    {
        if (_filterRegion is not { } region) return null;
        var rejects = branch is { Destination: null, Semantics: ControlFlowBranchSemantics.StructuredExceptionHandling };
        if (!rejects && (branch.Destination is null || (branch.Destination.Ordinal >= region.FirstBlockOrdinal && branch.Destination.Ordinal <= region.LastBlockOrdinal)))
            return null;
        ref var block = ref rejects ? ref _filterReject : ref _filterAccept;
        if (block.IsNull)
        {
            var current = _b.InsertBlock;
            block = NewBlock(rejects ? "filter.reject" : "filter.accept");
            _b.PositionAtEnd(block);
            _b.Ret(L.ConstInt(L.I32, rejects ? 0 : 1));
            _b.PositionAtEnd(current);
        }
        return block;
    }

    /// <summary>An exception the filter throws is its own: it declines the handler (ECMA-335 II.19).</summary>
    private LLVMBasicBlockRef FilterCatchAllPad()
    {
        if (!_filterPad.IsNull) return _filterPad;
        var (savedBlock, savedUnwind, savedHook) = (_b.InsertBlock, _b.UnwindDest, _b.UnwindHook);
        (_b.UnwindDest, _b.UnwindHook) = (default, null);
        _filterPad = NewBlock("filter.lpad");
        _b.PositionAtEnd(_filterPad);
        var lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, [LlvmContext.Null(L.Ptr)], cleanup: false);
        CallRuntime("csrt_begin_catch", L.Ptr, [L.Ptr], _b.ExtractValue(lp, 0));
        _b.Ret(L.ConstInt(L.I32, 0));
        _b.PositionAtEnd(savedBlock);
        (_b.UnwindDest, _b.UnwindHook) = (savedUnwind, savedHook);
        return _filterPad;
    }

    /// <summary>Allocates the environment of a handler region's captured variables (entering the catch).</summary>
    private void EnterHandlerScope(ControlFlowRegion region)
    {
        if (CurrentClosure.Analysis.ScopeOf(CurrentClosure, region) is { } scope) AllocateEnv(scope);
    }

    // ======================================================================================
    // IL front-end: the same over IL exception regions
    // ======================================================================================

    private bool IlNeedsEhFrame(IlTry t) =>
        t.Clauses.Any(c => c.Kind == System.Reflection.Metadata.ExceptionRegionKind.Filter)
        || (T.Target.IsWasm && t.Clauses.Any(c => c.Kind == System.Reflection.Metadata.ExceptionRegionKind.Catch));

    private EhTry IlEhTryOf(IlTry t)
    {
        if (_ehTrys.TryGetValue(t, out var known)) return known;
        var handlers = t.Clauses
            .Where(c => c.Kind is System.Reflection.Metadata.ExceptionRegionKind.Catch or System.Reflection.Metadata.ExceptionRegionKind.Filter)
            .Select(c => ((object)c, c.Kind == System.Reflection.Metadata.ExceptionRegionKind.Filter,
                // A filter gets any object: its own code tests the type.
                c.Kind == System.Reflection.Metadata.ExceptionRegionKind.Filter || IsCatchAllType(S(c.CatchType!)) ? LlvmContext.Null(L.Ptr) : IlCatchTypeInfo(c.CatchType!)))
            .ToList();
        return _ehTrys[t] = NewEhTry(t, graph: null, handlers);
    }

    /// <summary>A block of a filter's code (its handler's code starts at HandlerStart).</summary>
    private static bool IlInFilter(IlBlock block) => block.Handler is { } clause && clause.InFilter(block.Start);

    /// <summary>After the body is lowered: the filters, then the records' edges.</summary>
    private void IlFinishEhTrys(IlMethodBody body)
    {
        foreach (var eh in _ehTrys.Values.Where(e => e.Key is IlTry && !e.Finished).ToList())
        {
            foreach (var (handler, _, filter) in eh.Handlers)
                if (filter is { } f)
                    new FunctionCodeGen(_m, _method, f, _nested, _subst, _initializingType, filterParent: this, filterTry: eh)
                        .EmitIlFilter(body, _il!.Clauses.IndexOf((IlClause)handler));
            FinishEhTry(eh);
        }
    }

    /// <summary>Lowers the code of the filter of clause <paramref name="clauseIndex"/> as this function's body.</summary>
    private void EmitIlFilter(IlMethodBody body, int clauseIndex)
    {
        var parent = _filterParent!;
        var (syntax, baseSyntax) = _m.IlSyntax(body.Assembly);
        var il = _il = new IlState(body, syntax, baseSyntax);
        try
        {
            BuildIlBlocks(il);
            BuildIlRegions(il);
        }
        catch (IlException e) { throw IlError(e.Message); }
        il.Locals = new LLVMValueRef[body.Locals.Length]; // the parent's, on first use (IlLocalSlot)
        il.LocalErrors = parent._il!.LocalErrors;
        if (parent._this.Handle != 0) _this = ParentValue(parent._this);
        EnsurePersonality();

        var clause = il.Clauses[clauseIndex];
        var blocks = il.Blocks.Where(b => b.Start >= clause.Region.FilterOffset && b.Start < clause.HandlerStart).ToList();
        foreach (var block in blocks) block.Llvm = NewBlock($"IL_{block.Start:X4}");
        var first = il.BlockAt[clause.Region.FilterOffset];
        _b.Store(_fn.Fn.Param(1), first.Entry![0].Slot); // the exception, on the filter's stack
        _b.Br(first.Llvm);
        var reachable = IlReachableBlocks(il);
        foreach (var block in blocks)
        {
            if (reachable.Contains(block.Start)) EmitIlBlock(block);
            else
            {
                block.Emitted = true;
                _b.PositionAtEnd(block.Llvm);
                _b.Unreachable();
            }
        }
        (_b.UnwindDest, _b.UnwindHook) = (default, null);
        FinishFilter();
    }
}
