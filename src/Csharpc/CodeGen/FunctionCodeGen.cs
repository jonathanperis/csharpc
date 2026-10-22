using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Lowers one method from Roslyn's <see cref="ControlFlowGraph"/> to LLVM IR.
///
/// Roslyn's CFG plays the role rustc's MIR plays: structured control flow (loops, &amp;&amp;/||, ?:, ??,
/// ?., switch, patterns) is already lowered to basic blocks with conditional branches, and
/// intermediate values that must survive across blocks are "flow captures". So the job here is
/// mostly 1:1 — every CFG block becomes an LLVM block — plus expression lowering.
///
/// Like clang, every local/parameter/capture gets an entry-block alloca; LLVM's mem2reg/SROA
/// turn them into SSA registers, so we never have to build phi nodes ourselves.
/// </summary>
public sealed partial class FunctionCodeGen
{
    private readonly ModuleCodeGen _m;
    private readonly IMethodSymbol? _method;
    private readonly LoweredFunction _fn;
    private readonly IrBuilder _b;
    private readonly IrBuilder _allocas;
    private readonly LLVMBasicBlockRef _entry;
    private readonly LLVMBasicBlockRef _start;
    private readonly LLVMValueRef _this;
    /// <summary>Set when this is a lambda / local function: its closure-tree node and incoming env.</summary>
    private readonly ClosureFunction? _nested;
    private readonly LLVMValueRef _incomingEnv;
    private readonly Dictionary<ISymbol, LLVMValueRef> _slots = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IParameterSymbol> _byRefParams = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ILocalSymbol> _ownLocals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, LLVMBasicBlockRef> _throwBlocks = [];
    private CfgState? _cfg;

    private sealed class CfgState(ControlFlowGraph graph, LLVMBasicBlockRef? continuation)
    {
        public ControlFlowGraph Graph { get; } = graph;
        public LLVMBasicBlockRef? Continuation { get; } = continuation;
        public Dictionary<BasicBlock, LLVMBasicBlockRef> Blocks { get; } = [];
        public Dictionary<CaptureId, (LLVMValueRef Slot, ITypeSymbol? Type)> Captures { get; } = [];
        /// <summary>Captures used as assignment targets: they hold an address, not a value.</summary>
        public HashSet<CaptureId> LvalueCaptures { get; } = [];
        /// <summary>Captures holding a foreach-over-array enumerator -> element type.</summary>
        public Dictionary<CaptureId, ITypeSymbol> ArrayEnumerators { get; } = [];
        /// <summary>Per finally region: the continuation selector and where each value resumes.</summary>
        public Dictionary<ControlFlowRegion, (LLVMValueRef Selector, List<LLVMBasicBlockRef> Continuations)> Finally { get; } =
            new(ReferenceEqualityComparer.Instance);
        public List<(LLVMBasicBlockRef Block, ControlFlowRegion Region)> FinallyEnds { get; } = [];
        public LLVMBasicBlockRef Exit { get; set; }
        /// <summary>The closure-tree node for this graph (captured variables live in env scopes).</summary>
        public ClosureFunction Closure { get; set; } = null!;
    }

    private LlvmContext L => _m.Llvm;
    private TypeMapper T => _m.Types;
    internal ModuleCodeGen Module => _m;
    internal IrBuilder Builder => _b;

