using Csharpc.Llvm;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Csharpc.CodeGen;

/// <summary>
/// Stack-trace recording (docs/adr/0012-stack-traces.md). In a program that observes stack traces,
/// every call that may throw in a function of the program's own code becomes an `invoke` whose
/// landing pad belongs to that call site alone, with the site's descriptor as its first clause:
///
///   site.N:  %lp = landingpad { ptr, i32 } catch @csharpc.trace.site.N  &lt;the clauses of the try it is in&gt;
///            br %continue                                 ; phis: %lp, the site
///   continue: resume %lp  |  csrt_begin_catch + the try's dispatch  |  the coroutine's fault path
///
/// The personality records each descriptor it meets during the search phase, which walks from the
/// throw point to the catching frame: .NET's frame list. Record-only pads have no cleanup, so the
/// cleanup phase doesn't stop there; when LLVM inlines a function, the inlined pads get the caller's
/// clauses appended, so inlined frames are recorded too, in order. On wasm32 (no unwinder) the pad
/// code records the site instead (csrt_trace_frame), since every pad runs on the way out.
/// </summary>
public sealed partial class FunctionCodeGen
{
    /// <summary>This function's frame in the stack-trace tables; -1 when it records nothing.</summary>
    private readonly int _traceFrame = -1;
    /// <summary>The operation being lowered: its sequence point gives the line of a call site.</summary>
    private SyntaxNode? _traceAt;
    /// <summary>While lowering `throw;`: .NET doesn't list the rethrowing frame a second time.</summary>
    private bool _traceSuppressed;
    // Finally regions are ControlFlowRegions in source and IlClauses in IL.
    private readonly Dictionary<(nint Body, int Site, object? Leaving), LLVMBasicBlockRef> _sitePads = [];
    private readonly Dictionary<nint, SitePadTarget> _sitePadTargets = [];
    private SitePadTarget? _traceResume;

    /// <summary>Where the per-site pads of one landing pad (or of none: record and resume) lead.</summary>
    private sealed record SitePadTarget(List<LLVMValueRef> Clauses, bool Cleanup, LLVMBasicBlockRef Body, LLVMValueRef Pad, LLVMValueRef Site);

    private bool Traced => _traceFrame >= 0;

    private static int TraceFrameOf(ModuleCodeGen module, IMethodSymbol? method) =>
        module.StackTraces is not { } traces || method is null || !module.IsUserMethod(method) ? -1
        // The program given as IL (ADR 0010): every method of its assembly is a frame, named from metadata.
        : module.MainIl is { } main ? (module.Il.OwnerOf(method) == main ? traces.IlFrameOf(method) : -1)
        : module.Il.OwnerOf(method) is null ? traces.FrameOf(method) : -1;

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
                // No cleanup: only wasm32's explicit propagation ever enters these pads.
                _traceResume = NewSitePadTarget([], cleanup: false, "trace.record");
                // LLVM deletes landing pads that only resume, catch clauses and all; this volatile
                // store (never executed on native targets: phase 2 doesn't stop here) keeps the pad.
                if (!T.Target.IsWasm) Native.LLVMSetVolatile(_b.Store(L.ConstInt(L.I8, 0), _m.TracePadAnchor()), 1);
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
        // The site's clause comes first: the personality records it during the search phase, before
        // it looks for a handler among the clauses that follow (eh.c, "Stack traces").
        var descriptor = site >= 0 ? _m.StackTraces!.SiteDescriptor(_m, site) : LlvmContext.Null(L.Ptr);
        List<LLVMValueRef> clauses = site >= 0 ? [descriptor, .. target.Clauses] : target.Clauses;
        var lp = _b.LandingPad(LandingPadType, _m.Runtime("csrt_personality", L.I32).Fn, clauses, target.Cleanup || leaving is not null);
        var recorded = descriptor;
        if (leaving is not null)
        {
            // An exception leaving a finally block that runs on the normal path: CoreCLR lists the
            // method twice, for the finally funclet (this site) and for the method's own frame, at
            // the location of FinallyParentSite. During unwinding (the finally runs because of
            // another exception) the method appears once.
            var normal = _b.Load(L.I1, FinallyNormalFlag(leaving), "finally.normal");
            var parent = _b.Select(normal, _m.StackTraces!.SiteDescriptor(_m, FinallyParentSite(leaving)), LlvmContext.Null(L.Ptr));
            CallRuntime("csrt_trace_finally", L.Void, [L.Ptr, L.Ptr, L.Ptr], _b.ExtractValue(lp, 0), recorded, parent);
            recorded = LlvmContext.Null(L.Ptr);
        }
        _b.Br(target.Body);
        IrBuilder.AddIncoming(target.Pad, lp, _b.InsertBlock);
        IrBuilder.AddIncoming(target.Site, recorded, _b.InsertBlock);
        _b.PositionAtEnd(current);
        _b.UnwindHook = hook;
        return _sitePads[(target.Body.Handle, site, leaving)] = pad;
    }

    private readonly Dictionary<object, LLVMValueRef> _finallyNormal = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _finallyParentSite = new(ReferenceEqualityComparer.Instance);

    /// <summary>Per finally region of a traced function: true while it runs on the normal path (not unwinding).</summary>
    private LLVMValueRef FinallyNormalFlag(object finallyRegion) =>
        _finallyNormal.TryGetValue(finallyRegion, out var flag) ? flag : _finallyNormal[finallyRegion] = _allocas.Alloca(L.I1, "finally.normal");

    /// <summary>Records how a finally block was entered (ThroughFinally: normally; Dispatch: unwinding).</summary>
    private void NoteFinallyEntry(object finallyRegion, bool normal)
    {
        if (Traced) _b.Store(normal ? L.True : L.False, FinallyNormalFlag(finallyRegion));
    }

    /// <summary>
    /// The finally region an exception thrown at the current call leaves (no try inside it is in
    /// the way), or null.
    /// </summary>
    private object? FinallyLeftFrom()
    {
        if (_il is { CurrentBlock: { } ilBlock })
            // IL: the innermost handler is a finally, and no try inside that finally protects the instruction.
            return ilBlock.Handler is { Kind: System.Reflection.Metadata.ExceptionRegionKind.Finally } finallyClause
                && !(ilBlock.Try is { } t && t.Start >= finallyClause.HandlerStart && t.End <= finallyClause.HandlerEnd)
                ? finallyClause : null;
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
    private int FinallyParentSite(object region)
    {
        if (_finallyParentSite.TryGetValue(region, out var known)) return known;
        // IL: the same rule over the PDB's sequence points (the method's first one; the try's first one when optimized).
        if (region is IlClause clause)
            return _finallyParentSite[region] = _m.StackTraces!.IlFirstSite(_traceFrame, _method!, _m.StackTraces.Release ? clause.Try.Start : 0);
        var finallyRegion = (ControlFlowRegion)region;
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
        var site = _b.Phi(L.Ptr, "site");
        // Native targets record in the personality's search phase; wasm32 has none (eh.c).
        if (T.Target.IsWasm) CallRuntime("csrt_trace_frame", L.Void, [L.Ptr, L.Ptr], _b.ExtractValue(lp, 0), site);
        var target = new SitePadTarget(clauses, cleanup, body, lp, site);
        _sitePadTargets[body.Handle] = target;
        return target;
    }

    private int CurrentTraceSite() => _il is { } il
        ? _m.StackTraces!.IlSite(_traceFrame, _method!, il.Current.Offset)
        : _m.StackTraces!.Site(_traceFrame, _traceAt ?? DeclarationSyntax(), _traceAtEnd);

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
