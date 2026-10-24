using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Csharpc.CodeGen;

/// <summary>
/// Roslyn's control-flow graphs, the input of lowering (docs/adr/0014-compile-time.md).
///
/// Binding a method body and building its CFG is most of the cost of lowering corelib and generic
/// code, and it is pure Roslyn work: thread-safe, and independent of everything lowering decides.
/// So each graph is built once per syntax node (every instantiation of a generic method reuses it),
/// and methods queued for lowering get their graphs built ahead of time on other threads, in queue
/// order, while the lowering thread works through the queue. The lowering thread takes a finished
/// graph, waits for one in progress, or builds it itself; the graphs, and therefore the output, are
/// the same whichever thread built them.
/// </summary>
public sealed partial class ModuleCodeGen
{
    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _models = new();
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<ControlFlowGraph?>> _cfgs = new();
    private BlockingCollection<SyntaxNode>? _prefetchQueue;
    private Thread[] _prefetchThreads = [];

    public SemanticModel ModelFor(SyntaxTree tree) => _models.GetOrAdd(tree, t => Compilation.GetSemanticModel(t));

    /// <summary>
    /// Roslyn's CFG of a body, built once per syntax node: a CFG is immutable and independent of the
    /// substitution it is lowered under (per-function state lives in FunctionCodeGen/ClosureAnalysis).
    /// </summary>
    internal ControlFlowGraph? CfgFor(SyntaxNode syntax)
    {
        var start = Stopwatch.GetTimestamp();
        Stats.CfgRequests++;
        try { return CfgLazy(syntax).Value; }
        finally { Stats.CfgWaitTicks += Stopwatch.GetTimestamp() - start; }
    }

    private Lazy<ControlFlowGraph?> CfgLazy(SyntaxNode syntax) =>
        _cfgs.GetOrAdd(syntax, node => new Lazy<ControlFlowGraph?>(() => BuildCfg(node), LazyThreadSafetyMode.ExecutionAndPublication));

    private ControlFlowGraph? BuildCfg(SyntaxNode syntax)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            var model = ModelFor(syntax.SyntaxTree);
            // A primary constructor's type declaration only has a body operation when it calls a base constructor.
            return syntax is TypeDeclarationSyntax && model.GetOperation(syntax) is null ? null : ControlFlowGraph.Create(syntax, model);
        }
        finally
        {
            Interlocked.Increment(ref Stats.CfgBuilt);
            Interlocked.Add(ref Stats.CfgTicks, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Starts building the CFG of a method just queued for lowering (the same body EmitMethodCore lowers).</summary>
    private void PrefetchCfg(IMethodSymbol method)
    {
        if (Driver.Parallelism.Threads < 2 || CfgDump is not null) return;
        var definition = method.OriginalDefinition;
        var syntax = (definition.PartialImplementationPart ?? definition).DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is not (MethodDeclarationSyntax { Body: not null } or MethodDeclarationSyntax { ExpressionBody: not null }
            or ConstructorDeclarationSyntax or AccessorDeclarationSyntax { Body: not null } or AccessorDeclarationSyntax { ExpressionBody: not null }
            or ArrowExpressionClauseSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax))
            return;
        if (_cfgs.ContainsKey(syntax)) return;
        if (_prefetchQueue is null)
        {
            _prefetchQueue = [];
            _prefetchThreads = [.. Enumerable.Range(0, Driver.Parallelism.Threads - 1).Select(i => new Thread(PrefetchWorker, maxStackSize: 16 << 20)
            {
                IsBackground = true,
                Name = $"csharpc-cfg-{i}",
            })];
            foreach (var thread in _prefetchThreads) thread.Start();
            Stats.CfgThreads = _prefetchThreads.Length + 1;
        }
        _prefetchQueue.Add(syntax);
    }

    private void PrefetchWorker()
    {
        foreach (var syntax in _prefetchQueue!.GetConsumingEnumerable())
        {
            if (_prefetchStopped) break;
            // Failures surface on the lowering thread, from the same Lazy, if it ever needs this graph.
            try { _ = CfgLazy(syntax).Value; }
            catch (Exception) { }
        }
    }

    private volatile bool _prefetchStopped;

    /// <summary>Lowering is done: drop the graphs nobody started and wait for the ones in progress.</summary>
    private void StopPrefetch()
    {
        if (_prefetchQueue is null) return;
        _prefetchStopped = true;
        _prefetchQueue.CompleteAdding();
        foreach (var thread in _prefetchThreads) thread.Join();
        _prefetchQueue.Dispose();
        _prefetchQueue = null;
    }
}