    public FunctionCodeGen(ModuleCodeGen module, IMethodSymbol? method, LoweredFunction fn, ClosureFunction? nested = null)
    {
        _m = module;
        _nested = nested;
        _method = method;
        _fn = fn;
        _b = new IrBuilder(module.Llvm);
        _allocas = new IrBuilder(module.Llvm);
        _entry = _b.AppendBlock(fn.Fn, "entry");
        _allocas.PositionAtEnd(_entry);

        // Spill parameters to allocas (mem2reg will undo this) so they're assignable like locals.
        uint index = 0;
        if (method is not null && ModuleCodeGen.IsNestedFunction(method))
        {
            _incomingEnv = fn.Fn.Param(index++);
            _incomingEnv.SetName("env");
        }
        else if (method is not null && ModuleCodeGen.HasThis(method))
        {
            _this = fn.Fn.Param(index++);
            _this.SetName("this");
        }
        foreach (var p in method?.Parameters ?? [])
        {
            var value = fn.Fn.Param(index++);
            value.SetName(p.Name);
            if (p.RefKind != RefKind.None)
            {
                _slots[p] = value; // already an address
                _byRefParams.Add(p);
                continue;
            }
            var slot = _allocas.Alloca(T.Storage(p.Type), p.Name + ".addr");
            if (p.Type.SpecialType == SpecialType.System_Boolean) value = _allocas.Cast(LLVMOpcode.ZExt, value, L.I8);
            _allocas.Store(value, slot);
            _slots[p] = slot;
        }

        _start = _b.AppendBlock(fn.Fn, "start");
        _b.PositionAtEnd(_start);
    }

    private ITypeSymbol? ReturnType =>
        _method is null || _method.MethodKind == MethodKind.Constructor ? null : _method.ReturnType;

    // ======================================================================================
    // Entry points
    // ======================================================================================

    /// <summary>Lowers <paramref name="prologue"/> graphs (field initializers) in sequence, then the body.</summary>
    public void Emit(IReadOnlyList<ControlFlowGraph> prologue, ControlFlowGraph body)
    {
        LowerPrologue(prologue);
        LowerCfg(body, continuation: null);
        Finish();
    }

    public void EmitImplicitConstructor(IReadOnlyList<ControlFlowGraph> prologue)
    {
        LowerPrologue(prologue);
        if (_method!.ContainingType.BaseType is { } baseType && _m.IsUserType(baseType))
        {
            var baseCtor = baseType.InstanceConstructors.FirstOrDefault(c => c.Parameters.IsEmpty)
                ?? throw CompileError.Unsupported("base class without a parameterless constructor", _method);
            var f = _m.GetFunction(baseCtor);
            _b.Call(f.Type, f.Fn, [_this]);
        }
        _b.RetVoid();
        Finish();
    }

    public void EmitPrimaryConstructor(IReadOnlyList<ControlFlowGraph> initializers, ControlFlowGraph? baseCall)
    {
        // Parameters used outside initializers live in compiler-synthesized "<name>P" fields.
        foreach (var p in _method!.Parameters)
            if (CapturedParameterField(p) is { } field)
                StoreValue(ToStorage(LoadValue(_slots[p], p.Type), p.Type), FieldAddressOn(_this, field));
        if (baseCall is null)
        {
            EmitImplicitConstructor(initializers);
            return;
        }
        LowerPrologue(initializers);
        LowerCfg(baseCall, continuation: null);
        Finish();
    }

    private static IFieldSymbol? CapturedParameterField(IParameterSymbol p) =>
        p.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor
            ? ctor.ContainingType.GetMembers($"<{p.Name}>P").OfType<IFieldSymbol>().FirstOrDefault()
            : null;

    public void EmitAutoPropertyAccessor()
    {
        var property = (IPropertySymbol)_method!.AssociatedSymbol!;
        var field = BackingField(property);
        var address = field.IsStatic ? _m.StaticField(field) : FieldAddressOn(_this, field);
        if (_method.MethodKind == MethodKind.PropertyGet)
            _b.Ret(LoadValue(address, field.Type));
        else
        {
            StoreValue(ToStorage(_fn.Fn.Param(field.IsStatic ? 0u : 1u), field.Type), address);
            _b.RetVoid();
        }
        Finish();
    }

    public void EmitStaticInit(IReadOnlyList<ControlFlowGraph> initializers, IReadOnlyList<IMethodSymbol> staticConstructors)
    {
        LowerPrologue(initializers);
        foreach (var cctor in staticConstructors)
        {
            var f = _m.GetFunction(cctor);
            _b.Call(f.Type, f.Fn, []);
        }
        _b.RetVoid();
        Finish();
    }

