using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Stack-trace recording (docs/adr/0012-stack-traces.md). In a program that observes stack traces,
/// every call that may throw in a function of the program's own code becomes an `invoke` whose
/// landing pad belongs to that call site alone:
///
///   site.N:  %lp = landingpad { ptr, i32 } &lt;the clauses of the try it is in&gt; cleanup
///            br %record                                   ; phis: %lp, i32 N
///   record:  call csrt_trace_frame(%lp.0, %site)          ; append the site to the exception's frames
///            resume %lp  |  csrt_begin_catch + the try's dispatch  |  the coroutine's fault path
///
/// Phase 2 of the unwind runs these pads from the throw point outward and stops at the catching
/// frame, so the recorded list is .NET's frame list. A pad is just a constant and a jump; the shared
/// block does the work. When LLVM inlines a function, its call sites keep their own pads (and their
/// `resume` becomes a jump to the caller's pad), so inlined frames are recorded too.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>This function's frame in the stack-trace tables; -1 when it records nothing.</summary>
    private readonly int _traceFrame = -1;
    /// <summary>The operation being lowered: its sequence point gives the line of a call site.</summary>
    private SyntaxNode? _traceAt;
    /// <summary>While lowering `throw;`: .NET doesn't list the rethrowing frame a second time.</summary>
    private bool _traceSuppressed;
    private readonly Dictionary<(nint Body, int Site, ControlFlowRegion? Leaving), LLVMBasicBlockRef> _sitePads = [];
    private readonly Dictionary<nint, SitePadTarget> _sitePadTargets = [];
    private SitePadTarget? _traceResume;

    /// <summary>Where the per-site pads of one landing pad (or of none: record and resume) lead.</summary>
    private sealed record SitePadTarget(List<LLVMValueRef> Clauses, bool Cleanup, LLVMBasicBlockRef Body, LLVMValueRef Pad, LLVMValueRef Site);

    private bool Traced => _traceFrame >= 0;

    private static int TraceFrameOf(ModuleCodeGen module, IMethodSymbol? method) =>
        module.StackTraces is { } traces && method is not null && module.IsUserMethod(method) && module.Il.OwnerOf(method) is null
            ? traces.FrameOf(method) : -1;

    /// <summary>Calls lowered from the CFG get per-site pads; synthesized scaffolding doesn't.</summary>
    private void EnableTraceHook() => _b.UnwindHook = Traced ? SitePad : null;

    private LLVMBasicBlockRef SitePad(LLVMBasicBlockRef unwindDest)
    {
        var site = _traceSuppressed ? -1 : CurrentTraceSite();
        SitePadTarget? target;
        if (unwindDest.IsNull)
        {
            if (site < 0) return default; // `throw;` outside any try: unwind straight into the caller
            if (_traceResume is null)
            {
                var (savedBlock, savedUnwind, savedHook) = (_b.InsertBlock, _b.UnwindDest, _b.UnwindHook);
                (_b.UnwindDest, _b.UnwindHook) = (default, null);
                _traceResume = NewSitePadTarget([], cleanup: true, "trace.record");
                _b.Resume(_traceResume.Pad);
                _b.PositionAtEnd(savedBlock);
                (_b.UnwindDest, _b.UnwindHook) = (savedUnwind, savedHook);
            }
            target = _traceResume;
        }
        else if (!_sitePadTargets.TryGetValue(unwindDest.Handle, out target)) return unwindDest;
        var leaving = site >= 0 ? FinallyLeftFrom() : null;
        if (_sitePads.TryGetValue((target.Body.Handle, site, leaving), out var pad)) return pad;

        var (current, hook) = (_b.InsertBlock, _b.UnwindHook);
        _b.UnwindHook = null;
        pad = NewBlock("site");
        _b.PositionAtEnd(pad);
        var lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, target.Clauses, target.Cleanup);
        var recorded = L.ConstInt(L.I32, site);
        if (leaving is not null)
        {
            // An exception leaving a finally block that runs on the normal path: CoreCLR lists the
            // method twice, for the finally funclet (this site) and for the method's own frame, at
            // the location of FinallyParentSite. During unwinding (the finally runs because of
            // another exception) the method appears once.
            CallRuntime("csrt_trace_frame", L.Void, [L.Ptr, L.I32], _b.ExtractValue(lp, 0), recorded);
            var normal = _b.Load(L.I1, FinallyNormalFlag(leaving), "finally.normal");
            recorded = _b.Select(normal, L.ConstInt(L.I32, FinallyParentSite(leaving)), L.ConstInt(L.I32, -1));
        }
        _b.Br(target.Body);
        IrBuilder.AddIncoming(target.Pad, lp, _b.InsertBlock);
        IrBuilder.AddIncoming(target.Site, recorded, _b.InsertBlock);
        _b.PositionAtEnd(current);
        _b.UnwindHook = hook;
        return _sitePads[(target.Body.Handle, site, leaving)] = pad;
    }

    private readonly Dictionary<ControlFlowRegion, LLVMValueRef> _finallyNormal = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ControlFlowRegion, int> _finallyParentSite = new(ReferenceEqualityComparer.Instance);

    /// <summary>Per finally region of a traced function: true while it runs on the normal path (not unwinding).</summary>
    private LLVMValueRef FinallyNormalFlag(ControlFlowRegion finallyRegion) =>
        _finallyNormal.TryGetValue(finallyRegion, out var flag) ? flag : _finallyNormal[finallyRegion] = _allocas.Alloca(L.I1, "finally.normal");

    /// <summary>Records how a finally block was entered (ThroughFinally: normally; Dispatch: unwinding).</summary>
    private void NoteFinallyEntry(ControlFlowRegion finallyRegion, bool normal)
    {
        if (Traced) _b.Store(normal ? L.True : L.False, FinallyNormalFlag(finallyRegion));
    }

    /// <summary>
    /// The finally region an exception thrown at the current call leaves (no try inside it is in
    /// the way), or null.
    /// </summary>
    private ControlFlowRegion? FinallyLeftFrom()
    {
        if (_currentBlock is null || _cfg is null) return null;
        ControlFlowRegion? finallyRegion = null;
        for (var r = _currentBlock.EnclosingRegion; r is not null && !ReferenceEquals(r, _cfg.Graph.Root); r = r.EnclosingRegion)
            if (r.Kind == ControlFlowRegionKind.Finally) { finallyRegion = r; break; }
        if (finallyRegion is null) return null;
        for (var t = ProtectingTry(_currentBlock.EnclosingRegion); t is not null; t = t.EnclosingRegion)
            if (ReferenceEquals(t, finallyRegion)) return null; // a try inside the finally comes first
        return finallyRegion;
    }

    /// <summary>
    /// Where CoreCLR reports the method's own frame when an exception leaves a finally block run on
    /// the normal path. Debug code: the method's first sequence point (its opening brace). Optimized
    /// code: the try block's first statement (for using/lock the statement, for foreach the collection
    /// expression), the IL offset its frame is left at.
    /// </summary>
    private int FinallyParentSite(ControlFlowRegion finallyRegion)
    {
        if (_finallyParentSite.TryGetValue(finallyRegion, out var known)) return known;
        SyntaxNode? at = null;
        if (_m.StackTraces!.Release)
        {
            var first = _cfg!.Graph.Blocks[finallyRegion.FirstBlockOrdinal];
            var owner = (first.Operations.FirstOrDefault() ?? first.BranchValue)?.Syntax.AncestorsAndSelf().FirstOrDefault(n =>
                n is TryStatementSyntax or UsingStatementSyntax or CommonForEachStatementSyntax or LockStatementSyntax
                    or LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 });
            at = owner switch
            {
                TryStatementSyntax t => t.Block.Statements.FirstOrDefault() ?? (SyntaxNode)t.Block,
                CommonForEachStatementSyntax f => f.Expression,
                _ => owner,
            };
        }
        at ??= BodyStart();
        return _finallyParentSite[finallyRegion] = _m.StackTraces.Site(_traceFrame, at);
    }

    private SyntaxNode BodyStart()
    {
        var declaration = DeclarationSyntax();
        return declaration switch
        {
            BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
            AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax l => (SyntaxNode?)l.Body ?? l.ExpressionBody?.Expression,
            AnonymousFunctionExpressionSyntax f => f.Body,
            ArrowExpressionClauseSyntax a => a.Expression,
            CompilationUnitSyntax c => c.Members.OfType<GlobalStatementSyntax>().FirstOrDefault(),
            _ => null,
        } ?? declaration;
    }

    /// <summary>
    /// A block reached from per-site pads, with phis for the landingpad value and the site, that
    /// records the frame; the builder is left at its end for the code that follows (resume, the try's
    /// dispatch, the coroutine's fault path). Callers suspend the unwind hook while emitting it.
    /// </summary>
    private SitePadTarget NewSitePadTarget(List<LLVMValueRef> clauses, bool cleanup, string name)
    {
        EnsurePersonality();
        var body = NewBlock(name);
        _b.PositionAtEnd(body);
        var lp = _b.Phi(LandingPadType, "lp");
        var site = _b.Phi(L.I32, "site");
        CallRuntime("csrt_trace_frame", L.Void, [L.Ptr, L.I32], _b.ExtractValue(lp, 0), site);
        var target = new SitePadTarget(clauses, cleanup, body, lp, site);
        _sitePadTargets[body.Handle] = target;
        return target;
    }

    private int CurrentTraceSite() => _m.StackTraces!.Site(_traceFrame, _traceAt ?? DeclarationSyntax(), _traceAtEnd);

    /// <summary>Set with <see cref="_traceAt"/>: the location is the node's last token.</summary>
    private bool _traceAtEnd;

    /// <summary>
    /// Where the statement-level operation <paramref name="at"/> is reported. The implicit Dispose()
    /// of a `using` belongs to the end of its scope (the closing brace's sequence point), although
    /// Roslyn gives it the resource declaration's syntax.
    /// </summary>
    private (SyntaxNode Node, bool AtEnd) TraceLocation(IOperation at)
    {
        var call = at switch
        {
            IInvocationOperation invocation => invocation,
            IAwaitOperation { Operation: IInvocationOperation awaited } => awaited,
            _ => null,
        };
        if (call is { IsImplicit: true, TargetMethod.Name: "Dispose" or "DisposeAsync" })
            foreach (var node in at.Syntax.AncestorsAndSelf())
            {
                if (node is UsingStatementSyntax { Statement: { } body }) return (body, true);
                if (node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0, Parent: { } scope }) return (scope, true);
                if (node is StatementSyntax) break;
            }
        return (at.Syntax, false);
    }

    private SyntaxNode DeclarationSyntax() =>
        _method?.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
        ?? throw new InvalidOperationException("traced function without syntax");
}