    private void LowerPrologue(IReadOnlyList<ControlFlowGraph> graphs)
    {
        foreach (var graph in graphs)
        {
            var next = _b.AppendBlock(_fn.Fn, "init.done");
            LowerCfg(graph, next);
            _b.PositionAtEnd(next);
        }
    }

    private void Finish()
    {
        // The entry block holds only allocas and parameter spills; jump to the real code.
        _allocas.PositionAtEnd(_entry);
        _allocas.Br(_start);
        _b.Dispose();
        _allocas.Dispose();
    }

    // ======================================================================================
    // CFG lowering
    // ======================================================================================

    private void LowerCfg(ControlFlowGraph graph, LLVMBasicBlockRef? continuation)
    {
        var outer = _cfg;
        var state = _cfg = new CfgState(graph, continuation);
        state.Closure = _nested is { } n && ReferenceEquals(n.Cfg, graph)
            ? n
            : new ClosureAnalysis(graph, _method, _m).Root;
        _m.EnqueueNested(state.Closure);
        ScanRegions(graph, graph.Root);
        FindLvalueCaptures(graph, state.LvalueCaptures);
        FindArrayEnumerators(graph, state.ArrayEnumerators);

        foreach (var block in graph.Blocks)
            if (block is { Kind: BasicBlockKind.Block, IsReachable: true })
                state.Blocks[block] = _b.AppendBlock(_fn.Fn, $"bb{block.Ordinal}");

        EnterFunctionScope();
        _b.Br(Target(graph.Blocks[0].FallThroughSuccessor!));

        foreach (var (block, llvmBlock) in state.Blocks)
        {
            BeginBlock(block); // selects the landing pad for calls in this block (if inside a try)
            _b.PositionAtEnd(llvmBlock);
            foreach (var operation in block.Operations) EmitStatement(operation);
            EmitTerminator(block);
        }

        EndCfg();

        // End of each finally: dispatch to whichever continuation entered it.
        foreach (var (block, region) in state.FinallyEnds)
        {
            _b.PositionAtEnd(block);
            if (!state.Finally.TryGetValue(region, out var info) || info.Continuations.Count == 0) { _b.Unreachable(); continue; }
            if (info.Continuations.Count == 1) { _b.Br(info.Continuations[0]); continue; }
            var selector = _b.Load(L.I32, info.Selector, "finally.next");
            var cases = info.Continuations.Skip(1).Select((c, i) => (L.ConstInt(L.I32, i + 1), c)).ToList();
            _b.Switch(selector, info.Continuations[0], cases);
        }
        _cfg = outer;
    }

    private void ScanRegions(ControlFlowGraph graph, ControlFlowRegion region)
    {
        foreach (var local in region.Locals) _ownLocals.Add(local);
        foreach (var nested in region.NestedRegions) ScanRegions(graph, nested);
    }

    /// <summary>
    /// When an assignment's right-hand side contains control flow (`n = c ? a : b`), Roslyn captures
    /// the *target* before the branches to preserve evaluation order, then assigns through the
    /// capture. Those captures must alias the location, so we find them up front.
    /// </summary>
    private static void FindLvalueCaptures(ControlFlowGraph graph, HashSet<CaptureId> result)
    {
        foreach (var block in graph.Blocks)
        {
            IEnumerable<IOperation> roots = block.BranchValue is { } bv ? [.. block.Operations, bv] : block.Operations;
            foreach (var op in roots.SelectMany(r => r.DescendantsAndSelf()))
            {
                var target = op switch
                {
                    IAssignmentOperation a => a.Target,
                    IIncrementOrDecrementOperation i => i.Target,
                    IArgumentOperation { Parameter.RefKind: not RefKind.None } arg => arg.Value,
                    _ => null,
                };
                if (target is IFlowCaptureReferenceOperation r) result.Add(r.Id);
            }
        }
    }

    private static void FindArrayEnumerators(ControlFlowGraph graph, Dictionary<CaptureId, ITypeSymbol> result)
    {
        foreach (var capture in graph.Blocks.SelectMany(b => b.Operations).OfType<IFlowCaptureOperation>())
        {
            if (capture.Value is not IInvocationOperation { TargetMethod.Name: "GetEnumerator", Instance: { } instance }) continue;
            while (instance is IConversionOperation { Conversion.IsImplicit: true } c) instance = c.Operand;
            if (instance.Type is IArrayTypeSymbol { Rank: 1 } array) result[capture.Id] = array.ElementType;
        }
    }

    internal bool TryArrayEnumerator(IOperation? enumerator, out ITypeSymbol elementType)
    {
        elementType = null!;
        return enumerator is IFlowCaptureReferenceOperation r && _cfg!.ArrayEnumerators.TryGetValue(r.Id, out elementType!);
    }

    private LLVMBasicBlockRef Target(ControlFlowBranch branch) => ThroughFinally(branch, EnterScopes(branch, DirectTarget(branch)));

    /// <summary>
    /// A branch that leaves try regions must first run their finally blocks (innermost first). Each
    /// finally is emitted once; a trampoline stores which continuation to resume and jumps in.
    /// </summary>
    private LLVMBasicBlockRef ThroughFinally(ControlFlowBranch branch, LLVMBasicBlockRef dest)
    {
        var regions = branch.FinallyRegions;
        var target = dest;
        for (var i = regions.Length - 1; i >= 0; i--)
        {
            var region = regions[i];
            if (!_cfg!.Finally.TryGetValue(region, out var info))
                _cfg.Finally[region] = info = (_allocas.Alloca(L.I32, "finally.sel"), []);
            var index = info.Continuations.Count;
            info.Continuations.Add(target);
            var current = _b.InsertBlock;
            var trampoline = _b.AppendBlock(_fn.Fn, "to.finally");
            _b.PositionAtEnd(trampoline);
            _b.Store(L.ConstInt(L.I32, index), info.Selector);
            _b.Br(_cfg.Blocks[_cfg.Graph.Blocks[region.FirstBlockOrdinal]]);
            _b.PositionAtEnd(current);
            target = trampoline;
        }
        return target;
    }

    private static ControlFlowRegion? EnclosingFinally(BasicBlock block)
    {
        for (var r = block.EnclosingRegion; r is not null; r = r.EnclosingRegion)
            if (r.Kind == ControlFlowRegionKind.Finally) return r;
        return null;
    }

    private LLVMBasicBlockRef DirectTarget(ControlFlowBranch branch)
    {
        // The false edge of a `when` filter leaves the filter region: try the next handler.
        if (branch is { Destination: null, Semantics: ControlFlowBranchSemantics.StructuredExceptionHandling }
            && branch.Source.EnclosingRegion?.Kind == ControlFlowRegionKind.Filter)
            return FilterRejected(branch.Source);
        var dest = branch.Destination ?? throw CompileError.Unsupported($"branch with {branch.Semantics} semantics");
        if (dest.Kind != BasicBlockKind.Exit) return _cfg!.Blocks[dest];

        // Falling into the exit block: continue to the next prologue graph, or return void.
        if (_cfg!.Exit.IsNull)
        {
            var current = _b.InsertBlock;
            _cfg.Exit = _b.AppendBlock(_fn.Fn, "exit");
            _b.PositionAtEnd(_cfg.Exit);
            if (_cfg.Continuation is { } next) _b.Br(next);
            else if (ReturnType is null or { SpecialType: SpecialType.System_Void }) _b.RetVoid();
            else _b.Unreachable();
            _b.PositionAtEnd(current);
        }
        return _cfg.Exit;
    }

    private void EmitTerminator(BasicBlock block)
    {
        if (block.ConditionKind != ControlFlowConditionKind.None)
        {
            var condition = EmitValue(block.BranchValue!);
            var whenCondition = Target(block.ConditionalSuccessor!);
            var otherwise = Target(block.FallThroughSuccessor!);
            if (block.ConditionKind == ControlFlowConditionKind.WhenTrue) _b.CondBr(condition, whenCondition, otherwise);
            else _b.CondBr(condition, otherwise, whenCondition);
            return;
        }

        var next = block.FallThroughSuccessor!;
        switch (next.Semantics)
        {
            case ControlFlowBranchSemantics.Regular:
                _b.Br(Target(next));
                break;
            case ControlFlowBranchSemantics.Return:
                EmitReturn(block.BranchValue, next);
                break;
            case ControlFlowBranchSemantics.StructuredExceptionHandling when EnclosingFinally(block) is { } finallyRegion:
                _cfg!.FinallyEnds.Add((_b.InsertBlock, finallyRegion)); // terminated once all entries are known
                break;
            case ControlFlowBranchSemantics.Throw:
                EmitThrow(block.BranchValue);
                break;
            case ControlFlowBranchSemantics.Rethrow:
                EmitRethrow(block.BranchValue);
                break;
            case ControlFlowBranchSemantics.ProgramTermination or ControlFlowBranchSemantics.Error:
                _b.Unreachable();
                break;
            default:
                throw CompileError.Unsupported($"control flow with {next.Semantics} semantics", block.BranchValue);
        }
    }

    private void EmitReturn(IOperation? value, ControlFlowBranch branch)
    {
        var isVoid = value is null || ReturnType is null or { SpecialType: SpecialType.System_Void };
        if (_cfg!.Continuation is { } next || isVoid)
        {
            if (value is not null) EmitValue(value);
            if (_cfg.Continuation is { } cont) _b.Br(ThroughFinally(branch, cont));
            else if (branch.FinallyRegions.IsEmpty) _b.RetVoid();
            else _b.Br(ThroughFinally(branch, DirectTarget(branch)));
            return;
        }
        var result = Coerce(EmitValue(value!), value!.Type, ReturnType!);
        if (branch.FinallyRegions.IsEmpty) { _b.Ret(result); return; }

        // `return x;` inside try/finally: x is evaluated before the finally runs.
        _retSlot = _retSlot.IsNull ? _allocas.Alloca(result.Type, "retval") : _retSlot;
        _b.Store(result, _retSlot);
        if (_retBlock.IsNull)
        {
            var current = _b.InsertBlock;
            _retBlock = _b.AppendBlock(_fn.Fn, "return");
            _b.PositionAtEnd(_retBlock);
            _b.Ret(_b.Load(result.Type, _retSlot));
            _b.PositionAtEnd(current);
        }
        _b.Br(ThroughFinally(branch, _retBlock));
    }

    private LLVMValueRef _retSlot;
    private LLVMBasicBlockRef _retBlock;

    private void EmitStatement(IOperation operation)
    {
        if (operation is IFlowCaptureOperation { Value.Type: { } lvalueType } lvalue && _cfg!.LvalueCaptures.Contains(lvalue.Id))
        {
            var address = AddressOf(lvalue.Value);
            _cfg.Captures[lvalue.Id] = (_allocas.Alloca(L.Ptr, "capture.addr"), lvalueType);
            _b.Store(address, _cfg.Captures[lvalue.Id].Slot);
            return;
        }
        if (operation is IFlowCaptureOperation capture)
        {
            var value = EmitValue(capture.Value);
            var (slot, type) = CaptureSlot(capture.Id, capture.Value.Type);
            if (type is not null && capture.Value.Type is not null) value = Coerce(value, capture.Value.Type, type);
            StoreValue(type is null ? value : ToStorage(value, type), slot);
            return;
        }
        EmitValue(operation);
    }

    private (LLVMValueRef Slot, ITypeSymbol? Type) CaptureSlot(CaptureId id, ITypeSymbol? type)
    {
        if (_cfg!.Captures.TryGetValue(id, out var existing)) return existing;
        var slot = _allocas.Alloca(type is null ? L.Ptr : T.Storage(type), "capture");
        return _cfg.Captures[id] = (slot, type);
    }

    // ======================================================================================
    // Storage helpers
    // ======================================================================================

    internal LLVMValueRef LoadValue(LLVMValueRef address, ITypeSymbol type)
    {
        var value = _b.Load(T.Storage(type), address);
        return type.SpecialType == SpecialType.System_Boolean ? _b.Cast(LLVMOpcode.Trunc, value, L.I1) : value;
    }

    /// <summary>Converts an SSA value to its storage representation (bool: i1 -> i8).</summary>
    internal LLVMValueRef ToStorage(LLVMValueRef value, ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Boolean && value.Type.IntWidth == 1 ? _b.Cast(LLVMOpcode.ZExt, value, L.I8) : value;

    internal void StoreValue(LLVMValueRef storageValue, LLVMValueRef address) => _b.Store(storageValue, address);

    internal LLVMValueRef LocalSlot(ILocalSymbol local)
    {
        if (CapturedAddress(local) is { } captured) return captured;
        if (_slots.TryGetValue(local, out var slot)) return slot;
        if (local.IsRef) throw CompileError.Unsupported($"ref local '{local.Name}'", local);
        if (!_ownLocals.Contains(local))
            throw CompileError.Unsupported($"closure: local function or lambda capturing '{local.Name}'", local);
        return _slots[local] = _allocas.Alloca(T.Storage(local.Type), local.Name);
    }

    internal LLVMValueRef ParameterSlot(IParameterSymbol parameter, IOperation at)
    {
        if (CapturedAddress(parameter) is { } captured) return captured;
        if (_slots.TryGetValue(parameter, out var slot)) return slot;
        // A primary-constructor parameter referenced from a member: read its captured field.
        if (CapturedParameterField(parameter) is { } field)
            return FieldAddressOn(ThisValue(at), field);
        throw CompileError.Unsupported($"closure: local function or lambda capturing parameter '{parameter.Name}'", at);
    }

    internal IFieldSymbol BackingField(IPropertySymbol property) =>
        property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property))
        ?? throw CompileError.Unsupported($"property '{property.Name}' without accessor or backing field", property);

    internal LLVMValueRef FieldAddressOn(LLVMValueRef instance, IFieldSymbol field)
    {
        var layout = _m.GetLayout(field.ContainingType);
        return _b.StructGep(layout.InstanceType, instance, layout.FieldIndex[field.OriginalDefinition], field.Name);
    }

    // ======================================================================================
    // Runtime checks — explicit, because LLVM treats null derefs / sdiv-by-zero as UB
    // ======================================================================================

    private LLVMBasicBlockRef ThrowBlock(string helper)
    {
        // One shared block per helper *and* landing pad: inside a try the throw must be an invoke.
        var key = $"{helper}@{_b.UnwindDest.Handle}";
        if (_throwBlocks.TryGetValue(key, out var block)) return block;
        var current = _b.InsertBlock;
        block = _b.AppendBlock(_fn.Fn, helper["csrt_".Length..]);
        _b.PositionAtEnd(block);
        var f = _m.Runtime(helper, L.Void);
        _b.Call(f.Type, f.Fn, []);
        _b.Unreachable();
        _b.PositionAtEnd(current);
        return _throwBlocks[key] = block;
    }

    /// <summary>Branches to a shared throw block when <paramref name="failed"/> is true.</summary>
    internal void FailIf(LLVMValueRef failed, string helper)
    {
        var ok = _b.AppendBlock(_fn.Fn, "ok");
        _b.CondBr(failed, ThrowBlock(helper), ok);
        _b.PositionAtEnd(ok);
    }

    internal void NullCheck(LLVMValueRef reference) =>
        FailIf(_b.ICmp(LLVMIntPredicate.EQ, reference, LlvmContext.Null(L.Ptr)), "csrt_throw_null_reference");

    internal LLVMValueRef CallRuntime(string name, LLVMTypeRef ret, LLVMTypeRef[] parameterTypes, params LLVMValueRef[] args)
    {
        var f = _m.Runtime(name, ret, parameterTypes);
        return _b.Call(f.Type, f.Fn, args);
    }

    internal LLVMBasicBlockRef NewBlock(string name) => _b.AppendBlock(_fn.Fn, name);

    internal LLVMValueRef NewTemp(LLVMTypeRef type, string name) => _allocas.Alloca(type, name);
}
